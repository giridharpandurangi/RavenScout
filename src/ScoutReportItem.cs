using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace RavenScout
{
    /// <summary>
    /// The Scout Report: a parchment the raven brings back to the roost, mainly for no-map worlds. Its text
    /// lives in the item's custom data, which Valheim saves with the item wherever it is, so reports can be
    /// kept as a journal, stored in chests and given to friends. Read in the tooltip; nobody can change it.
    /// Not craftable.
    /// </summary>
    internal static class ScoutReportItem
    {
        public const string PrefabName = "RavenScoutReport";

        private const string TextKey = "ravenscout_text";
        private const string RoostKey = "ravenscout_roost";
        private const string DayKey = "ravenscout_day";

        private static ItemDrop.ItemData.SharedData s_shared;

        public static void Register()
        {
            var config = new ItemConfig
            {
                Name = "Scout Report",
                Description = "What a scouting raven saw on its trip, written down at the roost.",
                Icon = CreateIcon(),
                // No requirements: there is no recipe, only ravens bring these.
            };
            var item = new CustomItem(PrefabName, "LeatherScraps", config);
            var shared = item.ItemDrop.m_itemData.m_shared;
            shared.m_maxStackSize = 1; // each report holds its own text
            shared.m_weight = 0.1f;
            shared.m_teleportable = true;
            shared.m_value = 0;
            s_shared = shared;
            ItemManager.Instance.AddItem(item);
        }

        public static bool IsReport(ItemDrop.ItemData item) =>
            item != null && ((item.m_dropPrefab && item.m_dropPrefab.name == PrefabName) ||
                             (s_shared != null && item.m_shared == s_shared));

        /// <summary>A new report item with this text, or null if the item isn't registered.</summary>
        public static ItemDrop.ItemData Create(string text, string roost, int day)
        {
            var prefab = ObjectDB.instance ? ObjectDB.instance.GetItemPrefab(PrefabName) : null;
            var drop = prefab ? prefab.GetComponent<ItemDrop>() : null;
            if (!drop)
            {
                return null;
            }
            var item = drop.m_itemData.Clone();
            item.m_stack = 1;
            item.m_dropPrefab = prefab;
            item.m_customData[TextKey] = text ?? "";
            item.m_customData[RoostKey] = roost ?? "";
            item.m_customData[DayKey] = day.ToString();
            return item;
        }

        /// <summary>Lines added to the item tooltip.</summary>
        public static string Tooltip(ItemDrop.ItemData item)
        {
            item.m_customData.TryGetValue(TextKey, out var text);
            if (string.IsNullOrEmpty(text))
            {
                return "\n\n<color=grey>The page is empty.</color>";
            }
            item.m_customData.TryGetValue(RoostKey, out var roost);
            item.m_customData.TryGetValue(DayKey, out var day);
            string header = string.IsNullOrEmpty(roost) ? "Scout report" : $"Scout report from {roost}";
            if (!string.IsNullOrEmpty(day))
            {
                header += $", Day {day}";
            }
            return $"\n\n<color=orange>{Escape(header)}</color>\n<color=#e8dcc0>{Escape(text)}</color>";
        }

        // Roost names are typed by players: stop them opening rich-text tags in the tooltip.
        private static string Escape(string text) => text.Replace("<", "<​");

        /// <summary>A parchment with an inked route, a red mark at its end and a small compass star, drawn in code.</summary>
        private static Sprite CreateIcon()
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var clear = new Color(0, 0, 0, 0);
            var paper = new Color(0.86f, 0.77f, 0.56f);
            var paperDark = new Color(0.74f, 0.63f, 0.42f);
            var edge = new Color(0.55f, 0.43f, 0.26f);
            var ink = new Color(0.22f, 0.15f, 0.09f);
            var mark = new Color(0.70f, 0.12f, 0.08f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Color c = clear;
                    // Slightly wavy sheet so it reads as old paper.
                    int wobble = (int)(Mathf.Sin(y * 0.45f) * 1.5f);
                    bool inPaper = x >= 8 + wobble && x <= 55 + wobble && y >= 7 && y <= 56;
                    if (inPaper)
                    {
                        bool border = x <= 9 + wobble || x >= 54 + wobble || y <= 8 || y >= 55;
                        c = border ? edge : ((x + y) % 11 == 0 ? paperDark : paper);
                    }
                    tex.SetPixel(x, y, c);
                }
            }

            // Dashed route from the roost (bottom left) curving to the find (top right).
            for (int i = 0; i <= 40; i++)
            {
                if (i % 6 >= 4)
                {
                    continue;
                }
                float t = i / 40f;
                int x = Mathf.RoundToInt(Mathf.Lerp(15f, 44f, t));
                int y = Mathf.RoundToInt(Mathf.Lerp(16f, 44f, t) + Mathf.Sin(t * Mathf.PI) * 6f);
                tex.SetPixel(x, y, ink);
                tex.SetPixel(x + 1, y, ink);
                tex.SetPixel(x, y + 1, ink);
            }

            // Red X at the find.
            for (int d = -4; d <= 4; d++)
            {
                for (int w = 0; w <= 1; w++)
                {
                    tex.SetPixel(46 + d + w, 46 + d, mark);
                    tex.SetPixel(46 + d + w, 46 - d, mark);
                }
            }

            // Small four-point compass star, bottom right.
            for (int d = -5; d <= 5; d++)
            {
                tex.SetPixel(44 + d, 18, ink);
                tex.SetPixel(44, 18 + d, ink);
            }
            for (int d = -2; d <= 2; d++)
            {
                tex.SetPixel(44 + d, 18 + d, ink);
                tex.SetPixel(44 + d, 18 - d, ink);
            }

            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }
    }
}
