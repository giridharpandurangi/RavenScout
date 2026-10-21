using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RavenScout
{
    /// <summary>
    /// Live tracking on the sender's game: with the plan the server sent at take-off, reveals the map behind
    /// the raven as it flies out, drops pins as it passes each find, and moves a feather marker on the map
    /// there and back. Purely local: the server still sends the full result when the raven returns.
    /// </summary>
    internal static class ScoutLive
    {
        private const float TickSeconds = 0.25f;
        private const float ForgetAfterReturnSeconds = 60f;
        private const int MessagesPerTick = 3;

        private class Flight
        {
            public TripResult Plan;
            public float[] Along;       // how far along the outward route each finding is (0..1)
            public bool[] Pinned;
            public float Revealed;      // how far along the outward route the map is revealed (0..1)
            public Minimap.PinData Marker;
        }

        private static readonly Dictionary<string, Flight> s_flights = new Dictionary<string, Flight>();
        private static readonly FieldInfo s_pinUpdateRequired = AccessTools.Field(typeof(Minimap), "m_pinUpdateRequired");
        private static float s_nextTick;
        private static Sprite s_feather;

        /// <summary>A new world: the old map and its markers are gone.</summary>
        public static void Reset() => s_flights.Clear();

        public static void Track(TripResult plan)
        {
            if (s_flights.TryGetValue(plan.Key, out var old))
            {
                RemoveMarker(old);
            }
            var flight = new Flight
            {
                Plan = plan,
                Along = new float[plan.Findings.Count],
                Pinned = new bool[plan.Findings.Count],
            };
            for (int i = 0; i < plan.Findings.Count; i++)
            {
                flight.Along[i] = Along(plan.Findings[i].Position, plan.From, plan.Target);
            }
            s_flights[plan.Key] = flight;
            Plugin.Log.LogInfo($"Tracking raven live: {plan.Findings.Count} finding(s) on the way");
        }

        /// <summary>The raven is back and its full result arrived: stop tracking it.</summary>
        public static void Finish(string key)
        {
            if (s_flights.TryGetValue(key, out var flight))
            {
                RemoveMarker(flight);
                s_flights.Remove(key);
            }
        }

        public static void Tick()
        {
            if (s_flights.Count == 0 || Time.time < s_nextTick)
            {
                return;
            }
            s_nextTick = Time.time + TickSeconds;
            var map = Minimap.instance;
            var player = Player.m_localPlayer;
            if (!map || !player || !Scouting.IsActive)
            {
                return;
            }

            double now = Scouting.Now;
            foreach (var key in new List<string>(s_flights.Keys))
            {
                var flight = s_flights[key];
                var plan = flight.Plan;
                if (now > plan.ReturnAt + ForgetAfterReturnSeconds)
                {
                    Finish(key); // the final result never came (e.g. server restart); it will still arrive later
                    continue;
                }
                if (Game.m_noMap)
                {
                    RemoveMarker(flight);
                    continue; // no map, nothing to show
                }

                // Out to the target in the first half of the trip, home in the second.
                float t = Mathf.Clamp01((float)((now - plan.SentAt) / System.Math.Max(1.0, plan.ReturnAt - plan.SentAt)));
                float outward = Mathf.Clamp01(t * 2f);

                if (outward > flight.Revealed)
                {
                    ScoutMap.Reveal(map, Vector3.Lerp(plan.From, plan.Target, flight.Revealed),
                        Vector3.Lerp(plan.From, plan.Target, outward), plan.RevealRadius);
                    flight.Revealed = outward;
                }

                var spotted = new List<string>();
                for (int i = 0; i < plan.Findings.Count; i++)
                {
                    if (!flight.Pinned[i] && flight.Along[i] <= outward)
                    {
                        flight.Pinned[i] = true;
                        if (ScoutMap.AddPin(map, plan.Findings[i]))
                        {
                            spotted.Add(plan.Findings[i].Label);
                        }
                    }
                }
                if (spotted.Count > 0 && Plugin.LiveMessages.Value)
                {
                    // Joining mid-flight can catch up on many finds at once: sum those up.
                    player.Message(MessageHud.MessageType.TopLeft, spotted.Count <= MessagesPerTick
                        ? $"Your raven spotted: {string.Join(", ", spotted)}"
                        : $"Your raven spotted {spotted.Count} things (see map)");
                }

                var position = t < 0.5f
                    ? Vector3.Lerp(plan.From, plan.Target, outward)
                    : Vector3.Lerp(plan.Target, plan.From, (t - 0.5f) * 2f);
                MoveMarker(map, flight, position, t < 1f && Plugin.ShowRavenOnMap.Value);
            }
        }

        /// <summary>How far along the route from <paramref name="from"/> to <paramref name="to"/> a point lies, 0..1.</summary>
        private static float Along(Vector3 p, Vector3 from, Vector3 to)
        {
            var ab = new Vector2(to.x - from.x, to.z - from.z);
            var ap = new Vector2(p.x - from.x, p.z - from.z);
            return ab.sqrMagnitude < 0.01f ? 0f : Mathf.Clamp01(Vector2.Dot(ap, ab) / ab.sqrMagnitude);
        }

        private static void MoveMarker(Minimap map, Flight flight, Vector3 position, bool show)
        {
            if (!show)
            {
                RemoveMarker(flight);
                return;
            }
            if (flight.Marker == null)
            {
                // Not saved, no name: just a feather that moves. Its icon is set before the map draws it.
                flight.Marker = map.AddPin(position, Minimap.PinType.Icon3, "", false, false);
                var icon = FeatherIcon();
                if (icon)
                {
                    flight.Marker.m_icon = icon;
                }
            }
            flight.Marker.m_pos = position;
            // The map only redraws pins when asked to.
            s_pinUpdateRequired?.SetValue(map, true);
        }

        private static void RemoveMarker(Flight flight)
        {
            if (flight.Marker != null && Minimap.instance)
            {
                Minimap.instance.RemovePin(flight.Marker);
            }
            flight.Marker = null;
        }

        private static Sprite FeatherIcon()
        {
            if (!s_feather && ObjectDB.instance)
            {
                var prefab = ObjectDB.instance.GetItemPrefab("Feathers");
                var drop = prefab ? prefab.GetComponent<ItemDrop>() : null;
                s_feather = drop ? drop.m_itemData.GetIcon() : null;
            }
            return s_feather;
        }
    }
}
