using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace RavenScout
{
    /// <summary>One upgrade level of a roost, following the biome progression.</summary>
    internal class RoostTier
    {
        public int Index;
        public string Name;
        public string Biome;
        public ConfigEntry<string> Cost;
        public ConfigEntry<float> Range;
        public ConfigEntry<float> Speed;
        public ConfigEntry<float> Sight;

        public float RangeMultiplier => Mathf.Max(0.1f, Range.Value);
        public float SpeedMultiplier => Mathf.Max(0.1f, Speed.Value);
        public float SightMultiplier => Mathf.Max(0.1f, Sight.Value);

        public List<Cost> Requirements() => Costs.Parse(Cost.Value);
    }

    /// <summary>
    /// Roost tiers, Wooden to Deep North. A tier multiplies the raven's range and speed (on top of its meal)
    /// and how wide a strip it sees; ore and food spotting unlock at early tiers. All of it is in the config.
    /// </summary>
    internal static class RoostTiers
    {
        public const string TierKey = "ravenscout_tier";

        private static readonly List<RoostTier> s_tiers = new List<RoostTier>();
        private static bool s_validated;

        internal static ConfigEntry<int> OresFromTier;
        internal static ConfigEntry<int> FoodFromTier;

        public static IReadOnlyList<RoostTier> All => s_tiers;

        public static RoostTier Get(int index) => s_tiers[Mathf.Clamp(index, 0, s_tiers.Count - 1)];

        public static int Max => s_tiers.Count - 1;

        public static void Bind(ConfigFile config)
        {
            OresFromTier = config.Bind("Tiers", "OresFromTier", 1,
                Plugin.Synced("Lowest roost tier whose raven spots ore (0 = Wooden, 1 = Bronze, ...). 0 = every roost."));
            FoodFromTier = config.Bind("Tiers", "FoodFromTier", 2,
                Plugin.Synced("Lowest roost tier whose raven spots wild food (also needs SpotFood = true). 0 = every roost."));

            Add(config, "Wooden Roost", "Meadows", "", 1.0f, 1.0f, 1.0f);
            Add(config, "Bronze Roost", "Black Forest", "Bronze:5,BronzeNails:10", 1.25f, 1.1f, 1.0f);
            Add(config, "Iron Roost", "Swamp", "Iron:10,Guck:5", 1.5f, 1.2f, 1.25f);
            Add(config, "Silver Roost", "Mountains", "Silver:10,WolfPelt:4", 1.75f, 1.35f, 1.25f);
            Add(config, "Black Metal Roost", "Plains", "BlackMetal:10,LinenThread:10", 2.0f, 1.5f, 1.25f);
            Add(config, "Eitr Roost", "Mistlands", "BlackCore:2,Eitr:10,Carapace:10", 2.5f, 1.75f, 1.5f);
            Add(config, "Flametal Roost", "Ashlands", "FlametalNew:10,CelestialFeather:3", 3.0f, 2.0f, 1.5f);
            // Deep North materials aren't known yet; Ashlands end-game items stand in. Change in the config.
            Add(config, "Deep North Roost", "Deep North", "FlametalNew:30,MoltenCore:2", 4.0f, 2.5f, 1.5f);
        }

        private static void Add(ConfigFile config, string name, string biome, string cost, float range, float speed, float sight)
        {
            int index = s_tiers.Count;
            string section = $"Tier {index} - {name}";
            s_tiers.Add(new RoostTier
            {
                Index = index,
                Name = name,
                Biome = biome,
                Cost = config.Bind(section, "UpgradeCost", cost,
                    Plugin.Synced(index == 0 ? "Not used: this is the tier you build." : "Items to upgrade to this tier, as PrefabName:Amount, comma-separated.")),
                Range = config.Bind(section, "Range", range, Plugin.Synced("Range multiplier; multiplies with BaseRange and the meal.")),
                Speed = config.Bind(section, "Speed", speed, Plugin.Synced("Speed multiplier; multiplies with the meal.")),
                Sight = config.Bind(section, "Sight", sight,
                    Plugin.Synced("Multiplies RevealRadius and SpotRadius: how wide a strip this raven maps and sees.")),
            });
        }

        public static bool SpotsOre(int tier) => Plugin.SpotOres.Value && tier >= OresFromTier.Value;

        public static bool SpotsFood(int tier) => Plugin.SpotFood.Value && tier >= FoodFromTier.Value;

        /// <summary>Logs once any upgrade materials this game version doesn't have, so the config can be fixed.</summary>
        public static void ValidateItems()
        {
            if (s_validated || !ObjectDB.instance)
            {
                return;
            }
            s_validated = true;
            foreach (var tier in s_tiers.Skip(1))
            {
                var missing = Costs.Missing(tier.Requirements());
                if (missing.Count > 0)
                {
                    Plugin.Log.LogWarning($"{tier.Name}: unknown upgrade item(s) {string.Join(", ", missing)} - " +
                                          "this tier can't be reached until its UpgradeCost is changed in the config.");
                }
            }
        }
    }
}
