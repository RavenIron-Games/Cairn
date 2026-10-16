# Changelog

## 0.10.0

**Stones stack.** Vanilla never let a stone go on another stone. The hoe's single
`Placeable_Stone` shows red when aimed at one, so a cairn could only be a ring on the ground.
Now stones stack, and a stack stays standing.

- **Only stones go on stones.** A wall, beam or anything else aimed at a stone still shows red, so
  a single pebble can never become a foundation.
- **Everyone needs this version, the server included.** The machine that owns a stack decides
  whether it stands. On an older Cairn, or with Cairn removed, stacked stones may fall.
- **It is a setting.** `StackableStones` in `[3 - Cairn piles]` is on by default. Set it to
  `false` to leave the vanilla stone exactly as it was. It is read on each player's game and on
  the server when the world loads, so change it everywhere and restart.
- **Tall cairns light.** A cairn may now hold up to 100 stones, up from 12. Stones that
  stand closer than 2.5 m join into one cairn, so three columns side by side are one cairn with
  one light. Under the old limit they got no light at all. A setting still at the old 12 moves
  to 100 on first start, and a number you chose yourself is kept. The 4 m footprint rule
  still tells a cairn from a stone building.
- `cairn pieces` now reports, for every piece, whether something can be placed on it and why
  not.

Includes every 0.9.2 change below.

## 0.9.2

One fix, on the server. Players' games do not need updating — a 0.9.1 game reads a 0.9.2
server exactly as before — but the server does.

- **New cairns light again on a busy world.** A server sends players at most
  `BeaconMaxCount` lit cairns, and once a world had more than that (100, by default), it
  always sent the OLDEST ones. Every cairn built after that point stayed dark for joined
  players while the old ones kept burning — exactly the cairns you are standing beside and
  waiting on. Past the limit, the server now sends the cairns nearest a player instead, so
  anything near you lights and the ones dropped are those too far away to see anyway.
  Singleplayer and the host's own game were never affected; they draw every cairn.
- **The limit is no longer silent.** The server log warns once per session when a world has
  more lit cairns than it sends, and `cairn status` on the server shows how many are lit,
  how many are sent, and the limit.
- **`BeaconMaxCount` defaults to 250**, up from 100 — and this reaches existing servers too.
  Cairn now updates its own config file: on the first start of 0.9.2, a `BeaconMaxCount`
  still at the old default of 100 moves to 250, while any number you chose yourself is kept.
  The log says which happened, and the file as it was is saved beside it
  (`com.raveniron.cairn.cfg.v0.bak`). A new `[0 - Meta] ConfigVersion` line at the top of the
  file records that this has been done; leave it alone. 500 is the most players' games accept.

## 0.9.1 — 2026-09-25

Updated due to 1.0.16 Patch.

## 0.9.0

Bug fixes. No new feature, no config key added or removed, the same ledger file format, and
the same Valheim requirement as 0.8.0. Install it on the server and on every player's game, as
before.

- **Leaving a world now properly leaves it.** Until now nothing was reset when you logged
  out, so the first world you played carried into the next one until the game was restarted:
  - Play single-player, then join a server: you saw your single-player world's lights at
    those coordinates, never the server's real beacons, and the raven spoke single-player
    names.
  - Host one world, then another: the second world's ledger was not loaded, and the first
    world's cairns could be written into the second world's ledger file, replacing its
    history.
  - The ledger is now saved when a world ends, and everything is cleared before the next
    one starts.
- **Only the server can send beacons.** A modified client could previously push fake
  beacons, or any text for the raven, to every player on a server. The server now drops such
  pushes, so this needs the server updated to 0.9.0. Players' games also ignore pushes that
  do not claim to come from the server, as an extra check.
- **Two cairns beside one sign both light.** When two cairns stood within reach of the same
  named sign, they merged into one landmark: only one of them ever lit, and the ledger was
  rewritten every minute. Each sign now names at most one cairn, the closest pairs first. The
  other stays lit, either unnamed or named by another sign within reach, and on an existing
  world it shows up once as a new landmark.
- **A small memory leak is fixed:** each beacon that went out (a cairn taken down, or beacons
  switched off) left a little memory behind until the game was closed.
- **`cairn raven` now says when tutorials are turned off** in the game settings. The raven
  does not land to say a name while they are off.
- **`StonePileStoneCost` is described correctly.** It only affects the game of the player
  who sets it; setting it on a dedicated server changes nothing for players. The behaviour is
  unchanged, only the description.
- **The DLL no longer carries the build machine's folder path.** Every release through 0.8.0
  embedded an absolute build path, which included the build machine's user name, in the
  DLL's debug information. The build now records a neutral placeholder path instead.
- **The build no longer depends on the folder it is built in.** Two builds of the same
  commit, from Windows clones with Git's default line endings and the same .NET SDK, produce
  the same bytes. This DLL was built from the commit tagged `v0.9.0`; the GitHub release
  names that commit and gives the DLL's md5.
- **The README has a Support Raven Iron section:** the website, the Patreon and a permanent
  Discord invite. Every Raven Iron mod is free and stays free; nothing is held back for
  patrons.
- **The store page's website link now goes to the Raven Iron website** instead of the
  GitHub repo.

**Tested in game on 2026-09-24**, on the same code as this release (commit `802c368`; only
documents and the store page's website link changed after it): a Valheim 1.0.15 dedicated
server (crossplay) plus three single-player/hosted worlds in turn, with one client throughout.
Leaving a world and coming back worked as designed — a second local world loaded with none of
the first world's landmarks, returning to the first re-loaded all of them, and joining the
dedicated server as a client showed no local landmarks and `ledger : not on this process`. The
ledger was saved when the server stopped mid-run: the landmark's last-seen time was 12 s before
the stop and its first-seen time was unchanged. Renaming a sign worked, and toggling beacons
off and on worked on screen. Building two cairns beside one sign: both lit, only one took the
sign's name, and the rotation settled at zero changed — but the sign stood about 3.1 m from one
pile and 3.3 m from the other, so which cairn is nearer was not clearly demonstrated, and the
sweep's stone count also dropped once (86 to 82) for a reason not in the logs. With tutorials
off in the game settings, `cairn raven` reported them off. The raven then kept away, and
landed and spoke a name once tutorials were back on; that part was seen on screen only, not
in the logs.
The second `cairn raven` (which should read `tutorials : on`) was not typed in this run; that
reading was seen changing from OFF to on in an earlier session the same day, on the same DLL.
Cairn logged no error or warning on either side, and none of the lines that would mean a beacon
push was dropped or ignored, a receive failed, or a sweep tick threw appeared.

Not tried in game: mixed versions (0.8.x on the server and 0.9.0 on players' games, or the
reverse), since both sides ran 0.9.0; a second named sign within 6 m of the cairn that lost
the pairing to the other sign; a beacon push spoofed from a hostile client (the guard's only
evidence is that normal pushes were never dropped); the memory-leak fix itself (the beacon
toggle was checked on screen only); a targeted test of the check that skips a sweep while the
game shuts down; the landmark list and raven commands on the dedicated-server client session,
which were not typed there; and a clean exit at the final server stop, whose log ends before
any mod's stop line (the ledger file was written at that stop). Off-game: 206/206.

## 0.8.0

- **Valheim 1.0.7 support. This release REQUIRES it, and does not run on 0.2x.** The 1.0
  release deleted `World.GetWorldSavePath`, which is how the landmark ledger found its file —
  a clean compile and a mod that could not save. It now resolves through
  `SaveSystem.GetWorldsSaveRootPath`, the same method rehoused, so your existing
  `cairn_landmarks_*.dat` is found exactly where it was. **Nothing to migrate.**
- The singleton reader now also recognises `s_instance`, the name Valheim 1.0.7 gave
  `ZoneSystem`'s backing field. It already coped by falling through to the public property;
  this makes that deliberate rather than lucky.
- No gameplay or config change. Same sweeps, same beacons, same defaults.

## 0.7.0

First release. Built and verified in one day on a dedicated server, against cairns stacked
by hand rather than fixtures.

**Stack stones and they burn.** A tight pile of ordinary stone becomes a cairn; a cairn
carries a light you can steer by; a sign within six metres gives the place a name. The world
remembers where they are.

- **No new pieces.** A cairn is *detected*, never provided — four `Placeable_Stone` (Hoe, one
  stone each) inside a four-metre footprint. Everyone's looks different, and with the mod
  uninstalled it degrades to exactly what it appears to be.
- **No vanilla recipe touched** by default.
- **No HUD, no map, no markers.** Everything a player sees is an object standing in the world.
- **The light is occluded by terrain.** A ridge between you and a cairn puts it out — that is
  the difference between navigating and being told, and it is verified rather than asserted.
- **Constant angular size**, so a beacon reads as a point of light at 50m or 500m instead of
  shrinking away. Seen down a chain of fifteen spanning 420m.
- **Colour configurable** as hex.
- **Hugin — or Munin — speaks a landmark's name** when you stand at it. Deliberately flavour:
  vanilla will not land the bird below 30m of altitude, near a hostile, or during a world
  event, so it is silent exactly when you would most want it.
- **Server-authoritative.** One role-aware DLL: a headless server sweeps and owns the ledger,
  a client draws and speaks, a listen host does both.

Console: `cairn status | landmarks | beacons | raven | prefabs <text> | pieces <text> | save`.

Every command reports state rather than verdicts, and says *why* a thing is not happening —
which is what four wrong theories about a bird taught in a single afternoon.
