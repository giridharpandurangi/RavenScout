using System.Collections.Generic;
using UnityEngine;

namespace RavenScout
{
    /// <summary>
    /// Multiplayer plumbing. The server tracks trips: only it has the whole world's location list and objects,
    /// so when a raven is due back it works out what the raven saw, sends that to the player who sent it, and
    /// marks the trip as reported on the roost (through the roost's owner, as only an object's owner should
    /// change it). In single player the game is its own server, so the same code simply runs locally.
    /// </summary>
    internal static class ScoutNet
    {
        private const string RpcTripStarted = "RavenScout_TripStarted";
        private const string RpcResult = "RavenScout_Result";
        private const string RpcMarkReported = "RavenScout_MarkReported";
        private const string RpcServerMark = "RavenScout_ServerMark";
        private const string RpcTakeOff = "RavenScout_TakeOff";

        private const float TakeOffViewDistance = 150f;
        private const float RetryMarkSeconds = 20f;

        /// <summary>Roosts with a raven out (or back but not yet reported), with when the server started watching.</summary>
        private static readonly Dictionary<ZDOID, float> s_watched = new Dictionary<ZDOID, float>();
        /// <summary>The "trip started" message can arrive before the roost's new data; don't give up on it too soon.</summary>
        private const float WatchGraceSeconds = 30f;
        /// <summary>Trips already reported this session, with when, in case the roost's owner never saved it.</summary>
        private static readonly Dictionary<string, float> s_reported = new Dictionary<string, float>();
        private static bool s_scannedWorld;
        private static float s_nextTick;

        /// <summary>Called when a world starts (Game.Start), once ZRoutedRpc exists.</summary>
        public static void Register()
        {
            var rpc = ZRoutedRpc.instance;
            if (rpc == null)
            {
                return;
            }
            s_watched.Clear();
            s_reported.Clear();
            s_scannedWorld = false;

            rpc.Register<ZDOID>(RpcTripStarted, OnTripStarted);
            rpc.Register<ZPackage>(RpcResult, OnResult);
            rpc.Register<ZDOID, string, string>(RpcMarkReported, OnMarkReported);
            rpc.Register<ZDOID, string, string>(RpcServerMark, OnServerMark);
            rpc.Register<Vector3, Vector3, ZPackage>(RpcTakeOff, OnTakeOff);
        }

        // ------------------------------------------------------------ trips (server)

        /// <summary>Tells the server a roost has a raven out, so it watches for its return.</summary>
        public static void TripStarted(ZDOID roost) => ZRoutedRpc.instance?.InvokeRoutedRPC(RpcTripStarted, roost);

        private static void OnTripStarted(long sender, ZDOID roost)
        {
            if (Scouting.IsServer)
            {
                s_watched[roost] = Time.time;
            }
        }

        /// <summary>Server housekeeping, every second: report ravens that are back.</summary>
        public static void ServerTick()
        {
            if (!Scouting.IsServer || Time.time < s_nextTick)
            {
                return;
            }
            s_nextTick = Time.time + 1f;

            if (!s_scannedWorld)
            {
                // After a restart, pick up ravens that were still out or not yet reported.
                s_scannedWorld = true;
                foreach (var zdo in Scouting.GetAllRoostZdos())
                {
                    var trip = Scouting.ReadTrip(zdo);
                    if (trip != null && !trip.Reported)
                    {
                        s_watched[zdo.m_uid] = Time.time;
                    }
                }
            }

            if (s_watched.Count == 0)
            {
                return;
            }
            double now = Scouting.Now;
            foreach (var pair in new List<KeyValuePair<ZDOID, float>>(s_watched))
            {
                var id = pair.Key;
                var zdo = ZDOMan.instance.GetZDO(id);
                var trip = zdo != null && zdo.IsValid() ? Scouting.ReadTrip(zdo) : null;
                if (trip == null || trip.Reported)
                {
                    if (Time.time - pair.Value > WatchGraceSeconds)
                    {
                        s_watched.Remove(id);
                    }
                    continue;
                }
                if (trip.IsAway(now))
                {
                    continue;
                }
                string key = trip.Key(id);
                if (s_reported.TryGetValue(key, out float at) && Time.time - at < RetryMarkSeconds)
                {
                    continue; // waiting for the roost's owner to save it
                }
                // The findings go to the sender; if they're offline, the raven waits with its news until they're back.
                if (!IsOnline(trip.Sender))
                {
                    continue;
                }

                var result = Scouting.Survey(trip, key);
                Plugin.Log.LogInfo($"Raven back for {trip.Sender}: {result.Report} ({result.Findings.Count} findings)");
                SendResult(trip.Sender, result);
                s_reported[key] = Time.time;
                MarkReported(zdo, key, result.Report);
            }
        }

        private static bool IsOnline(string playerName)
        {
            if (Player.m_localPlayer && Player.m_localPlayer.GetPlayerName() == playerName)
            {
                return true;
            }
            foreach (var peer in ZNet.instance.GetPeers())
            {
                if (peer.m_playerName == playerName)
                {
                    return true;
                }
            }
            return false;
        }

        private static void SendResult(string playerName, TripResult result)
        {
            // The host's own player isn't in the peer list.
            if (Player.m_localPlayer && Player.m_localPlayer.GetPlayerName() == playerName)
            {
                ScoutMap.Apply(result);
            }
            foreach (var peer in ZNet.instance.GetPeers())
            {
                if (peer.m_playerName == playerName)
                {
                    ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, RpcResult, Scouting.Serialize(result));
                }
            }
        }

        private static void OnResult(long sender, ZPackage pkg)
        {
            ScoutMap.Apply(Scouting.Deserialize(pkg));
        }

        /// <summary>
        /// Only the owner of an object should change it, or one of the two changes can be lost. If a player
        /// owns the roost (they're near it), they save the report; otherwise the server does.
        /// </summary>
        private static void MarkReported(ZDO zdo, string key, string report)
        {
            long owner = zdo.GetOwner();
            if (owner != 0 && owner != ZDOMan.GetSessionID() && ZNet.instance.GetPeer(owner) != null)
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(owner, RpcMarkReported, zdo.m_uid, key, report);
                return;
            }
            SaveReport(zdo, key, report);
        }

        private static void OnMarkReported(long sender, ZDOID id, string key, string report)
        {
            var zdo = ZDOMan.instance.GetZDO(id);
            if (zdo != null && zdo.IsOwner())
            {
                SaveReport(zdo, key, report);
                return;
            }
            // Ownership moved on in the meantime: let the server save it.
            ZRoutedRpc.instance.InvokeRoutedRPC(RpcServerMark, id, key, report);
        }

        private static void OnServerMark(long sender, ZDOID id, string key, string report)
        {
            if (!Scouting.IsServer)
            {
                return;
            }
            var zdo = ZDOMan.instance.GetZDO(id);
            if (zdo != null && zdo.IsValid())
            {
                SaveReport(zdo, key, report);
            }
        }

        private static void SaveReport(ZDO zdo, string key, string report)
        {
            var trip = Scouting.ReadTrip(zdo);
            if (trip == null || trip.Key(zdo.m_uid) != key)
            {
                return; // a newer trip has started since
            }
            trip.Reported = true;
            Scouting.WriteTrip(zdo, trip);
            zdo.Set(Scouting.ReportKey, report ?? "");
        }

        // ------------------------------------------------------------ visuals

        /// <summary>Lets other players near the roost see the raven take off too.</summary>
        public static void BroadcastTakeOff(Vector3 from, Vector3 to, float tripSeconds, float speed)
        {
            var pkg = new ZPackage();
            pkg.Write(tripSeconds);
            pkg.Write(speed);
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcTakeOff, from, to, pkg);
        }

        private static void OnTakeOff(long sender, Vector3 from, Vector3 to, ZPackage pkg)
        {
            if (sender == ZDOMan.GetSessionID() || !Player.m_localPlayer)
            {
                return; // the sender already shows its own
            }
            if (Vector3.Distance(Player.m_localPlayer.transform.position, from) > TakeOffViewDistance)
            {
                return;
            }
            float seconds = pkg.ReadSingle();
            float speed = pkg.ReadSingle();
            RavenFlight.Launch(from, to, seconds, speed);
        }
    }
}
