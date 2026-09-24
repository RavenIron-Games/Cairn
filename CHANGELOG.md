# Changelog

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
  pushes, and players' games accept beacons only from the server they are connected to.
- **Two cairns beside one sign both light.** When two cairns stood within reach of the same
  named sign, they merged into one landmark: only one of them ever lit, and the ledger was
  rewritten every minute. The sign now names only the nearer cairn, and the other stays lit
  as an unnamed cairn.
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
- **The build is reproducible from the commit.** The same commit now builds to the same
  bytes wherever the repository is checked out. This DLL was built from commit TBD.
- **The README has a Support Raven Iron section:** the website, the Patreon and a permanent
  Discord invite. Every Raven Iron mod is free and stays free; nothing is held back for
  patrons.

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
