using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace RavenScout
{
    /// <summary>
    /// Ravens need a meal per flight. Meals are stored on the roost as speed multipliers;
    /// better food (more health) makes a faster raven.
    /// </summary>
    internal static class RavenFood
    {
        public const string MealsKey = "ravenscout_meals";

        private static HashSet<string> s_rawMeats;
        private static string s_rawMeatsSource;

        /// <summary>Speed multiplier this item gives as a meal, or 0 if ravens won't eat it.</summary>
        public static float SpeedFor(ItemDrop.ItemData item)
        {
            if (item == null)
            {
                return 0f;
            }
            var shared = item.m_shared;
            if (shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable && shared.m_food > 0f)
            {
                // 30 health food -> x1.6, 75 -> x2.5, 100 -> x3; end-game food flies fastest.
                return Mathf.Min(Plugin.MaxFoodSpeed.Value, 1f + shared.m_food / 50f);
            }
            string prefab = item.m_dropPrefab ? item.m_dropPrefab.name : "";
            return RawMeats().Contains(prefab) ? Plugin.RawMeatSpeed.Value : 0f;
        }

        private static HashSet<string> RawMeats()
        {
            string source = Plugin.RawMeatItems.Value ?? "";
            if (s_rawMeats == null || source != s_rawMeatsSource)
            {
                s_rawMeatsSource = source;
                s_rawMeats = new HashSet<string>(source.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0));
            }
            return s_rawMeats;
        }

        /// <summary>The food in <paramref name="inventory"/> that makes the fastest raven, or null.</summary>
        public static ItemDrop.ItemData BestFood(Inventory inventory)
        {
            ItemDrop.ItemData best = null;
            float bestSpeed = 0f;
            foreach (var item in inventory.GetAllItems())
            {
                float speed = SpeedFor(item);
                if (speed > bestSpeed && !item.m_equipped)
                {
                    best = item;
                    bestSpeed = speed;
                }
            }
            return best;
        }

        /// <summary>Stored meals, best first.</summary>
        public static List<float> ReadMeals(ZDO zdo)
        {
            var meals = new List<float>();
            foreach (var part in zdo.GetString(MealsKey).Split(';'))
            {
                if (float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out float speed) && speed > 0f)
                {
                    meals.Add(speed);
                }
            }
            meals.Sort((a, b) => b.CompareTo(a));
            return meals;
        }

        public static void WriteMeals(ZDO zdo, List<float> meals)
        {
            meals.Sort((a, b) => b.CompareTo(a));
            zdo.Set(MealsKey, string.Join(";", meals.Select(m => m.ToString("0.###", CultureInfo.InvariantCulture))));
        }

        public static string FormatSpeed(float speed) => $"x{speed:0.##}";
    }
}
