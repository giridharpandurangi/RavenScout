# Raven Scout (v0.1)

A Valheim mod: build a **Raven Roost** at your base and send its raven on long scouting trips to far-away
land. It flies out over real time, comes back, reveals a strip of map along its route and pins what it saw.

Unlike companion or map mods that reveal or pin things **around the player**, Raven Scout explores
**distant land from your base**, on a real trip, while you do something else.

Everyone (server and players) needs the mod. Needs BepInEx and Jötunn.

## Build

```bash
dotnet build -c Release
```

Output: `bin/Release/RavenScout.dll`. To also copy it into the r2modman profile and the local dedicated
server (if it has BepInEx):

```bash
dotnet build -c Release -p:Deploy=true
```

Paths are at the top of `RavenScout.csproj` (`ValheimDir`, `ProfileDir`, `ServerDir`).

## How to play

1. Hammer → Misc → **Raven Roost** (needs a workbench). Costs 10 Wood, 4 Feathers, 4 Fine Wood.
2. **Feed the raven**: look at the roost and press the hotbar number of a food, or open it and click
   **Feed** (uses the best food you carry). A roost stores up to 10 meals; each trip eats the best one.
3. Open the roost (**E**). In the **Raven Roost** window next to its slots:
   - pick a **direction** with the compass buttons (fine-tune with -15° / +15°)
   - pick a **distance** (-500 / -100 / +100 / +500 / Max)
   - click **Send raven**.
4. The raven flies out and back in real (in-game) time. Hover the roost to see when it's back.
5. When it returns you get a message, and on **your map**:
   - a revealed strip along its route
   - pins for places it saw near the route: dungeons, boss altars, traders, villages and more
   - pins for ore deposits in land that has already been loaded
   The roost window shows the trip's **report** (distance, biomes, what was spotted).

## Rules

- **Range** = 1500 m × the meal's multiplier. Cooked food gives 1 + health / 50 (30 hp = x1.6 → 2.4 km,
  75 hp = x2.5 → 3.75 km), raw meat x1.25. A hungry raven flies at x0.5 (or refuses, if `HungryRaven = Refuse`).
- **Trip time** = there and back at 15 s per 100 m, divided by the meal's multiplier. So better food flies
  farther in about the same time. In-game time: sleeping speeds it up.
- One raven per roost. A roost can't be deconstructed while its raven is out.
- **Ore**: Valheim only creates ore rocks when someone has been near that land. In wild land nobody has
  visited yet the raven can't see ore (the report says so). Dungeons, bosses and traders are seen anywhere.
- **Multiplayer**: the server tracks trips and works out what the raven saw. The findings go to the player
  who sent the raven; if they're offline, the raven keeps its news until they're back.

All settings are in `BepInEx/config/com.ravenscout.valheim.cfg` (gameplay settings are synced from the server).

## Files

- `src/Plugin.cs`: config, roost piece registration
- `src/RavenRoost.cs`: per-roost logic: feed, send, the sitting raven, hover text
- `src/Scouting.cs`: trip storage, range and time, working out what the raven saw, the report
- `src/ScoutMap.cs`: revealing the route and adding pins on the player's map
- `src/ScoutNet.cs`: multiplayer: server watches trips, sends results, saves reports
- `src/ScoutWindow.cs`: the roost window
- `src/RavenFlight.cs`, `src/RoostModel.cs`, `src/RavenFood.cs`: raven visuals, roost look and food (from Raven Post)
- `src/Patches.cs`: Harmony patches for hover text, feeding and removal

## Roadmap

| Version | Feature |
|---|---|
| v0.1 | Roost, one scouting trip, map reveal, pins, report |
| v0.2 | Click a spot on the map as the target; report history |
| v0.3 | Roost tiers through the biomes: longer range, faster trips, more things spotted |
| v0.4 | Small finds: a feather, a seed, a bit of ore |
| v0.5 | Shared or private findings in multiplayer; trips-per-day balance |
| v0.6 | Two ravens at high tiers; loop routes |
| v1.0 | Thunderstore release |
