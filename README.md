# Raven Scout (v0.1.2)

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
   With **live tracking** (on by default) you can follow it: a feather on your map and minimap flies out
   and back, the map is revealed behind it as it flies out, and pins appear as it passes each find.
5. When it returns you get a message, and on **your map**:
   - a revealed strip along its route
   - pins for places it saw near the route: dungeons, boss altars, traders, villages and more
   - pins for ore deposits and wild food (berries, mushrooms, thistle, wild seeds...) in land that has
     already been loaded
   The roost window shows the trip's **report** (distance, biomes, what was spotted).

## Rules

- **Range** = 1500 m × the meal's multiplier. Cooked food gives 1 + health / 50 (30 hp = x1.6 → 2.4 km,
  75 hp = x2.5 → 3.75 km), raw meat x1.25. A hungry raven flies at x0.5 (or refuses, if `HungryRaven = Refuse`).
- **Trip time** = there and back at 15 s per 100 m, divided by the meal's multiplier. So better food flies
  farther in about the same time. In-game time: sleeping speeds it up.
- One raven per roost. A roost can't be deconstructed while its raven is out.
- **Food**: each plant in `FoodNames` can need a patch (e.g. `RaspberryBush=Raspberries:3` = at least 3
  bushes within 60 m), so single bushes don't fill the map. Picked plants and plants next to anything
  player-built (farms) are skipped. Turn it all off with `SpotFood = false`.
- **No-map worlds**: the map is never revealed or pinned; the report at the roost is the news (more in v0.2).
- **Ore**: Valheim only creates ore rocks (and wild plants) when someone has been near that land. In wild land nobody has
  visited yet the raven can't see ore (the report says so). Dungeons, bosses and traders are seen anywhere.
- **Multiplayer**: the server tracks trips and works out what the raven saw. The findings go to the player
  who sent the raven; if they're offline, the raven keeps its news until they're back.

All settings are in `BepInEx/config/com.ravenscout.valheim.cfg` (gameplay settings are synced from the server).

## Files

- `src/Plugin.cs`: config, roost piece registration
- `src/RavenRoost.cs`: per-roost logic: feed, send, the sitting raven, hover text
- `src/Scouting.cs`: trip storage, range and time, working out what the raven saw, the report
- `src/ScoutMap.cs`: revealing the route and adding pins on the player's map
- `src/ScoutNet.cs`: multiplayer: server watches trips, sends live plans and results, saves reports
- `src/ScoutLive.cs`: live tracking: raven marker, reveal and pins while it flies
- `src/ScoutWindow.cs`: the roost window
- `src/RavenFlight.cs`, `src/RoostModel.cs`, `src/RavenFood.cs`: raven visuals, roost look and food (from Raven Post)
- `src/Patches.cs`: Harmony patches for hover text, feeding and removal

## Roadmap

| Version | Feature |
|---|---|
| v0.1 | Roost, one scouting trip, map reveal, pins, report; live tracking and food pins (0.1.2) |
| v0.2 | No-map worlds: Scout Report item written from the roost's point of view |
| v0.3 | Report precision options, copy reports for friends, feather guide |
| Later | Click a spot on the map as the target; report history |
| Later | Roost tiers through the biomes: longer range, faster trips, more things spotted |
| Later | Small finds: a feather, a seed, a bit of ore |
| Later | Shared or private findings in multiplayer; two ravens at high tiers; loop routes |
| v1.0 | Thunderstore release |
