using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace RavenScout
{
    /// <summary>
    /// The raven's feather guide: using a Scout Report gives a short hint toward the nearest find written on
    /// it, "the feather points left", relative to where the player is facing. Never a marker or compass, and
    /// only a few uses per report.
    /// </summary>
    internal static class FeatherGuide
    {
        /// <summary>Close enough to count as arrived: the hint says so instead of pointing.</summary>
        private const float ArrivedDistance = 40f;

        private class Target
        {
            public string Label;
            public Vector3 Position;
        }

        public static void Use(Player player, ItemDrop.ItemData item)
        {
            if (!Plugin.FeatherGuideOn.Value)
            {
                player.Message(MessageHud.MessageType.Center, "Read it in the tooltip");
                return;
            }
            var targets = Targets(item);
            if (targets.Count == 0)
            {
                player.Message(MessageHud.MessageType.Center, "This report has nothing for the feather to point to");
                return;
            }
            int uses = UsesLeft(item);
            if (uses <= 0)
            {
                player.Message(MessageHud.MessageType.Center, "The feather has gone still - it can't show the way any more");
                return;
            }

            // Nearest find from where the player stands now.
            var here = player.transform.position;
            Target nearest = null;
            float best = float.MaxValue;
            foreach (var t in targets)
            {
                float d = Scouting.DistanceXZ(here, t.Position);
                if (d < best)
                {
                    best = d;
                    nearest = t;
                }
            }

            if (best < ArrivedDistance)
            {
                // Arriving costs nothing.
                player.Message(MessageHud.MessageType.Center, $"The feather trembles: {nearest.Label} is right here");
                return;
            }

            item.m_customData[ScoutReportItem.GuideUsesKey] = (uses - 1).ToString();
            var to = nearest.Position - here;
            to.y = 0f;
            var facing = player.transform.forward;
            facing.y = 0f;
            float angle = Vector3.SignedAngle(facing, to, Vector3.up); // + = to the right
            string way = Mathf.Abs(angle) <= 30f ? "ahead"
                : Mathf.Abs(angle) >= 150f ? "behind you"
                : angle > 0f ? "to your right" : "to your left";
            string left = uses - 1 == 0 ? "it goes still" : uses - 1 == 1 ? "1 more time" : $"{uses - 1} more times";
            player.Message(MessageHud.MessageType.Center,
                $"The raven's feather points {way}, toward the {nearest.Label} ({Far(best)})  -  {left}");
        }

        /// <summary>A line for the report's tooltip, or nothing.</summary>
        public static string TooltipLine(ItemDrop.ItemData item)
        {
            if (!Plugin.FeatherGuideOn.Value || Targets(item).Count == 0)
            {
                return "";
            }
            int uses = UsesLeft(item);
            return uses > 0
                ? $"\n\n<color=grey>Use it: the raven's feather points toward the nearest find ({uses} use{(uses == 1 ? "" : "s")} left).</color>"
                : "\n\n<color=grey>The feather has gone still.</color>";
        }

        /// <summary>Rough distance, like the reports: the feather doesn't count metres.</summary>
        private static string Far(float metres)
        {
            if (Plugin.ReportPrecision.Value == Plugin.Precision.Exact)
            {
                return $"{Mathf.RoundToInt(metres)} m";
            }
            if (metres < 150f)
            {
                return "close";
            }
            if (metres < 700f)
            {
                return "not far";
            }
            return metres < 2000f ? "a good walk" : "far away";
        }

        private static int UsesLeft(ItemDrop.ItemData item) =>
            item.m_customData.TryGetValue(ScoutReportItem.GuideUsesKey, out var text) && int.TryParse(text, out int n) ? n : 0;

        private static List<Target> Targets(ItemDrop.ItemData item)
        {
            var list = new List<Target>();
            if (!item.m_customData.TryGetValue(ScoutReportItem.GuideKey, out var guide) || string.IsNullOrEmpty(guide))
            {
                return list;
            }
            foreach (var entry in guide.Split(';'))
            {
                var bits = entry.Split('|');
                if (bits.Length == 3 &&
                    float.TryParse(bits[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                    float.TryParse(bits[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                {
                    list.Add(new Target { Label = bits[0], Position = new Vector3(x, 0f, z) });
                }
            }
            return list;
        }
    }
}
