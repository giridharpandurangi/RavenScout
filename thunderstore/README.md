# Raven Scout

Build a **Raven Roost** at your base and send its raven on a **long scouting trip** to far-away land. It
flies out over real time, comes back, and tells you what it saw: a revealed strip of map along its route
and pins for the dungeons, boss altars, traders, villages and ore it spotted.

**Exploring far away, from home.** Other raven and map mods reveal or pin things **around you, where you
stand**. Raven Scout's raven flies **away from your base** to land you haven't reached yet, on a real trip
that takes minutes, while you do something else. Upgrade the roost through the biomes and it reaches the
far edges of the world.

Works in **single player, hosted games and dedicated servers**, and in **no-map worlds**.

## Features

- **Raven Roost**: a box with a tall post and a sitting raven. Name it (**N**), pick a compass direction
  and a distance, feed the raven, and click **Send raven**.
- **Real trips**: the raven flies out and back in in-game time, and keeps flying while you're away.
- **Live tracking**: follow a feather on your map and minimap. The map is revealed behind it as it flies,
  and pins appear as it passes each find ("Your raven spotted: Troll Cave").
- **What it spots**, anywhere in the world, even land nobody has visited:
  - **Dungeons**: Burial Chambers, Troll Caves, Sunken Crypts, Frost Caves, Infested Mines, Charred
    Fortress, Hildir's dungeons
  - **Boss altars**: Eikthyr to Fader
  - **Traders**: Haldor, Hildir, the Bog Witch
  - **Other places**: Fuling villages, Drake nests, Tar pits, Dvergr towers and lighthouses, giant
    remains, Morgen holes, charred ruins and towers, Volture nests, hot springs and more
  - **Ore** (copper, tin, silver, obsidian, iron scrap, flametal...) and, if you turn it on, **wild food**
    (berries, mushrooms, thistle, wild seeds), in land someone has already been near
- **Food**: better food flies **farther and faster**: 1 + the food's health ÷ 50 (cooked meat x1.6,
  end-game food x3+). A hungry raven flies short and slow.
- **Roost tiers**: upgrade in place with each biome's materials.

  | Tier | Upgrade cost | Range | Speed | Also |
  |---|---|---|---|---|
  | Wooden (Meadows) | build cost | x1.0 | x1.0 | spots places |
  | Bronze (Black Forest) | 5 Bronze, 10 Bronze Nails | x1.25 | x1.1 | spots ore |
  | Iron (Swamp) | 10 Iron, 5 Guck | x1.5 | x1.2 | spots wild food, sees wider |
  | Silver (Mountains) | 10 Silver, 4 Wolf Pelt | x1.75 | x1.35 | |
  | Black Metal (Plains) | 10 Black Metal, 10 Linen Thread | x2.0 | x1.5 | |
  | Eitr (Mistlands) | 2 Black Core, 10 Eitr, 10 Carapace | x2.5 | x1.75 | sees wider |
  | Flametal (Ashlands) | 10 Flametal, 3 Celestial Feathers | x3.0 | x2.0 | |
  | Deep North | 30 Flametal, 2 Molten Core | x4.0 | x2.5 | |

  Tier and food multiply: a Black Metal roost with good food reaches 7.5 km.
- **Scout Reports**: a parchment the raven brings back to the roost, written from the roost's point of
  view: *"Troll Cave on a hill in the Black Forest, about 600 m north-east. The Swamp begins about 1.5 km
  north-east."* Reports don't stack, so they make an exploration journal; store them or give them away.
  **Copy** the latest one at the roost for a friend (1 Feather).
- **Feather guide**: use a report and the raven's feather points **ahead, left, right or behind you**
  toward the nearest find on it. Just a hint, never a marker or compass; three uses per report.
- **No-map worlds**: detected automatically. The map is never touched; the raven brings a Scout Report
  instead. Or choose map, report or both in the config.

## How to use

1. Hammer → **Misc** → **Raven Roost** (needs a workbench): 10 Wood, 4 Feathers, 4 Fine Wood.
2. Feed the raven: look at the roost and press a food's hotbar key, or click **Feed** in the window.
3. Open the roost (**E**). On the **Scout** tab, pick a direction (compass buttons, ±15°) and a distance,
   then **Send raven**.
4. Watch the feather on your map, or go do something else. When it's back you get a message, the map
   and pins are updated, and the window shows the trip's report.
5. On the **Roost** tab, upgrade the roost when you have the materials.

Trip time: 15 seconds per 100 m there and back at x1.0, so 1 km out takes about 3 minutes with cooked
meat. It uses in-game time, so sleeping speeds it up.

## Multiplayer

- **Everyone needs the mod**, the server too, with the same version (Jötunn checks this when you join).
- The server works out what each raven saw, so all players get the same findings.
- `ShareFindings`: **Private** (default, only the sender gets the map reveal and pins) or **Everyone**
  (all players; those offline catch up on the latest trips when they join).
- Gameplay settings come from the server; keybinds stay personal.

## Configuration

`BepInEx/config/com.ravenscout.valheim.cfg`, or in game with the Configuration Manager (F1):
range, trip speed, trips per day, strip width, what is spotted (every name list), pin grouping for ore
and food, food rules, every tier's cost and numbers, map or report (`NoMapMode`), report precision
(`Vague`, `Rough`, `Exact`), sharing, copies and the feather guide.

## Good to know

- **Ore and wild food** only exist in land someone has already been near: Valheim creates them when a
  player first comes close. Over brand-new land the raven still maps the land and spots places, and its
  report says the rest couldn't be seen yet.
- No danger: ravens always come back.
- Made for Valheim 1.0.20 with BepInExPack 5.4.2351 and Jötunn 2.30.2.

## Credits

Made by **Faasnu**. MIT license.
