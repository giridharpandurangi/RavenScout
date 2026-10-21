using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RavenScout
{
    /// <summary>
    /// Puts a returned raven's findings on the local player's own map: reveals a strip along the route and
    /// adds pins. Each player's map is their own, so this runs on the game of whoever receives the result.
    /// </summary>
    internal static class ScoutMap
    {
        private const string AppliedKey = "RavenScout_applied";
        private const int AppliedMemory = 50;
        private const float SamePinDistance = 50f;

        private static readonly MethodInfo s_explore =
            AccessTools.Method(typeof(Minimap), "Explore", new[] { typeof(Vector3), typeof(float) });
        private static readonly FieldInfo s_pins = AccessTools.Field(typeof(Minimap), "m_pins");

        /// <summary>Applies a trip result once per player (results can be sent again after a server restart).</summary>
        public static void Apply(TripResult result)
        {
            var player = Player.m_localPlayer;
            var map = Minimap.instance;
            if (result == null || !player || !map)
            {
                return;
            }
            if (!MarkApplied(player, result.Key))
            {
                return;
            }

            string roost = string.IsNullOrEmpty(result.RoostName) ? "the roost" : result.RoostName;
            int count = result.Findings.Count;
            string spotted = count == 0 ? "nothing special spotted" : count == 1 ? "1 thing spotted" : $"{count} things spotted";

            // The server says no map for this trip (no-map world or setting); this player may also have turned
            // their own map off. Either way the map stays untouched.
            if (!result.ApplyMap || Game.m_noMap)
            {
                player.Message(MessageHud.MessageType.TopLeft, $"Your scouting raven is back: {spotted}");
                player.Message(MessageHud.MessageType.Center, result.GaveReport
                    ? $"Your scouting raven brought a Scout Report to {roost}"
                    : $"Your scouting raven is back - read its report at {roost}");
                Plugin.Log.LogInfo($"Trip result (no map): {result.Report}");
                return;
            }

            // With live tracking most of this is already done; both are safe to repeat.
            Reveal(map, result.From, result.Target, result.RevealRadius);
            foreach (var f in result.Findings)
            {
                AddPin(map, f);
            }

            player.Message(MessageHud.MessageType.TopLeft, $"Your scouting raven is back: {spotted}");
            player.Message(MessageHud.MessageType.Center, result.GaveReport
                ? $"Your scouting raven is back - check your map and the Scout Report at {roost}"
                : "Your scouting raven is back - check your map");
            Plugin.Log.LogInfo($"Trip result applied: {result.Report}");
        }

        /// <summary>Remembers the trip in the character's save. False if it was already applied.</summary>
        private static bool MarkApplied(Player player, string key)
        {
            player.m_customData.TryGetValue(AppliedKey, out var text);
            var keys = (text ?? "").Split('|').Where(k => k.Length > 0).ToList();
            if (keys.Contains(key))
            {
                return false;
            }
            keys.Add(key);
            if (keys.Count > AppliedMemory)
            {
                keys.RemoveRange(0, keys.Count - AppliedMemory);
            }
            player.m_customData[AppliedKey] = string.Join("|", keys);
            return true;
        }

        /// <summary>Reveals a strip of map from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public static void Reveal(Minimap map, Vector3 from, Vector3 to, float radius)
        {
            if (s_explore == null)
            {
                Plugin.Log.LogWarning("Minimap.Explore not found; can't reveal the route.");
                return;
            }
            radius = Mathf.Max(10f, radius);
            float length = Scouting.DistanceXZ(from, to);
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / (radius * 0.5f)));
            for (int i = 0; i <= steps; i++)
            {
                s_explore.Invoke(map, new object[] { Vector3.Lerp(from, to, i / (float)steps), radius });
            }
        }

        /// <summary>Adds a saved pin for a finding, unless the map already has one like it nearby.</summary>
        public static bool AddPin(Minimap map, Finding f)
        {
            string label = f.Label ?? "";
            float same = Scouting.SamePinDistance(f.Type, SamePinDistance);
            var pins = s_pins?.GetValue(map) as List<Minimap.PinData>;
            if (pins != null && pins.Any(p => p.m_save && IsPinFor(p.m_name, label) && Scouting.DistanceXZ(p.m_pos, f.Position) < same))
            {
                return false;
            }
            bool counted = (f.Type == Finding.Kind.Food || f.Type == Finding.Kind.Ore) && f.Count > 1;
            map.AddPin(f.Position, PinFor(f.Type), counted ? $"{label} x{f.Count}" : label, true, false);
            return true;
        }

        /// <summary>True for "Raspberries" and "Raspberries x8" when the label is "Raspberries".</summary>
        private static bool IsPinFor(string pinName, string label) =>
            pinName == label || (pinName != null && pinName.StartsWith(label + " x", System.StringComparison.Ordinal));

        /// <summary>Removes the food pins Raven Scout added (by their labels in FoodNames). Returns how many.</summary>
        public static int ClearFoodPins()
        {
            var map = Minimap.instance;
            var pins = map ? s_pins?.GetValue(map) as List<Minimap.PinData> : null;
            if (pins == null)
            {
                return 0;
            }
            var labels = Scouting.FoodLabels();
            var doomed = pins.Where(p => p.m_save && labels.Any(l => IsPinFor(p.m_name, l))).ToList();
            foreach (var pin in doomed)
            {
                map.RemovePin(pin);
            }
            return doomed.Count;
        }

        private static Minimap.PinType PinFor(Finding.Kind kind)
        {
            switch (kind)
            {
                case Finding.Kind.Boss: return Minimap.PinType.Boss;
                case Finding.Kind.Dungeon: return Minimap.PinType.Icon4;
                case Finding.Kind.Trader: return Minimap.PinType.Icon1;
                case Finding.Kind.Ore: return Minimap.PinType.Icon2;
                case Finding.Kind.Food: return Minimap.PinType.Icon3;
                default: return Minimap.PinType.Icon0;
            }
        }
    }
}
