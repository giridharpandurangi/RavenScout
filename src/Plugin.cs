using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;

namespace RavenScout
{
    [BepInPlugin(ModGuid, ModName, ModVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class Plugin : BaseUnityPlugin
    {
        public const string ModGuid = "com.ravenscout.valheim";
        public const string ModName = "Raven Scout";
        public const string ModVersion = "0.4.1";

        public const string RoostPrefabName = "piece_ravenroost";

        internal static ManualLogSource Log;

        // Trips
        internal static ConfigEntry<float> BaseRange;
        internal static ConfigEntry<float> MinDistance;
        internal static ConfigEntry<float> SecondsPer100m;
        internal static ConfigEntry<float> MinTripSeconds;
        internal static ConfigEntry<int> MaxTripsPerDay;

        // What the raven sees
        internal static ConfigEntry<float> RevealRadius;
        internal static ConfigEntry<float> SpotRadius;
        internal static ConfigEntry<bool> SpotDungeons;
        internal static ConfigEntry<string> DungeonNames;
        internal static ConfigEntry<bool> SpotBosses;
        internal static ConfigEntry<string> BossNames;
        internal static ConfigEntry<bool> SpotTraders;
        internal static ConfigEntry<string> TraderNames;
        internal static ConfigEntry<bool> SpotOther;
        internal static ConfigEntry<string> OtherNames;
        internal static ConfigEntry<bool> SpotOres;
        internal static ConfigEntry<string> OreNames;
        internal static ConfigEntry<bool> SpotFood;
        internal static ConfigEntry<string> FoodNames;
        internal static ConfigEntry<float> FoodPinSpacing;
        internal static ConfigEntry<float> OrePinSpacing;

        // Live tracking
        internal static ConfigEntry<bool> LiveTracking;
        internal static ConfigEntry<bool> ShowRavenOnMap;
        internal static ConfigEntry<bool> LiveMessages;

        // Food
        internal enum HungryBehaviour { FlySlowly, Refuse }

        /// <summary>How food and ore pins are grouped on the map.</summary>
        internal enum PinGrouping { Area, Patch, Each }

        internal static ConfigEntry<PinGrouping> FoodPinGrouping;
        internal static ConfigEntry<PinGrouping> OrePinGrouping;

        // Map or report
        internal enum NoMapBehaviour { Auto, MapOnly, ReportOnly, Both }

        internal enum Precision { Vague, Rough, Exact }

        internal static ConfigEntry<NoMapBehaviour> NoMapMode;
        internal static ConfigEntry<Precision> ReportPrecision;
        internal static ConfigEntry<KeyCode> RenameKey;
        internal static ConfigEntry<bool> ShareReports;
        internal static ConfigEntry<string> CopyCost;
        internal static ConfigEntry<bool> FeatherGuideOn;
        internal static ConfigEntry<int> FeatherGuideUses;

        internal static ConfigEntry<bool> UseFood;
        internal static ConfigEntry<HungryBehaviour> Hungry;
        internal static ConfigEntry<float> HungrySpeed;
        internal static ConfigEntry<int> MaxMeals;
        internal static ConfigEntry<float> MaxFoodSpeed;
        internal static ConfigEntry<float> RawMeatSpeed;
        internal static ConfigEntry<string> RawMeatItems;

        private Harmony _harmony;

        // v0.1 default, kept so configs still holding it get the new list (see UpgradeDefault).
        private const string OtherNamesV1 =
            "GoblinCamp=Fuling Village,DrakeNest=Drake Nest,TarPit=Tar Pit,Mistlands_Giant=Giant Remains," +
            "Mistlands_Excavation=Dvergr Excavation,WoodVillage=Abandoned Village,StoneHenge=Stone Circle,NorthVillage=Northern Village";

        private const string OtherNamesDefault = OtherNamesV1 +
            ",Mistlands_GuardTower=Dvergr Tower,Mistlands_Lighthouse=Dvergr Lighthouse,MorgenHole=Morgen Hole," +
            "CharredRuins=Charred Ruins,CharredTowerRuins=Charred Tower,VoltureNest=Volture Nest,HotSpring=Hot Spring," +
            "NorthMemorialPlace=Memorial Site";

        /// <summary>
        /// BepInEx never changes a value already in the config file, so new default entries wouldn't reach
        /// existing players. If the value is still an old default (nobody edited it), switch to the new one.
        /// </summary>
        private void UpgradeDefault(ConfigEntry<string> entry, string oldDefault)
        {
            string current = entry.Value;
            string fresh = (string)entry.DefaultValue;
            if (current != fresh && current == oldDefault)
            {
                entry.Value = fresh;
                Log.LogInfo($"{entry.Definition.Key}: updated to the new default list");
            }
        }

        /// <summary>Gameplay settings come from the server so everyone plays by the same rules.</summary>
        internal static ConfigDescription Synced(string description) =>
            new ConfigDescription(description, null, new ConfigurationManagerAttributes { IsAdminOnly = true });

        private void Awake()
        {
            Log = Logger;

            BaseRange = Config.Bind("Trip", "BaseRange", 1500f,
                Synced("How far (metres) a raven with an ordinary meal (x1.0) can fly out. The meal's speed multiplies it: x1.6 food = 2400 m."));
            MinDistance = Config.Bind("Trip", "MinDistance", 200f,
                Synced("Shortest scouting trip, in metres."));
            SecondsPer100m = Config.Bind("Trip", "SecondsPer100m", 15f,
                Synced("Seconds a raven at x1.0 speed needs per 100 m. The trip is there and back, so 1 km out takes 300 s at x1.0. In-game time: sleeping speeds it up."));
            MinTripSeconds = Config.Bind("Trip", "MinTripSeconds", 30f,
                Synced("Shortest possible trip, in seconds."));
            MaxTripsPerDay = Config.Bind("Trip", "MaxTripsPerDay", 0,
                Synced("How many trips each roost can start per in-game day. 0 = unlimited (a roost always has only one raven out)."));

            NoMapMode = Config.Bind("Report", "NoMapMode", NoMapBehaviour.Auto,
                Synced("What the raven brings back. Auto: map reveal and pins, or a Scout Report item in no-map worlds (and for players " +
                       "who turned their own map off). MapOnly, ReportOnly, Both: as named. A no-map world never gets map changes: " +
                       "there MapOnly gives a report instead."));
            ReportPrecision = Config.Bind("Report", "ReportPrecision", Precision.Vague,
                Synced("How exact Scout Reports are. Vague: \"about 300 m\", \"far, over 2 km\", north-east. " +
                       "Rough: to the nearest 100 m. Exact: metres and compass degrees."));
            RenameKey = Config.Bind("Keys", "Rename", KeyCode.N, "While looking at a roost: give it a name (shown on its reports).");
            ShareReports = Config.Bind("Report", "ShareReports", true,
                Synced("Show a Copy report button at the roost: anyone can take a copy of its last report, to keep or give to a friend."));
            CopyCost = Config.Bind("Report", "CopyCost", "Feathers:1",
                Synced("What a copy costs, as PrefabName:Amount, comma-separated. Empty = free."));
            FeatherGuideOn = Config.Bind("Report", "FeatherGuide", true,
                Synced("Using a Scout Report (hotbar key or right-click) makes the raven's feather point left, right, ahead or behind " +
                       "toward the nearest find on it. Never a marker or compass. Turn off for hardcore servers."));
            FeatherGuideUses = Config.Bind("Report", "FeatherGuideUses", 3,
                Synced("How many times each report's feather can point the way."));

            RevealRadius = Config.Bind("Spotting", "RevealRadius", 120f,
                Synced("Half the width of the strip of map the raven reveals along its route, in metres."));
            SpotRadius = Config.Bind("Spotting", "SpotRadius", 150f,
                Synced("How far from its route (metres) the raven spots places and ore."));
            SpotDungeons = Config.Bind("Spotting", "SpotDungeons", true, Synced("Pin dungeons: crypts, caves, mines."));
            DungeonNames = Config.Bind("Spotting", "DungeonNames",
                "Crypt=Burial Chambers,HalfBurried_ForestCrypt=Burial Chambers,SunkenCrypt=Sunken Crypt,TrollCave=Troll Cave," +
                "MountainCave=Frost Cave,Mistlands_DvergrTownEntrance=Infested Mine,CharredFortress=Charred Fortress," +
                "Hildir_crypt=Smouldering Tomb,Hildir_cave=Howling Cavern,Hildir_plainsfortress=Sealed Tower",
                Synced("Location prefab name (or start of it) = pin label, comma-separated."));
            SpotBosses = Config.Bind("Spotting", "SpotBosses", true, Synced("Pin boss altars."));
            BossNames = Config.Bind("Spotting", "BossNames",
                "Eikthyrnir=Eikthyr,GDKing=The Elder,Bonemass=Bonemass,Dragonqueen=Moder,GoblinKing=Yagluth," +
                "Mistlands_DvergrBossEntrance=The Queen,FaderLocation=Fader,DN_Bossroom=Deep North boss",
                Synced("Location prefab name (or start of it) = pin label, comma-separated."));
            SpotTraders = Config.Bind("Spotting", "SpotTraders", true, Synced("Pin traders."));
            TraderNames = Config.Bind("Spotting", "TraderNames",
                "Vendor_BlackForest=Haldor,Hildir_camp=Hildir,BogWitch_Camp=Bog Witch",
                Synced("Location prefab name (or start of it) = pin label, comma-separated."));
            SpotOther = Config.Bind("Spotting", "SpotOther", true, Synced("Pin other interesting places."));
            OtherNames = Config.Bind("Spotting", "OtherNames", OtherNamesDefault,
                Synced("Location prefab name (or start of it) = pin label, comma-separated."));
            UpgradeDefault(OtherNames, OtherNamesV1);
            SpotOres = Config.Bind("Spotting", "SpotOres", true,
                Synced("Pin ore deposits. Only land someone has already been near has ore in it, so far wild land shows none."));
            OreNames = Config.Bind("Spotting", "OreNames",
                "rock4_copper=Copper,rock4_copper_frac=Copper,MineRock_Copper=Copper,MineRock_Tin=Tin,Pickable_Tin=Tin," +
                "rock3_silver=Silver,rock3_silver_frac=Silver,silvervein=Silver,silvervein_frac=Silver,MineRock_Obsidian=Obsidian," +
                "mudpile=Iron Scrap,mudpile2=Iron Scrap,mudpile_frac=Iron Scrap,mudpile2_frac=Iron Scrap,mudpile_beacon=Iron Scrap," +
                "giant_brain=Soft Tissue,giant_brain_frac=Soft Tissue,MineRock_Meteorite=Flametal,FlametalRockstand=Flametal," +
                "FlametalRockstand_frac=Flametal,goldvein=Gold,goldvein_frac=Gold",
                Synced("Ore object prefab name (exact) = pin label, comma-separated. Add :N to the label to need at least N close together."));
            SpotFood = Config.Bind("Spotting", "SpotFood", false,
                Synced("Pin wild food: berries, mushrooms, thistle, wild seeds. Off by default: like ore, plants only exist in land someone " +
                       "has already been near, so it's a rough guide, not a full list. " +
                       "Picked plants and plants next to anything player-built (farms) are skipped."));
            FoodNames = Config.Bind("Spotting", "FoodNames",
                "RaspberryBush=Raspberries:3,BlueberryBush=Blueberries:3,CloudberryBush=Cloudberries:3,LingonberryBush=Lingonberries:3," +
                "Pickable_Mushroom=Mushrooms:2,Pickable_Mushroom_yellow=Yellow Mushrooms,Pickable_Mushroom_blue=Blue Mushrooms," +
                "Pickable_Mushroom_Magecap=Magecap,Pickable_Mushroom_JotunPuffs=Jotun Puffs,Pickable_SmokePuff=Smoke Puffs," +
                "Pickable_Thistle=Thistle:2,Pickable_Dandelion=Dandelions:3,Pickable_SeedCarrot=Carrot Seeds,Pickable_SeedTurnip=Turnip Seeds," +
                "Pickable_SeedOnion=Onion Seeds,Pickable_Barley_Wild=Wild Barley,Pickable_Flax_Wild=Wild Flax,Pickable_Fiddlehead=Fiddlehead," +
                "Pickable_RoyalJelly=Royal Jelly",
                Synced("Plant prefab name (exact) = pin label:N, comma-separated. N = how many must grow close together (within 60 m) for a pin; " +
                       "it stops single raspberry bushes from covering the map. Remove an entry to never pin it."));

            FoodPinSpacing = Config.Bind("Spotting", "FoodPinSpacing", 300f,
                Synced("Within this many metres, patches of the same food share one pin (at the biggest patch, with the total count)."));
            OrePinSpacing = Config.Bind("Spotting", "OrePinSpacing", 100f,
                Synced("Within this many metres, deposits of the same ore share one pin (with the total count)."));
            FoodPinGrouping = Config.Bind("Spotting", "FoodPinGrouping", PinGrouping.Area,
                Synced("How food is pinned. Area: one pin per food within FoodPinSpacing, with the total count. " +
                       "Patch: one pin per patch (plants within 60 m, at least the :N in FoodNames). Each: a pin on every single plant (many pins)."));
            OrePinGrouping = Config.Bind("Spotting", "OrePinGrouping", PinGrouping.Area,
                Synced("How ore is pinned. Area: one pin per ore within OrePinSpacing, with the total count. " +
                       "Patch: one pin per group of deposits within 60 m. Each: a pin on every single deposit."));

            LiveTracking = Config.Bind("Live", "LiveTracking", true,
                Synced("Reveal the map and add pins while the raven flies, instead of all at once when it returns."));
            ShowRavenOnMap = Config.Bind("Live", "ShowRavenOnMap", true,
                "Show your flying raven as a feather on the map and minimap (your own choice, not synced).");
            LiveMessages = Config.Bind("Live", "LiveMessages", true,
                "Show a top-left message when your flying raven spots something (your own choice, not synced).");

            UseFood = Config.Bind("Food", "UseFood", true,
                Synced("Ravens eat one stored meal per trip; better food flies farther and faster. Turn off to ignore food entirely."));
            Hungry = Config.Bind("Food", "HungryRaven", HungryBehaviour.FlySlowly,
                Synced("What a raven does with no meals stored: FlySlowly (at HungrySpeed) or Refuse to fly."));
            HungrySpeed = Config.Bind("Food", "HungrySpeed", 0.5f,
                Synced("Range and speed multiplier for a hungry raven when HungryRaven = FlySlowly."));
            MaxMeals = Config.Bind("Food", "MaxMeals", 10, Synced("How many meals a roost can store."));
            MaxFoodSpeed = Config.Bind("Food", "MaxFoodSpeed", 4f,
                Synced("Biggest multiplier a meal can give. Cooked food gives 1 + health / 50, e.g. 30 hp = x1.6, 75 hp = x2.5."));
            RawMeatSpeed = Config.Bind("Food", "RawMeatSpeed", 1.25f, Synced("Multiplier for raw meat."));
            RawMeatItems = Config.Bind("Food", "RawMeatItems",
                "RawMeat,DeerMeat,NeckTail,WolfMeat,LoxMeat,SerpentMeat,BugMeat,HareMeat,ChickenMeat,AsksvinMeat,VoltureMeat,BoneMawSerpentMeat",
                Synced("Comma-separated item prefab names ravens eat raw."));

            RoostTiers.Bind(Config);

            PrefabManager.OnVanillaPrefabsAvailable += AddRoostPiece;

            _harmony = new Harmony(ModGuid);
            _harmony.PatchAll(typeof(Plugin).Assembly);

            Log.LogInfo($"{ModName} {ModVersion} loaded");
        }

        private void AddRoostPiece()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= AddRoostPiece;

            var config = new PieceConfig
            {
                Name = "Raven Roost",
                Description = "Send a raven on a long scouting trip. It maps the land it flies over and pins what it sees.",
                PieceTable = "Hammer",
                Category = "Misc",
                CraftingStation = "piece_workbench",
                Requirements = new[]
                {
                    new RequirementConfig("Wood", 10),
                    new RequirementConfig("Feathers", 4),
                    new RequirementConfig("FineWood", 4),
                },
            };

            var piece = new CustomPiece(RoostPrefabName, "piece_chest_wood", config);
            var prefab = piece.PiecePrefab;

            // A small box for what the raven brings back: Scout Reports (and small finds later).
            var container = prefab.GetComponent<Container>();
            container.m_name = "Raven Roost";
            container.m_width = 4;
            container.m_height = 2;

            prefab.AddComponent<RavenRoost>();

            RavenFlight.Prepare();
            try
            {
                RoostModel.Decorate(prefab);
            }
            catch (System.Exception e)
            {
                Log.LogError($"Couldn't build the roost model, using the plain chest look: {e}");
            }

            PieceManager.Instance.AddPiece(piece);
            ScoutReportItem.Register();
        }

        private void Update()
        {
            ScoutWindow.Tick();

            if (!Scouting.IsActive)
            {
                return;
            }
            ScoutNet.ServerTick();
            ScoutLive.Tick();
            HandleKeys();
        }

        private static void HandleKeys()
        {
            var player = Player.m_localPlayer;
            if (!player || InventoryGui.IsVisible() || Menu.IsVisible() || TextInput.IsVisible() ||
                global::Console.IsVisible() || (Chat.instance && Chat.instance.HasFocus()))
            {
                return;
            }
            var hover = player.GetHoverObject();
            var roost = hover ? hover.GetComponentInParent<RavenRoost>() : null;
            if (roost && ZInput.GetKeyDown(RenameKey.Value, false))
            {
                TextInput.instance.RequestText(roost, "Name this roost", 24);
            }
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
