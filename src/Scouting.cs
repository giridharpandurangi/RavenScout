using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace RavenScout
{
    /// <summary>One raven out on a scouting trip (or back from one). Saved on the roost's own world data.</summary>
    internal class ScoutTrip
    {
        public double SentAt;
        public double ReturnAt;
        public Vector3 From;
        public Vector3 Target;
        public string Sender = "";
        public float Meal = 1f;
        public bool Reported; // the server has handed the findings to the sender

        public bool IsAway(double now) => now < ReturnAt;

        /// <summary>Identifies this trip from this roost, for "already reported" checks.</summary>
        public string Key(ZDOID roost) => $"{roost}:{SentAt:R}";

        public float Distance => Scouting.DistanceXZ(From, Target);
    }

    /// <summary>Something the raven saw: a place or an ore deposit.</summary>
    internal class Finding
    {
        public enum Kind { Dungeon, Boss, Trader, Other, Ore }

        public Kind Type;
        public string Label;
        public Vector3 Position;
        public int Count = 1; // ore deposits close together are one pin
    }

    /// <summary>What the server sends to the player whose raven came back.</summary>
    internal class TripResult
    {
        public string Key;
        public Vector3 From;
        public Vector3 Target;
        public float RevealRadius;
        public string Report;
        public List<Finding> Findings = new List<Finding>();
    }

    /// <summary>Trip storage, distances and times, and (on the server) working out what a raven saw.</summary>
    internal static class Scouting
    {
        public const string TripKey = "ravenscout_trip";
        public const string ReportKey = "ravenscout_report";
        private const int TripVersion = 1;
        private const int ResultVersion = 1;

        /// <summary>Ravens turn back at the edge of the world.</summary>
        private const float WorldRadius = 10000f;

        public static bool IsActive => ZNet.instance && ZDOMan.instance != null && ZRoutedRpc.instance != null;

        public static bool IsServer => ZNet.instance && ZNet.instance.IsServer();

        public static double Now => ZNet.instance.GetTimeSeconds();

        public static int Day(double time) => EnvMan.instance ? EnvMan.instance.GetDay(time) : 0;

        // ------------------------------------------------------------ trip storage

        public static ScoutTrip ReadTrip(ZDO zdo)
        {
            byte[] data = zdo?.GetByteArray(TripKey);
            if (data == null || data.Length == 0)
            {
                return null;
            }
            var pkg = new ZPackage(data);
            int version = pkg.ReadInt();
            if (version != TripVersion)
            {
                Plugin.Log.LogWarning($"Unknown trip version {version}; ignoring.");
                return null;
            }
            return new ScoutTrip
            {
                SentAt = pkg.ReadDouble(),
                ReturnAt = pkg.ReadDouble(),
                From = pkg.ReadVector3(),
                Target = pkg.ReadVector3(),
                Sender = pkg.ReadString(),
                Meal = pkg.ReadSingle(),
                Reported = pkg.ReadBool(),
            };
        }

        public static void WriteTrip(ZDO zdo, ScoutTrip trip)
        {
            var pkg = new ZPackage();
            pkg.Write(TripVersion);
            pkg.Write(trip.SentAt);
            pkg.Write(trip.ReturnAt);
            pkg.Write(trip.From);
            pkg.Write(trip.Target);
            pkg.Write(trip.Sender ?? "");
            pkg.Write(trip.Meal);
            pkg.Write(trip.Reported);
            zdo.Set(TripKey, pkg.GetArray());
        }

        /// <summary>Every roost in the world. Server only: clients only know the area around them.</summary>
        public static List<ZDO> GetAllRoostZdos()
        {
            var list = new List<ZDO>();
            int index = 0;
            while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(Plugin.RoostPrefabName, list, ref index))
            {
            }
            list.RemoveAll(z => z == null || !z.IsValid());
            return list;
        }

        // ------------------------------------------------------------ trip maths

        public static float DistanceXZ(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        /// <summary>How far out a raven with this meal can fly.</summary>
        public static float RangeFor(float meal) => Mathf.Max(Plugin.MinDistance.Value, Plugin.BaseRange.Value * meal);

        /// <summary>Seconds for the whole trip, there and back.</summary>
        public static float TripSeconds(float distance, float meal) =>
            Mathf.Max(Plugin.MinTripSeconds.Value, 2f * distance / 100f * Plugin.SecondsPer100m.Value / Mathf.Max(0.05f, meal));

        /// <summary>The point <paramref name="distance"/> metres from <paramref name="from"/> towards compass <paramref name="degrees"/> (0 = north), kept inside the world.</summary>
        public static Vector3 TargetPoint(Vector3 from, float degrees, float distance)
        {
            float rad = degrees * Mathf.Deg2Rad;
            var dir = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
            var target = from + dir * distance;
            for (int i = 0; i < 400 && new Vector2(target.x, target.z).magnitude > WorldRadius && distance > 0f; i++)
            {
                distance = Mathf.Max(0f, distance - 50f);
                target = from + dir * distance;
            }
            target.y = from.y;
            return target;
        }

        public static string CompassName(float degrees)
        {
            string[] names = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            int i = Mathf.RoundToInt(Mathf.Repeat(degrees, 360f) / 45f) % 8;
            return names[i];
        }

        public static float Heading(Vector3 from, Vector3 to) =>
            Mathf.Repeat(Mathf.Atan2(to.x - from.x, to.z - from.z) * Mathf.Rad2Deg, 360f);

        public static string FormatDuration(double seconds)
        {
            if (seconds < 0)
            {
                seconds = 0;
            }
            int total = Mathf.CeilToInt((float)seconds);
            return total >= 3600
                ? $"{total / 3600}h {total % 3600 / 60:00}m"
                : $"{total / 60}:{total % 60:00}";
        }

        public static string FormatDistance(float metres) =>
            metres >= 1000f ? $"{metres / 1000f:0.0} km" : $"{Mathf.RoundToInt(metres)} m";

        /// <summary>Distance on the ground from <paramref name="p"/> to the route line from <paramref name="a"/> to <paramref name="b"/>.</summary>
        private static float DistanceToRoute(Vector3 p, Vector3 a, Vector3 b)
        {
            var ap = new Vector2(p.x - a.x, p.z - a.z);
            var ab = new Vector2(b.x - a.x, b.z - a.z);
            float t = ab.sqrMagnitude < 0.01f ? 0f : Mathf.Clamp01(Vector2.Dot(ap, ab) / ab.sqrMagnitude);
            return (ap - ab * t).magnitude;
        }

        // ------------------------------------------------------------ what the raven saw (server)

        private static readonly System.Reflection.FieldInfo s_objectsBySector = AccessTools.Field(typeof(ZDOMan), "m_objectsBySector");
        private static readonly HashSet<string> s_reportedUnknownLocations = new HashSet<string>();
        private static bool s_checkedOreNames;

        /// <summary>
        /// Works out what a raven flying from <paramref name="trip"/>'s roost to its target and back has seen.
        /// Places come from the world's location list, which covers the whole world even where nobody has been;
        /// ore only exists where the land has been loaded before.
        /// </summary>
        public static TripResult Survey(ScoutTrip trip, string key)
        {
            var result = new TripResult
            {
                Key = key,
                From = trip.From,
                Target = trip.Target,
                RevealRadius = Plugin.RevealRadius.Value,
            };
            float radius = Mathf.Max(0f, Plugin.SpotRadius.Value);

            FindPlaces(trip, radius, result.Findings);
            bool wildLand = false;
            if (Plugin.SpotOres.Value)
            {
                wildLand = FindOres(trip, radius, result.Findings);
            }
            result.Report = Report(trip, result.Findings, wildLand);
            return result;
        }

        private static void FindPlaces(ScoutTrip trip, float radius, List<Finding> findings)
        {
            var rules = new List<NameRule>();
            if (Plugin.SpotDungeons.Value) rules.Add(new NameRule(Finding.Kind.Dungeon, Plugin.DungeonNames.Value));
            if (Plugin.SpotBosses.Value) rules.Add(new NameRule(Finding.Kind.Boss, Plugin.BossNames.Value));
            if (Plugin.SpotTraders.Value) rules.Add(new NameRule(Finding.Kind.Trader, Plugin.TraderNames.Value));
            if (Plugin.SpotOther.Value) rules.Add(new NameRule(Finding.Kind.Other, Plugin.OtherNames.Value));
            if (rules.Count == 0 || !ZoneSystem.instance)
            {
                return;
            }

            var unknown = new HashSet<string>();
            foreach (var instance in ZoneSystem.instance.GetLocationList())
            {
                if (instance.m_location == null || DistanceToRoute(instance.m_position, trip.From, trip.Target) > radius)
                {
                    continue;
                }
                string prefab = instance.m_location.m_prefab.Name ?? instance.m_location.m_prefabName ?? "";
                if (!Match(rules, prefab, out var kind, out var label))
                {
                    unknown.Add(prefab);
                    continue;
                }
                findings.Add(new Finding { Type = kind, Label = label, Position = instance.m_position });
            }

            // Helps tune the name lists: say once which places the raven flew past but doesn't pin.
            var fresh = unknown.Where(n => s_reportedUnknownLocations.Add(n)).OrderBy(n => n).ToList();
            if (fresh.Count > 0)
            {
                Plugin.Log.LogInfo($"Raven flew past places it doesn't pin: {string.Join(", ", fresh)}");
            }
        }

        /// <summary>
        /// One category's name list. A class, not a tuple: Valheim's .NET has no System.ValueTuple, so tuples
        /// compile but fail when the game runs them.
        /// </summary>
        private class NameRule
        {
            public readonly Finding.Kind Kind;
            public readonly Dictionary<string, string> Names;

            public NameRule(Finding.Kind kind, string names)
            {
                Kind = kind;
                Names = ParseNames(names);
            }
        }

        /// <summary>Longest matching name start wins, so "SunkenCrypt" isn't mistaken for "Crypt"-anything.</summary>
        private static bool Match(List<NameRule> rules, string prefab,
            out Finding.Kind kind, out string label)
        {
            kind = Finding.Kind.Other;
            label = null;
            int best = -1;
            foreach (var rule in rules)
            {
                foreach (var pair in rule.Names)
                {
                    if (pair.Key.Length > best && prefab.StartsWith(pair.Key, System.StringComparison.OrdinalIgnoreCase))
                    {
                        best = pair.Key.Length;
                        kind = rule.Kind;
                        label = pair.Value;
                    }
                }
            }
            return label != null;
        }

        /// <summary>Adds ore deposits near the route. Returns true if part of the route is land nobody has loaded yet.</summary>
        private static bool FindOres(ScoutTrip trip, float radius, List<Finding> findings)
        {
            var ores = ParseNames(Plugin.OreNames.Value);
            if (ores.Count == 0)
            {
                return false;
            }
            CheckOreNames(ores);
            var byHash = new Dictionary<int, string>();
            foreach (var pair in ores)
            {
                byHash[pair.Key.GetStableHashCode()] = pair.Value;
            }

            // Only look in the 64 m sectors the route passes over, not the whole world.
            var sectors = s_objectsBySector?.GetValue(ZDOMan.instance) as List<ZDO>[];
            var found = new List<Finding>();
            bool wildLand = false;
            if (sectors != null)
            {
                var visited = new HashSet<uint>();
                float length = DistanceXZ(trip.From, trip.Target);
                int steps = Mathf.Max(1, Mathf.CeilToInt(length / 32f));
                int reach = Mathf.CeilToInt(radius / 64f) + 1;
                for (int s = 0; s <= steps; s++)
                {
                    var p = Vector3.Lerp(trip.From, trip.Target, s / (float)steps);
                    var zone = ZoneSystem.GetZone(p);
                    for (int dy = -reach; dy <= reach; dy++)
                    {
                        for (int dx = -reach; dx <= reach; dx++)
                        {
                            uint index = ZoneSystem.SectorToIndex(zone.x + dx, zone.y + dy).Sector;
                            if (index == 0 || index >= sectors.Length || !visited.Add(index))
                            {
                                continue;
                            }
                            var list = sectors[index];
                            if (list == null || list.Count == 0)
                            {
                                if (dx == 0 && dy == 0)
                                {
                                    wildLand = true; // right under the raven and nothing there yet
                                }
                                continue;
                            }
                            foreach (var zdo in list)
                            {
                                if (byHash.TryGetValue(zdo.GetPrefab(), out var label) &&
                                    DistanceToRoute(zdo.GetPosition(), trip.From, trip.Target) <= radius)
                                {
                                    found.Add(new Finding { Type = Finding.Kind.Ore, Label = label, Position = zdo.GetPosition() });
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                Plugin.Log.LogWarning("Couldn't read the world's object list; searching every ore object instead.");
                foreach (var pair in ores)
                {
                    var list = new List<ZDO>();
                    int index = 0;
                    while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(pair.Key, list, ref index))
                    {
                    }
                    foreach (var zdo in list)
                    {
                        if (DistanceToRoute(zdo.GetPosition(), trip.From, trip.Target) <= radius)
                        {
                            found.Add(new Finding { Type = Finding.Kind.Ore, Label = pair.Value, Position = zdo.GetPosition() });
                        }
                    }
                }
            }

            findings.AddRange(Cluster(found, 40f));
            return wildLand;
        }

        /// <summary>Deposits of the same ore within <paramref name="distance"/> of each other become one pin.</summary>
        private static List<Finding> Cluster(List<Finding> ores, float distance)
        {
            var clusters = new List<Finding>();
            foreach (var ore in ores)
            {
                var near = clusters.FirstOrDefault(c => c.Label == ore.Label && DistanceXZ(c.Position, ore.Position) < distance);
                if (near == null)
                {
                    clusters.Add(ore);
                    continue;
                }
                near.Position = (near.Position * near.Count + ore.Position) / (near.Count + 1);
                near.Count++;
            }
            return clusters;
        }

        private static void CheckOreNames(Dictionary<string, string> ores)
        {
            if (s_checkedOreNames || !ZNetScene.instance)
            {
                return;
            }
            s_checkedOreNames = true;
            var missing = ores.Keys.Where(n => !ZNetScene.instance.GetPrefab(n)).ToList();
            if (missing.Count > 0)
            {
                Plugin.Log.LogWarning($"OreNames lists objects this game doesn't have: {string.Join(", ", missing)}");
            }
        }

        /// <summary>"Name=Label,Name2=Label2" to a dictionary. A name without a label is its own label.</summary>
        public static Dictionary<string, string> ParseNames(string text)
        {
            var result = new Dictionary<string, string>();
            foreach (var part in (text ?? "").Split(','))
            {
                var bits = part.Split('=');
                string name = bits[0].Trim();
                if (name.Length == 0)
                {
                    continue;
                }
                result[name] = bits.Length > 1 && bits[1].Trim().Length > 0 ? bits[1].Trim() : name;
            }
            return result;
        }

        // ------------------------------------------------------------ report text

        public static string Report(ScoutTrip trip, List<Finding> findings, bool wildLand)
        {
            var sb = new StringBuilder();
            float heading = Heading(trip.From, trip.Target);
            sb.Append($"Day {Day(trip.ReturnAt)}: flew {FormatDistance(trip.Distance)} {CompassName(heading)}");
            var biomes = BiomesAlong(trip.From, trip.Target);
            if (biomes.Count > 0)
            {
                sb.Append($" over {string.Join(", ", biomes)}");
            }
            sb.Append('.');

            var places = findings.Where(f => f.Type != Finding.Kind.Ore)
                .GroupBy(f => f.Label).Select(g => g.Count() == 1 ? g.Key : $"{g.Count()} x {g.Key}").ToList();
            var ores = findings.Where(f => f.Type == Finding.Kind.Ore)
                .GroupBy(f => f.Label).Select(g => g.Count() == 1 ? g.Key : $"{g.Count()} x {g.Key}").ToList();

            if (places.Count == 0 && ores.Count == 0)
            {
                sb.Append(" Nothing special spotted.");
            }
            else
            {
                if (places.Count > 0)
                {
                    sb.Append($" Spotted: {string.Join(", ", places)}.");
                }
                if (ores.Count > 0)
                {
                    sb.Append($" Ore: {string.Join(", ", ores)}.");
                }
            }
            if (wildLand && Plugin.SpotOres.Value)
            {
                sb.Append(" Some land was wild and unvisited, so its ore can't be seen yet.");
            }
            return sb.ToString();
        }

        /// <summary>Biomes under the route, in the order the raven met them.</summary>
        private static List<string> BiomesAlong(Vector3 from, Vector3 to)
        {
            var result = new List<string>();
            if (WorldGenerator.instance == null)
            {
                return result;
            }
            int steps = Mathf.Max(1, Mathf.CeilToInt(DistanceXZ(from, to) / 50f));
            for (int i = 0; i <= steps; i++)
            {
                var p = Vector3.Lerp(from, to, i / (float)steps);
                string name = BiomeName(WorldGenerator.instance.GetBiome(p.x, p.z));
                if (name != null && !result.Contains(name))
                {
                    result.Add(name);
                }
            }
            return result;
        }

        private static string BiomeName(Heightmap.Biome biome)
        {
            switch (biome)
            {
                case Heightmap.Biome.Meadows: return "Meadows";
                case Heightmap.Biome.BlackForest: return "Black Forest";
                case Heightmap.Biome.Swamp: return "Swamp";
                case Heightmap.Biome.Mountain: return "Mountains";
                case Heightmap.Biome.Plains: return "Plains";
                case Heightmap.Biome.Ocean: return "Ocean";
                case Heightmap.Biome.Mistlands: return "Mistlands";
                case Heightmap.Biome.AshLands: return "Ashlands";
                case Heightmap.Biome.DeepNorth: return "Deep North";
                default: return null;
            }
        }

        // ------------------------------------------------------------ network format

        public static ZPackage Serialize(TripResult r)
        {
            var pkg = new ZPackage();
            pkg.Write(ResultVersion);
            pkg.Write(r.Key ?? "");
            pkg.Write(r.From);
            pkg.Write(r.Target);
            pkg.Write(r.RevealRadius);
            pkg.Write(r.Report ?? "");
            pkg.Write(r.Findings.Count);
            foreach (var f in r.Findings)
            {
                pkg.Write((int)f.Type);
                pkg.Write(f.Label ?? "");
                pkg.Write(f.Position);
                pkg.Write(f.Count);
            }
            return pkg;
        }

        public static TripResult Deserialize(ZPackage pkg)
        {
            pkg.SetPos(0);
            if (pkg.ReadInt() != ResultVersion)
            {
                return null;
            }
            var r = new TripResult
            {
                Key = pkg.ReadString(),
                From = pkg.ReadVector3(),
                Target = pkg.ReadVector3(),
                RevealRadius = pkg.ReadSingle(),
                Report = pkg.ReadString(),
            };
            int count = pkg.ReadInt();
            for (int i = 0; i < count; i++)
            {
                r.Findings.Add(new Finding
                {
                    Type = (Finding.Kind)pkg.ReadInt(),
                    Label = pkg.ReadString(),
                    Position = pkg.ReadVector3(),
                    Count = pkg.ReadInt(),
                });
            }
            return r;
        }
    }
}
