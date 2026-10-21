using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace RavenScout
{
    /// <summary>
    /// Writes a Scout Report the way a raven would tell it at the roost: which way it flew, what it saw
    /// and roughly where, from the roost's point of view. Distances are rounded (see ReportPrecision) so it
    /// reads like a raven's account, not GPS. Uses the world generator, so it works over unvisited land.
    /// </summary>
    internal static class ScoutWords
    {
        private const int MaxFindLines = 12;

        public static string Write(ScoutTrip trip, List<Finding> findings, bool wildLand)
        {
            var sb = new StringBuilder();
            float heading = Scouting.Heading(trip.From, trip.Target);
            sb.Append($"Flew {Where(trip.Distance, heading)}");
            var biomes = BiomesAlong(trip.From, trip.Target);
            if (biomes.Count > 0)
            {
                sb.Append($", over {JoinAnd(biomes.Select(b => b.Name).ToList())}");
            }
            sb.Append('.');

            var finds = findings
                .Select(f => new { Find = f, Distance = Scouting.DistanceXZ(trip.From, f.Position) })
                .OrderBy(f => f.Distance)
                .ToList();
            if (finds.Count == 0)
            {
                sb.Append("\nSaw nothing worth a note.");
            }
            foreach (var f in finds.Take(MaxFindLines))
            {
                var p = f.Find.Position;
                string what = f.Find.Count > 1 ? $"{f.Find.Label} x{f.Find.Count}" : f.Find.Label;
                string terrain = Terrain(p);
                string biome = BiomePhrase(Biome(p));
                sb.Append($"\n- {what}{(terrain.Length > 0 ? " " + terrain : "")}{(biome.Length > 0 ? " " + biome : "")}, " +
                          $"{Where(f.Distance, Scouting.Heading(trip.From, p))}.");
            }
            if (finds.Count > MaxFindLines)
            {
                sb.Append($"\n...and {finds.Count - MaxFindLines} more.");
            }

            // Where the land changes, beyond the roost's own biome.
            foreach (var b in biomes.Skip(1))
            {
                sb.Append($"\n{Capitalise(b.Name)} {(b.Name.EndsWith("s") ? "begin" : "begins")} {Where(b.Distance, heading)}.");
            }
            if (wildLand && (Plugin.SpotOres.Value || Plugin.SpotFood.Value))
            {
                sb.Append("\nSome land was wild and unvisited: ore and plants there couldn't be seen.");
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------ distances and directions

        /// <summary>"about 600 m north-east", "close by, to the north", "far to the east, over 2 km".</summary>
        public static string Where(float metres, float degrees)
        {
            string dir = Direction(degrees);
            switch (Plugin.ReportPrecision.Value)
            {
                case Plugin.Precision.Exact:
                    return $"{Mathf.RoundToInt(metres)} m {Scouting.CompassName(degrees)} ({Mathf.RoundToInt(degrees)}°)";
                case Plugin.Precision.Rough:
                    return metres < 1000f
                        ? $"about {Mathf.Max(100, Mathf.RoundToInt(metres / 100f) * 100)} m {dir}"
                        : $"about {metres / 1000f:0.0} km {dir}";
                default:
                    if (metres < 150f)
                    {
                        return $"close by, to the {dir}";
                    }
                    if (metres < 650f)
                    {
                        return $"about {Mathf.RoundToInt(metres / 100f) * 100} m {dir}";
                    }
                    if (metres < 2000f)
                    {
                        float km = Mathf.Round(metres / 500f) * 0.5f;
                        return $"about {km:0.#} km {dir}";
                    }
                    return $"far to the {dir}, over {Mathf.FloorToInt(metres / 1000f)} km";
            }
        }

        private static string Direction(float degrees)
        {
            string[] names = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
            return names[Mathf.RoundToInt(Mathf.Repeat(degrees, 360f) / 45f) % 8];
        }

        // ------------------------------------------------------------ land

        private class BiomeStretch
        {
            public string Name;
            public float Distance; // from the roost, along the route
        }

        /// <summary>Biomes under the route in the order the raven met them, with where each starts.</summary>
        private static List<BiomeStretch> BiomesAlong(Vector3 from, Vector3 to)
        {
            var result = new List<BiomeStretch>();
            if (WorldGenerator.instance == null)
            {
                return result;
            }
            float length = Scouting.DistanceXZ(from, to);
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / 50f));
            for (int i = 0; i <= steps; i++)
            {
                var p = Vector3.Lerp(from, to, i / (float)steps);
                string name = BiomeName(Biome(p));
                if (name != null && result.All(b => b.Name != name))
                {
                    result.Add(new BiomeStretch { Name = name, Distance = length * i / steps });
                }
            }
            return result;
        }

        private static Heightmap.Biome Biome(Vector3 p) =>
            WorldGenerator.instance != null ? WorldGenerator.instance.GetBiome(p.x, p.z) : Heightmap.Biome.None;

        private static string BiomeName(Heightmap.Biome biome)
        {
            switch (biome)
            {
                case Heightmap.Biome.Meadows: return "the Meadows";
                case Heightmap.Biome.BlackForest: return "the Black Forest";
                case Heightmap.Biome.Swamp: return "the Swamp";
                case Heightmap.Biome.Mountain: return "the Mountains";
                case Heightmap.Biome.Plains: return "the Plains";
                case Heightmap.Biome.Ocean: return "open sea";
                case Heightmap.Biome.Mistlands: return "the Mistlands";
                case Heightmap.Biome.AshLands: return "the Ashlands";
                case Heightmap.Biome.DeepNorth: return "the Deep North";
                default: return null;
            }
        }

        /// <summary>"in the Black Forest", "on the Plains", "at sea".</summary>
        private static string BiomePhrase(Heightmap.Biome biome)
        {
            if (biome == Heightmap.Biome.Ocean)
            {
                return "at sea";
            }
            string name = BiomeName(biome);
            if (name == null)
            {
                return "";
            }
            return biome == Heightmap.Biome.Plains ? $"on {name}" : $"in {name}";
        }

        /// <summary>"by the water" or "on a hill" when the land around makes that clear; otherwise nothing.</summary>
        private static string Terrain(Vector3 p)
        {
            var gen = WorldGenerator.instance;
            if (gen == null || !ZoneSystem.instance || Biome(p) == Heightmap.Biome.Ocean)
            {
                return "";
            }
            float water = ZoneSystem.instance.m_waterLevel;
            float h = gen.GetHeight(p.x, p.z);
            float sum = 0f;
            bool nearWater = h < water + 1f;
            const int samples = 8;
            for (int i = 0; i < samples; i++)
            {
                float a = i * Mathf.PI * 2f / samples;
                float s = gen.GetHeight(p.x + Mathf.Sin(a) * 50f, p.z + Mathf.Cos(a) * 50f);
                sum += s;
                nearWater |= s < water;
            }
            if (nearWater)
            {
                return "by the water";
            }
            return h - sum / samples > 6f ? "on a hill" : "";
        }

        // ------------------------------------------------------------ text helpers

        private static string JoinAnd(List<string> items) =>
            items.Count <= 1 ? string.Join("", items) : string.Join(", ", items.Take(items.Count - 1)) + " and " + items.Last();

        private static string Capitalise(string text) =>
            string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
    }
}
