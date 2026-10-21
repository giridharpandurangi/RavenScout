using System.Collections.Generic;
using UnityEngine;

namespace RavenScout
{
    /// <summary>
    /// Multiplayer plumbing. The server tracks trips: only it has the whole world's location list and objects,
    /// so it works out what the raven sees (at take-off for live tracking, and again when it's back), sends
    /// that to the player who sent it (or everyone, with ShareFindings = Everyone), and marks the trip as
    /// reported on the roost (through the roost's owner, as only an object's owner should change it). Shared
    /// trips are kept on the roost so players who join later catch up. In single player the game is its own
    /// server, so the same code simply runs locally.
    /// </summary>
    internal static class ScoutNet
    {
        private const string RpcTripStarted = "RavenScout_TripStarted";
        private const string RpcResult = "RavenScout_Result";
        private const string RpcPlan = "RavenScout_Plan";
        private const string RpcMarkReported = "RavenScout_MarkReported";
        private const string RpcServerMark = "RavenScout_ServerMark";
        private const string RpcTakeOff = "RavenScout_TakeOff";
        private const string RpcCatchUp = "RavenScout_CatchUp";

        private const float TakeOffViewDistance = 150f;
        private const float RetryMarkSeconds = 20f;

        /// <summary>Roosts with a raven out (or back but not yet reported), with when the server started watching.</summary>
        private static readonly Dictionary<ZDOID, float> s_watched = new Dictionary<ZDOID, float>();
        /// <summary>The "trip started" message can arrive before the roost's new data; don't give up on it too soon.</summary>
        private const float WatchGraceSeconds = 30f;
        /// <summary>Trips already reported this session, with when, in case the roost's owner never saved it.</summary>
        private static readonly Dictionary<string, float> s_reported = new Dictionary<string, float>();
        private static readonly HashSet<string> s_failed = new HashSet<string>();
        /// <summary>Which of the sender's connections got each trip's live plan (sent again if they reconnect).</summary>
        private static readonly Dictionary<string, long> s_planSent = new Dictionary<string, long>();
        /// <summary>Connections that already got the shared trips they missed.</summary>
        private static readonly HashSet<long> s_caughtUp = new HashSet<long>();
        private static bool s_scannedWorld;
        private static float s_nextTick;

        private static bool Sharing => Plugin.ShareFindings.Value == Plugin.ShareMode.Everyone;

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
            s_failed.Clear();
            s_planSent.Clear();
            s_caughtUp.Clear();
            s_scannedWorld = false;
            ScoutLive.Reset();

            rpc.Register<ZDOID>(RpcTripStarted, OnTripStarted);
            rpc.Register<ZPackage>(RpcResult, OnResult);
            rpc.Register<ZPackage>(RpcPlan, OnPlan);
            rpc.Register<ZDOID, ZPackage>(RpcMarkReported, OnMarkReported);
            rpc.Register<ZDOID, ZPackage>(RpcServerMark, OnServerMark);
            rpc.Register<Vector3, Vector3, ZPackage>(RpcTakeOff, OnTakeOff);
            rpc.Register<ZPackage>(RpcCatchUp, OnCatchUp);
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

        /// <summary>Server housekeeping, every second: send live plans for ravens in the air, report ravens that are back.</summary>
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

            if (Sharing)
            {
                CatchUpNewPlayers();
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
                string key = trip.Key(id);
                if (trip.IsAway(now))
                {
                    if (Plugin.LiveTracking.Value && MapAllowed(trip))
                    {
                        SendPlanIfNeeded(trip, key);
                    }
                    continue;
                }
                if (s_reported.TryGetValue(key, out float at) && Time.time - at < RetryMarkSeconds)
                {
                    continue; // waiting for the roost's owner to save it
                }
                // Private: the findings go to the sender; if they're offline, the raven waits with its news until
                // they're back. Shared: everyone online gets them now, and the rest catch up when they join.
                if (!Sharing && PlayerConnection(trip.Sender) == 0L)
                {
                    continue;
                }

                var result = TrySurvey(trip, key);
                s_reported[key] = Time.time; // on failure: try again later
                if (result == null)
                {
                    continue;
                }
                // Shared, the sender's own "map off" choice is theirs alone (their game skips the map anyway).
                result.ApplyMap = Sharing ? SharedMapAllowed : MapAllowed(trip);
                result.GaveReport = WantsReport(trip);
                result.RoostName = Scouting.RoostName(zdo);
                result.Sender = trip.Sender;
                bool shared = Sharing && result.ApplyMap;
                Plugin.Log.LogInfo($"Raven back for {trip.Sender}: {result.Report} ({result.Findings.Count} findings, " +
                                   $"map {(result.ApplyMap ? "yes" : "no")}, report item {(result.GaveReport ? "yes" : "no")}, " +
                                   $"{(shared ? "shared with everyone" : "private")})");
                byte[] sharedData = null;
                if (shared)
                {
                    var pkg = Scouting.Serialize(result);
                    sharedData = pkg.GetArray();
                    ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcResult, pkg); // includes this game
                }
                else
                {
                    Send(trip.Sender, RpcResult, result);
                }
                MarkReported(zdo, key, result.Report, result.ReportText, result.GaveReport, result.Guide, sharedData);
            }
        }

        // ------------------------------------------------------------ map or report

        /// <summary>
        /// May this trip change the sender's map? Never in a no-map world, whatever the config says (the server
        /// chose to play without a map). In Auto, not for a player who had their own map turned off either.
        /// </summary>
        private static bool MapAllowed(ScoutTrip trip)
        {
            var mode = Plugin.NoMapMode.Value;
            if (Scouting.WorldHasNoMap || mode == Plugin.NoMapBehaviour.ReportOnly)
            {
                return false;
            }
            return !(mode == Plugin.NoMapBehaviour.Auto && trip.SenderNoMap);
        }

        /// <summary>With shared findings: may returning ravens change players' maps at all?</summary>
        private static bool SharedMapAllowed =>
            !Scouting.WorldHasNoMap && Plugin.NoMapMode.Value != Plugin.NoMapBehaviour.ReportOnly;

        // ------------------------------------------------------------ catching up (shared findings)

        /// <summary>Sends each newly joined player the shared trips of every roost; their game skips what it already has.</summary>
        private static void CatchUpNewPlayers()
        {
            foreach (var peer in ZNet.instance.GetPeers())
            {
                // Wait until their character exists, so their game has a map to put the trips on.
                if (string.IsNullOrEmpty(peer.m_playerName) || peer.m_characterID.IsNone() || !s_caughtUp.Add(peer.m_uid))
                {
                    continue;
                }
                var trips = new List<byte[]>();
                foreach (var zdo in Scouting.GetAllRoostZdos())
                {
                    trips.AddRange(Scouting.ReadShared(zdo));
                }
                if (trips.Count == 0)
                {
                    continue;
                }
                var pkg = new ZPackage();
                pkg.Write(trips.Count);
                foreach (var t in trips)
                {
                    pkg.Write(t);
                }
                Plugin.Log.LogInfo($"Sending {peer.m_playerName} {trips.Count} shared raven trip(s) to catch up on");
                ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, RpcCatchUp, pkg);
            }
        }

        private static void OnCatchUp(long sender, ZPackage pkg)
        {
            int count = pkg.ReadInt();
            int applied = 0;
            for (int i = 0; i < count; i++)
            {
                var result = Scouting.Deserialize(new ZPackage(pkg.ReadByteArray()));
                if (result != null && ScoutMap.Apply(result, quiet: true))
                {
                    applied++;
                }
            }
            if (applied > 0 && Player.m_localPlayer)
            {
                Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft,
                    $"Ravens brought news while you were away: {applied} trip{(applied == 1 ? "" : "s")} on your map");
            }
        }

        /// <summary>Does this trip bring a Scout Report item? Always when the map can't show what the raven saw.</summary>
        private static bool WantsReport(ScoutTrip trip)
        {
            var mode = Plugin.NoMapMode.Value;
            return Scouting.WorldHasNoMap || mode == Plugin.NoMapBehaviour.ReportOnly || mode == Plugin.NoMapBehaviour.Both ||
                   (mode == Plugin.NoMapBehaviour.Auto && trip.SenderNoMap);
        }

        /// <summary>
        /// Live tracking: the world hardly changes during a flight, so the server works out what the raven will
        /// see at take-off and the sender's game plays it out along the flight. Sent again if they reconnect.
        /// </summary>
        private static void SendPlanIfNeeded(ScoutTrip trip, string key)
        {
            long connection = PlayerConnection(trip.Sender);
            if (connection == 0L || (s_planSent.TryGetValue(key, out long sentTo) && sentTo == connection))
            {
                return;
            }
            s_planSent[key] = connection;
            var plan = TrySurvey(trip, key);
            if (plan != null)
            {
                Send(trip.Sender, RpcPlan, plan);
            }
        }

        private static TripResult TrySurvey(ScoutTrip trip, string key)
        {
            try
            {
                return Scouting.Survey(trip, key);
            }
            catch (System.Exception e)
            {
                // Only fill the log once per trip.
                if (s_failed.Add(key))
                {
                    Plugin.Log.LogError($"Couldn't work out what the raven saw: {e}");
                }
                return null;
            }
        }

        /// <summary>The named player's current connection (this game's own for the host's player), or 0 if they're offline.</summary>
        private static long PlayerConnection(string playerName)
        {
            if (Player.m_localPlayer && Player.m_localPlayer.GetPlayerName() == playerName)
            {
                return ZDOMan.GetSessionID();
            }
            foreach (var peer in ZNet.instance.GetPeers())
            {
                if (peer.m_playerName == playerName)
                {
                    return peer.m_uid;
                }
            }
            return 0L;
        }

        private static void Send(string playerName, string rpc, TripResult result)
        {
            // The host's own player isn't in the peer list.
            if (Player.m_localPlayer && Player.m_localPlayer.GetPlayerName() == playerName)
            {
                if (rpc == RpcPlan)
                {
                    ScoutLive.Track(result);
                }
                else
                {
                    ScoutLive.Finish(result.Key);
                    ScoutMap.Apply(result);
                }
            }
            foreach (var peer in ZNet.instance.GetPeers())
            {
                if (peer.m_playerName == playerName)
                {
                    ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, rpc, Scouting.Serialize(result));
                }
            }
        }

        private static void OnPlan(long sender, ZPackage pkg)
        {
            var plan = Scouting.Deserialize(pkg);
            if (plan != null)
            {
                ScoutLive.Track(plan);
            }
        }

        private static void OnResult(long sender, ZPackage pkg)
        {
            var result = Scouting.Deserialize(pkg);
            if (result != null)
            {
                ScoutLive.Finish(result.Key);
                ScoutMap.Apply(result);
            }
        }

        /// <summary>
        /// Only the owner of an object should change it, or one of the two changes can be lost. If a player
        /// owns the roost (they're near it), they save the report; otherwise the server does.
        /// <paramref name="reportText"/> is the Scout Report's text: kept as the roost's last report (to copy),
        /// and turned into an item when <paramref name="giveItem"/>.
        /// </summary>
        private static void MarkReported(ZDO zdo, string key, string report, string reportText, bool giveItem, string guide,
            byte[] shared)
        {
            var pkg = new ZPackage();
            pkg.Write(key);
            pkg.Write(report ?? "");
            pkg.Write(reportText ?? "");
            pkg.Write(giveItem);
            pkg.Write(guide ?? "");
            pkg.Write(shared ?? new byte[0]);
            long owner = zdo.GetOwner();
            if (owner != 0 && owner != ZDOMan.GetSessionID() && ZNet.instance.GetPeer(owner) != null)
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(owner, RpcMarkReported, zdo.m_uid, pkg);
                return;
            }
            SaveReport(zdo, pkg);
        }

        private static void OnMarkReported(long sender, ZDOID id, ZPackage pkg)
        {
            var zdo = ZDOMan.instance.GetZDO(id);
            if (zdo != null && zdo.IsOwner())
            {
                SaveReport(zdo, pkg);
                return;
            }
            // Ownership moved on in the meantime: let the server save it.
            pkg.SetPos(0);
            ZRoutedRpc.instance.InvokeRoutedRPC(RpcServerMark, id, pkg);
        }

        private static void OnServerMark(long sender, ZDOID id, ZPackage pkg)
        {
            if (!Scouting.IsServer)
            {
                return;
            }
            var zdo = ZDOMan.instance.GetZDO(id);
            if (zdo != null && zdo.IsValid())
            {
                SaveReport(zdo, pkg);
            }
        }

        private static void SaveReport(ZDO zdo, ZPackage pkg)
        {
            pkg.SetPos(0);
            string key = pkg.ReadString();
            string report = pkg.ReadString();
            string reportText = pkg.ReadString();
            bool giveItem = pkg.ReadBool();
            string guide = pkg.ReadString();
            byte[] shared = pkg.ReadByteArray();

            var trip = Scouting.ReadTrip(zdo);
            if (trip == null || trip.Key(zdo.m_uid) != key || trip.Reported)
            {
                return; // a newer trip has started since, or this one is already saved
            }
            trip.Reported = true;
            Scouting.WriteTrip(zdo, trip);
            zdo.Set(Scouting.ReportKey, report ?? "");
            int day = Scouting.Day(trip.ReturnAt);
            zdo.Set(Scouting.LastTextKey, reportText ?? "");
            zdo.Set(Scouting.LastDayKey, day);
            zdo.Set(Scouting.LastGuideKey, guide ?? "");
            if (shared != null && shared.Length > 0)
            {
                Scouting.AddShared(zdo, shared); // for players who join later
            }
            if (giveItem && !string.IsNullOrEmpty(reportText))
            {
                // The roost's owner turns it into an item in the roost's box (see RavenRoost.DeliverReports).
                var pending = Scouting.ReadPending(zdo);
                pending.Add(new PendingReport { Text = reportText, Roost = Scouting.RoostName(zdo), Day = day, Guide = guide ?? "" });
                Scouting.WritePending(zdo, pending);
            }
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
