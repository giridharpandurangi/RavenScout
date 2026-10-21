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

            Reveal(map, result.From, result.Target, result.RevealRadius);
            int added = 0;
            foreach (var f in result.Findings)
            {
                if (AddPin(map, f))
                {
                    added++;
                }
            }

            string pins = added == 0 ? "no new pins" : added == 1 ? "1 new pin" : $"{added} new pins";
            player.Message(MessageHud.MessageType.TopLeft, $"Your scouting raven is back ({pins} on your map)");
            player.Message(MessageHud.MessageType.Center, "Your scouting raven is back - check your map");
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

        private static void Reveal(Minimap map, Vector3 from, Vector3 to, float radius)
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

        private static bool AddPin(Minimap map, Finding f)
        {
            string name = f.Label ?? "";
            var pins = s_pins?.GetValue(map) as List<Minimap.PinData>;
            if (pins != null && pins.Any(p => p.m_name == name && Scouting.DistanceXZ(p.m_pos, f.Position) < SamePinDistance))
            {
                return false;
            }
            map.AddPin(f.Position, PinFor(f.Type), name, true, false);
            return true;
        }

        private static Minimap.PinType PinFor(Finding.Kind kind)
        {
            switch (kind)
            {
                case Finding.Kind.Boss: return Minimap.PinType.Boss;
                case Finding.Kind.Dungeon: return Minimap.PinType.Icon4;
                case Finding.Kind.Trader: return Minimap.PinType.Icon1;
                case Finding.Kind.Ore: return Minimap.PinType.Icon2;
                default: return Minimap.PinType.Icon3;
            }
        }
    }
}
