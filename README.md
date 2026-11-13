<!-- README schema v2 -->
# 🪨 Cairn

![CAIRN](https://img.shields.io/badge/CAIRN-STACK_STONES_AND_THEY_BURN-c9a227?style=for-the-badge&labelColor=1b1b1b)
![VERSION](https://img.shields.io/badge/VERSION-0.12.0-8b1e1e?style=for-the-badge&labelColor=1b1b1b)
![CLIENT & SERVER](https://img.shields.io/badge/CLIENT_%26_SERVER-SAME_VERSION_ON_BOTH-1f4e79?style=for-the-badge&labelColor=1b1b1b)

> *Odin's ravens see the whole of Midgard and tell no one where anything is. So the first settlers piled stones on the headlands and set them alight, and a crew lost in the dusk could walk home by the fires their friends had left.*

### Stack four stones on a headland, and they burn.

On a world with no map, knowing where you are is knowledge, and it lives in players' heads and in screenshots pasted into Discord. Cairn moves it into the world, where it can be built, found, lit and lost.

It adds no pieces, no assets and nothing to your screen. It changes one vanilla piece: the hoe's stone, so stones stack.

---

![NEW IN 0.12.0](https://img.shields.io/badge/NEW_IN_0.12.0-PICK_YOUR_COLOURS-8b1e1e?style=for-the-badge&labelColor=1b1b1b)

**Pick the colour of the lights in game, with sliders, and give each cairn its own colour from its sign.**

| You | It |
|---|---|
| **Press F1 in game** (needs the BepInEx Configuration Manager mod) | `BeaconColour` and `TombstoneColour` show a colour swatch with red, green, blue and alpha sliders, and the lights change as you drag |
| **Write a colour on a cairn's sign** | Write the sign as `<color=#4A9EFF>Home</color>` and that cairn burns blue for everyone. The sign shows the word in blue too |
| **Prefer your own colour everywhere?** | Set `UseSignColours = false` and every cairn uses your `BeaconColour`, whatever its sign says |
| **Keep your old colours** | A colour you already set, like `4A9EFF`, carries over as it is |
| **Only players' games need it** | Colours are worked out on each player's own game, so the server does not need this version for them |

---

![BEFORE YOU INSTALL](https://img.shields.io/badge/BEFORE_YOU_INSTALL-READ_THIS-b8860b?style=for-the-badge&labelColor=1b1b1b)

| | If… | Then… |
|---|---|---|
| 🤝 | you play on a **server** | install Cairn on the server **and** on every player, the same version. The server finds the cairns and keeps the list; each player's game draws the lights. |
| 🪨 | you **stack stones** | every player needs Cairn, and the server too. The game that owns a stack decides whether it stands, and without Cairn stacked stones may fall. |
| 🧹 | you ever **uninstall** | what stays is exactly what it looks like: a pile of rocks with a sign on it. Stones stacked on stones may fall, because vanilla does not let one stone hold up another. |
| 🐦 | you want **Hugin to speak** | turn **tutorials on** in the game settings. With tutorials off, the raven never lands. |
| 🗺️ | you also run a **map mod** | they ignore each other, though running both does rather blunt the point. |

---

![I](https://img.shields.io/badge/I-BUILD_A_CAIRN-c9a227?style=for-the-badge&labelColor=1b1b1b)

**Four stones and a minute's work.**

| You | It |
|---|---|
| **Place four Stones with the Hoe** | one stone each; piled within four metres of each other they make a cairn |
| **Stack them** | a stone may go on another stone, so a cairn can stand tall rather than lie in a ring |
| **Spread them out** | it stays a scatter: a cairn is narrow and a wall is wide, and that is how Cairn tells them apart |
| **Wait a couple of seconds** | a fire appears on the crown |
| **Write on a sign within six metres** | the cairn takes that name. The sign can stand on the ground or hang on the stones themselves |

<details>
<summary>📖 <b>Read the saga</b> — what counts as a cairn</summary>

**A cairn is recognised, never provided.** Cairn adds no piece of its own. Every few seconds the server looks for stacked stones, and for written signs beside them. When a player places a stone or writes a sign, their game tells the server where, and the server looks at just that spot a second later, so the fire comes on almost at once. The hoe's single stone and the hammer's stone pile both count.

**Narrow, not wide.** Stones that stand closer than 2.5 m join into one cairn, so three columns side by side are one cairn with one light, and a cairn may hold up to 100 stones. The footprint rule does the real work: a cluster wider than 4 m is a building, not a waymark, so a stone house can never become a landmark.

**Only stones go on stones.** A wall or beam aimed at a stone still shows red, so a single pebble can never become a foundation. This is the one change Cairn makes to a vanilla piece. `StackableStones = false` turns it off.

**Every one looks different,** because you built it.

</details>

---

![II](https://img.shields.io/badge/II-THE_LIGHT-8b1e1e?style=for-the-badge&labelColor=1b1b1b)

**A beacon you navigate by, not a marker that tells you where to go.**

| | |
|---|---|
| 🔥 **Seen from far off** | a lit cairn can be seen from hundreds of metres away, 800 m by default |
| 📏 **Holds its size at distance** | a fire on a far headland reads as a point of light the way a real one does, not as something that shrinks until it is gone |
| ⛰️ **Hidden by terrain** | a ridge between you and a cairn puts it out, so you have to move to see it |
| 🎨 **Your colour** | Pick `BeaconColour` with sliders in the F1 Configuration Manager, or type hex: `4A9EFF` for cold blue, `7CFF7C` for something stranger; the default is firelight |
| 🧱 **No limit on building** | build as many cairns as you like; the server always sends each player the lit cairns nearest them |

<details>
<summary>📖 <b>Read the saga</b> — why the hills hide it</summary>

**That is the difference between navigating and being told.** A glow that shines through a mountain is a waypoint marker in a costume. A light you lose behind a ridge and find again from the next rise is a beacon. Leave `BeaconOcclusion` on.

**The light is drawn on each player's game,** so its colour, size and reach are yours to set without changing anyone else's. The server only says which cairns are lit.

</details>

---

![III](https://img.shields.io/badge/III-THE_RAVEN-5b2c83?style=for-the-badge&labelColor=1b1b1b)

**Stand at a named cairn and Hugin may land and say its name.**

| | |
|---|---|
| 🐦 **He speaks the name** | stand within about twelve metres of a named cairn and the raven may land there and say it |
| 📜 **Names are optional** | a cairn with no sign lights exactly the same |
| 🖥️ **The console knows them too** | `cairn landmarks` lists every named cairn in single player, and the named cairns near you, nearest first, on a server |

<details>
<summary>📖 <b>Read the saga</b> — why the raven is fussy</summary>

**He is a grace note, not a compass.** Vanilla will not land the bird below 30 metres of altitude, with anything hostile within ten metres, during a world event, or while tutorials are turned off in the game settings. So he is silent exactly when you would most want him, which is why nothing in the mod depends on him.

**`cairn raven` shows what keeps him away:** the tutorials setting, your altitude, and how far the nearest named cairn is.

**To play without names,** turn off `EnableRavenVoice` or leave your signs blank.

</details>

---

![IV](https://img.shields.io/badge/IV-YOUR_TOMBSTONE-1f4e79?style=for-the-badge&labelColor=1b1b1b)

**With no map there is no death pin. A light can bring you back to your body.** Optional, off by default.

| | |
|---|---|
| 🔴 **A red light over your tombstone** | turn on `EnableTombstoneLights`, and after you die a red light burns over your tombstone |
| 👤 **Only yours** | only you see the light over your own tombstone; other players see theirs, not yours |
| ⛰️ **Hills still hide it** | like a cairn's light: you walk toward a glow you can see, not a marker |
| 🕯️ **It goes out** | once you empty the tombstone |

<details>
<summary>📖 <b>Read the saga</b> — who sets what</summary>

**Set it on the server.** In single player, set it in your own game. Every player and the server need this version of Cairn for the light to work.

**Each player keeps a say.** `ShowTombstoneLight = false` hides the light in your own game, and `TombstoneColour` changes its colour (red by default, so it can never be mistaken for a cairn). `cairn status` says whether tombstone lights are on, and how many of the tombstones it knows about are yours.

</details>

---

![V](https://img.shields.io/badge/V-NO_MAP%2C_NO_MARKERS-b8860b?style=for-the-badge&labelColor=1b1b1b)

**If you learn where you are, you learn it by looking at the world.**

| | |
|---|---|
| 🚫 **Nothing on your screen** | no map, no compass, no waypoints, no markers, no pins, no arrows, no HUD, no readout of any kind |
| 📦 **Nothing added** | no prefabs, no assets, and vanilla recipes left alone |
| 🪨 **Nothing left behind** | uninstall it and a cairn is a pile of rocks with a sign on it |

---

![THE TOOLBOX](https://img.shields.io/badge/THE_TOOLBOX-QUICK_START_%C2%B7_COMMANDS_%C2%B7_HOW--TO-1b1b1b?style=for-the-badge&labelColor=8b1e1e)

**First ten minutes:** install on the server and every player → climb a headland → place four **Stones** with the **Hoe**, stacked or close together → wait a couple of seconds for the fire → put up a sign within six metres and write a name on it → with tutorials on, stand by it and listen for Hugin.

Cairn adds no keys. Press `` ` `` to open the console; every command is `cairn <word>`, and each reports **state rather than verdicts**.

| Command | |
|---|---|
| `cairn status` | what Cairn is doing on this machine, how many cairns are lit and sent, and the tombstone lights |
| `cairn landmarks` | the named cairns: every one in single player, the ones near you on a server |
| `cairn beacons` | each light, and whether it is out of range or hidden behind a hill |
| `cairn raven` | the tutorials setting, your altitude, and how far the nearest named cairn is |
| `cairn prefabs <text>` | the game's prefabs whose names contain the text |
| `cairn pieces <text>` | the pieces whose names contain the text, and whether something can be placed on each |
| `cairn save` | write the list of cairns now (on the server or the host) |

<details>
<summary>🔴 <b>Turn on tombstone lights</b></summary>

On a server, set `EnableTombstoneLights = true` in the server's `BepInEx/config/com.raveniron.cairn.cfg` (section `[6 - Tombstones]`). In single player, set it in your own game's file. Every player needs this version of Cairn. A player who would rather not see it sets `ShowTombstoneLight = false` in their own file.

</details>

<details>
<summary>🎨 <b>Change the colour of the lights</b></summary>

**In game:** with the BepInEx Configuration Manager mod installed, press F1, find Cairn, and drag the red, green, blue and alpha sliders for `BeaconColour` (cairns) or `TombstoneColour` (your tombstone). The lights change as you drag. Alpha sets how strong the light is; all the way down makes it invisible.

**In the file:** in your own `com.raveniron.cairn.cfg`, set `BeaconColour` under `[4 - The light]` to a hex colour such as `4A9EFF`. Cairn writes it back as `RRGGBBAA` (`4A9EFFFF`); the last two digits are the alpha. A value that will not read is left at the default, and BepInEx says so in the log. `TombstoneColour`, under `[6 - Tombstones]`, works the same way.

**On a cairn's sign:** write the sign's text inside a colour tag, for example `<color=#4A9EFF>Home</color>` or `<color=red>Red Cliff</color>`. That cairn's light takes the colour, for every player who has `UseSignColours` on (the default), and the sign shows the name in the same colour. Hex (`#RGB`, `#RRGGBB`, `#RRGGBBAA`) always works; the names the sign understands are red, lightblue, blue, grey, black, green, white, orange, purple and yellow. If a sign has more than one colour tag, the first one counts. Set `UseSignColours = false` to light every cairn in your own `BeaconColour` instead.

</details>

<details>
<summary>📒 <b>Where the cairns are kept</b></summary>

Landmarks are stored per world, beside the save, in a plain text file you can read. On a server the list lives on the server.

</details>

---

![MULTIPLAYER](https://img.shields.io/badge/MULTIPLAYER-ONE_DLL%2C_EVERY_ROLE-1f4e79?style=for-the-badge&labelColor=1b1b1b)

| Works for a player without Cairn | Needs Cairn on the server | Needs Cairn on both ends |
|---|---|---|
| a cairn is still a real pile of stones they can see and touch, but they see no light and hear no name | finding cairns and their names · the list of cairns, saved with the world · which lit cairns each player is sent · switching tombstone lights on | drawing the lights · Hugin saying a name · stacking stones · the tombstone light |

**One DLL does every role.** A dedicated server sweeps the world and keeps the list, a player's game draws the lights and carries the raven's line, and a host does both. Keep the server and every player on the same version.

---

![COMPATIBILITY](https://img.shields.io/badge/COMPATIBILITY-PLAYS_WELL_WITH_OTHERS-2e5e3a?style=for-the-badge&labelColor=1b1b1b)

**Nothing is patched that another mod is likely to want, and no prefab is added, so no save is at risk.**

| Mod | |
|---|---|
| **Map mods** | Cairn writes nothing to the minimap, so the two simply ignore each other, though running both does rather blunt the point. |
| **Season and weather mods** | Untouched: Cairn only reads the environment and the weather. |

---

![SETTINGS](https://img.shields.io/badge/SETTINGS-BEPINEX_CONFIG-b8860b?style=for-the-badge&labelColor=1b1b1b)

All safe to leave alone. They are in `BepInEx/config/com.raveniron.cairn.cfg`, and every value carries a note explaining what it costs. Cairn does not send its settings from the server: each machine reads its own file, so the tables say whose copy counts. The file updates itself between versions: a value still at an old default moves to the new one, and a value you chose is kept.

<details>
<summary>🔒 <b>On the server</b> — the server's copy decides (in single player, your own)</summary>

| Section | Setting | What it does |
|---|---|---|
| 1 - Core | `Enabled` | master switch; off on the server, nothing is found, kept or sent (each player's own copy switches off their lights and the raven) |
| 1 - Core | `TickBudgetMs` · `AutosaveIntervalSeconds` | how much time per frame the server may spend, and how often the list of cairns is saved |
| 2 - Landmarks | `EnableLandmarks` | **leave this on**: it is how the mod finds cairns; off, new cairns never light. It is not the setting for names |
| 2 - Landmarks | `QuickChecks` | a placed stone or written sign lights its cairn in a couple of seconds; off, new cairns wait for the full search (on) |
| 2 - Landmarks | `LandmarkRotationSeconds` | how often the whole world is searched for cairns, which catches anything a quick check missed (20 s) |
| 2 - Landmarks | `SignPrefabs` | which pieces count as signs |
| 3 - Cairn piles | `StonePrefabs` | which stone pieces a cairn can be built from |
| 3 - Cairn piles | `PileMinPieces` · `PileMaxPieces` | how many stones make a cairn (4), and the most one may hold (100); raise the first if decorative stones start lighting up |
| 3 - Cairn piles | `PileLinkMeters` · `PileMaxExtentMeters` | stones this close join one cairn (2.5 m); the footprint that separates a waymark from a building (4 m) |
| 3 - Cairn piles | `LandmarkPairMeters` | how far a sign may stand from the cairn it names (6 m) |
| 3 - Cairn piles | `PileDriftMeters` | how far an unnamed cairn may shift as you add stones and stay the same cairn |
| 3 - Cairn piles | `StackableStones` | stones stack; set it the same on the server and every player, and restart |
| 4 - The light | `EnableBeacons` | off on the server sends no lights to anyone |
| 4 - The light | `BeaconSyncSeconds` · `BeaconMaxCount` | how often lit cairns are sent, and how many each player is sent at once, nearest first (250); not a limit on building |
| 6 - Tombstones | `EnableTombstoneLights` | the red light over your tombstone (off) |

</details>

<details>
<summary>🖥️ <b>In your own game</b> — yours alone</summary>

| Section | Settings |
|---|---|
| **1 - Core** | `VerboseLogging` |
| **3 - Cairn piles** | `StackableStones` (each player's own copy decides their placing) · `StonePileStoneCost` (0 leaves the hammer's stone pile recipe alone; any other value is charged by your own game only) |
| **4 - The light** | `EnableBeacons` · `BeaconColour` · `UseSignColours` · `BeaconMaxDistanceMeters` (800 m) · `BeaconOcclusion` (terrain hides beacons; leave it on) · `BeaconAngularSize` · `BeaconMinSizeMeters` · `BeaconMaxSizeMeters` · `BeaconHeightMeters` |
| **5 - The raven** | `EnableRavenVoice` · `RavenNameMeters` · `RavenNameMaxLength` |
| **6 - Tombstones** | `ShowTombstoneLight` · `TombstoneColour` |

</details>

---

![CREDITS](https://img.shields.io/badge/CREDITS-THANK_YOU-1b1b1b?style=for-the-badge&labelColor=c9a227)

**Needs:** [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)

- **Design document**: the reasoning behind every decision, including the ones that were wrong: <https://claude.ai/code/artifact/a04abbae-14d5-4a21-9bdc-032e91da0936>
- **Support Raven Iron.** Every Raven Iron mod is free, and stays free: all of it, always. Nothing is held back for patrons, and nothing ever will be. To help cover server hosting and test hardware: the [website](https://ravenirongames.com/) · [Patreon](https://www.patreon.com/cw/RavenIronGames) · [Discord](https://discord.gg/AGKDEurAVa), a channel per mod and where the testing happens.

*A Valheim mod by [Raven Iron](https://ravenirongames.com/).*
