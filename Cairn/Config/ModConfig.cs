using BepInEx.Configuration;
using UnityEngine;

namespace RavenIron.Cairn.Config
{
    /// <summary>
    /// Config surface. Expected to grow — beacon height and colour, the raven's voice,
    /// sound range, ledger cadence all belong here eventually.
    ///
    /// Two conventions carried over from Ragnarok's Wrath, both paid for:
    ///
    /// 1. Every system gets its own on/off toggle from day one. That is what makes
    ///    incremental testing possible and lets a server owner adopt part of the mod.
    ///    There are no systems yet, so there are no system toggles yet — a toggle for a
    ///    thing that does not exist is a promise the log cannot keep.
    ///
    /// 2. Clamp on READ as well as on write. Config files get hand-edited, and BepInEx
    ///    persists whatever it clamped, so a value validated only on write comes back
    ///    wrong on the next boot.
    /// </summary>
    public static class ModConfig
    {
        // ---- Meta -----------------------------------------------------------------
        public static ConfigEntry<int> ConfigVersion;

        // ---- Core -----------------------------------------------------------------
        public static ConfigEntry<bool>  Enabled;
        public static ConfigEntry<float> TickBudgetMs;
        public static ConfigEntry<bool>  VerboseLogging;
        public static ConfigEntry<float> AutosaveIntervalSeconds;

        // ---- Landmarks ------------------------------------------------------------
        public static ConfigEntry<bool>   EnableLandmarks;
        public static ConfigEntry<float>  LandmarkRotationSeconds;
        public static ConfigEntry<bool>   QuickChecks;
        public static ConfigEntry<string> SignPrefabs;

        // ---- Cairn piles ----------------------------------------------------------
        public static ConfigEntry<string> StonePrefabs;
        public static ConfigEntry<float>  PileLinkMeters;
        public static ConfigEntry<int>    PileMinPieces;
        public static ConfigEntry<int>    PileMaxPieces;
        public static ConfigEntry<float>  PileMaxExtentMeters;
        public static ConfigEntry<float>  LandmarkPairMeters;
        public static ConfigEntry<float>  PileDriftMeters;
        public static ConfigEntry<int>    StonePileStoneCost;
        public static ConfigEntry<bool>   StackableStones;

        // ---- The light ------------------------------------------------------------
        public static ConfigEntry<bool>  EnableBeacons;
        public static ConfigEntry<float> BeaconSyncSeconds;
        public static ConfigEntry<int>   BeaconMaxCount;
        public static ConfigEntry<float> BeaconMaxDistanceMeters;
        public static ConfigEntry<float> BeaconAngularSize;
        public static ConfigEntry<float> BeaconMinSizeMeters;
        public static ConfigEntry<float> BeaconMaxSizeMeters;
        public static ConfigEntry<float> BeaconHeightMeters;
        public static ConfigEntry<bool>   BeaconOcclusion;
        public static ConfigEntry<Color>  BeaconColour;
        public static ConfigEntry<bool>   UseSignColours;
        public static ConfigEntry<string> HolderPrefabs;
        public static ConfigEntry<string> HeldColours;
        public static ConfigEntry<bool>   StandsHoldListedItems;

        // ---- The raven ------------------------------------------------------------
        public static ConfigEntry<bool>  EnableRavenVoice;
        public static ConfigEntry<float> RavenNameMeters;
        public static ConfigEntry<int>   RavenNameMaxLength;

        // ---- Tombstones -----------------------------------------------------------
        public static ConfigEntry<bool>   EnableTombstoneLights;
        public static ConfigEntry<bool>   ShowTombstoneLight;
        public static ConfigEntry<Color>  TombstoneColour;
        public static ConfigEntry<float>  TombstoneHeightMeters;

        public static void Bind(ConfigFile config)
        {
            // Bound at 0 and stamped by ConfigMigration.Finish. NO AcceptableValueRange, on
            // purpose: BepInEx clamps out-of-range values silently, so a ceiling would one day
            // refuse the stamp and turn the migration into something that re-runs every boot.
            ConfigVersion = config.Bind(
                Core.ConfigLedger.MetaSection, Core.ConfigLedger.VersionKey, 0,
                "Which config layout this file is at. Written by the mod; do not edit. Each " +
                "update that changes a default moves your value only if it was still the old " +
                "default, and keeps anything you set yourself (the log names it either way).");

            Enabled = config.Bind(
                "1 - Core", "Enabled", true,
                "Master switch. False leaves the plugin loaded and the console registered, " +
                "but nothing ticks.");

            TickBudgetMs = config.Bind(
                "1 - Core", "TickBudgetMs", 2.0f,
                new ConfigDescription(
                    "Milliseconds per frame CairnTick may spend across ALL systems. Work that " +
                    "does not fit resumes next frame from where the cursor stopped.",
                    new AcceptableValueRange<float>(0.25f, 10f)));

            VerboseLogging = config.Bind(
                "1 - Core", "VerboseLogging", false,
                "Per-tick detail. Off by default: it is loud enough to hide the lines that matter.");

            AutosaveIntervalSeconds = config.Bind(
                "1 - Core", "AutosaveIntervalSeconds", 60f,
                new ConfigDescription(
                    "How often the landmark ledger is written when it has changed. Time-based " +
                    "rather than change-based on purpose: a busy world would otherwise write on " +
                    "every sweep, and an idle one would never write at all.",
                    new AcceptableValueRange<float>(5f, 600f)));

            EnableLandmarks = config.Bind(
                "2 - Landmarks", "EnableLandmarks", true,
                "LEAVE THIS ON. It is how the mod finds cairns. Every few seconds the server looks " +
                "for stacked stones, and for signs written on beside them. " +
                "If you turn it off, new cairns are never found, so they never light. " +
                "Cairns found before you turned it off keep their light. " +
                "This is NOT the setting for names. To play without names, turn off " +
                "EnableRavenVoice, or leave your signs blank. " +
                "Set it on the server.");

            QuickChecks = config.Bind(
                "2 - Landmarks", "QuickChecks", true,
                "Light a new cairn within a couple of seconds. When a player places a stone or " +
                "writes on a sign, their game tells the server where, and the server checks just " +
                "that spot instead of waiting for the next full search of the world. It costs " +
                "nothing while nobody is building. Off means new cairns wait for the full search " +
                "(LandmarkRotationSeconds), as before 0.12.0. " +
                "Set it on the server; players need 0.12.0 or newer to send the notice.");

            LandmarkRotationSeconds = config.Bind(
                "2 - Landmarks", "LandmarkRotationSeconds", 20f,
                new ConfigDescription(
                    "Seconds for a FULL rotation over every sign and stone prefab — which is " +
                    "how long a newly built cairn can take to be noticed. Each prefab is swept " +
                    "one at a time at rotation/count, so adding a prefab makes each step " +
                    "shorter rather than the wait longer. It used to govern each PREFAB, which " +
                    "made four prefabs a three-minute wait; latency should not be a tax on the " +
                    "length of a config list. TRADE-OFF: a shorter rotation touches the ZDO " +
                    "index more often, which is the thing AwayFromHome also scans every 60s. " +
                    "Raise it on a heavily modded server if sweeps ever show up in a profile.",
                    new AcceptableValueRange<float>(4f, 600f)));

            SignPrefabs = config.Bind(
                "2 - Landmarks", "SignPrefabs", "sign,sign_notext",
                "Comma-separated prefab names treated as signs. CONFIG rather than code because " +
                "these are data about the game's content: they drift with game patches and modded " +
                "pieces, and a wrong name costs a silent zero matches. The first sweep of each " +
                "session logs its per-prefab counts so a wrong name is visible in the log. " +
                "Both defaults confirmed in-game 2026-09-02 by `cairn prefabs sign`: exactly " +
                "two of 3458 prefabs contain \"sign\", and these are they.");

            StonePrefabs = config.Bind(
                "3 - Cairn piles", "StonePrefabs", "Placeable_Stone,stone_pile",
                "Comma-separated stone pieces a cairn can be built from. Both confirmed BUILDABLE " +
                "in-game 2026-09-02 by `cairn pieces stone`: Placeable_Stone is [Hoe] Stone x1 — " +
                "a single stone you stack, which is what a cairn actually is — and stone_pile is " +
                "[Hammer] Stone x50, one pre-made heap. Architecture is deliberately absent: " +
                "walls and floors would only feed the footprint rule things it is going to " +
                "reject, and leaving them out means a stone HOUSE can never become a landmark.");

            PileLinkMeters = config.Bind(
                "3 - Cairn piles", "PileLinkMeters", 2.5f,
                new ConfigDescription(
                    "Stones within this distance of each other belong to the same pile. Linking " +
                    "is transitive, so a chain of stones forms ONE cluster — which is what lets " +
                    "a long wall be measured, and rejected, as a whole.",
                    new AcceptableValueRange<float>(0.5f, 10f)));

            PileMinPieces = config.Bind(
                "3 - Cairn piles", "PileMinPieces", 4,
                new ConfigDescription(
                    "Fewer stones than this is a dropped rock, not a cairn. At 1 stone per " +
                    "Placeable_Stone the price signals nothing, so DELIBERATENESS is carried by " +
                    "this count and the footprint rule instead — which is the job they were " +
                    "written for. Raise it if scattered decorative stones start lighting up.",
                    new AcceptableValueRange<int>(2, 20)));

            PileMaxPieces = config.Bind(
                "3 - Cairn piles", "PileMaxPieces", 100,
                new ConfigDescription(
                    "More than this is a structure. A backstop only — the footprint rule does the " +
                    "real work, because piece count alone can never tell a tidy wall from a waymark. " +
                    "Was 12 until 0.10.0, when stones began to stack: one tall column passes 12 on its " +
                    "own, and stacks standing closer than PileLinkMeters join into one cairn.",
                    new AcceptableValueRange<int>(3, 100)));

            PileMaxExtentMeters = config.Bind(
                "3 - Cairn piles", "PileMaxExtentMeters", 4f,
                new ConfigDescription(
                    "THE rule. A cairn is narrow; a building is wide. Widest horizontal span a " +
                    "cluster may cover before it is judged architecture rather than a waymark.",
                    new AcceptableValueRange<float>(1f, 20f)));

            LandmarkPairMeters = config.Bind(
                "3 - Cairn piles", "LandmarkPairMeters", 6f,
                new ConfigDescription(
                    "How close a sign must be to a cairn to give it a name, in metres. " +
                    "Write on a sign this close to a cairn and the cairn takes that name. " +
                    "You see the name in two places only. First, Hugin the raven may land at " +
                    "the cairn and say it, but only while tutorials are turned on in the game " +
                    "settings. Second, the console command `cairn landmarks` lists names: every one " +
                    "in single player, and the ones near you when you play on a server. " +
                    "A name does not change the light. A cairn with no sign lights the same. " +
                    "The distance is measured along the ground and ignores height, so a sign " +
                    "low on the side of a tall cairn still counts.",
                    new AcceptableValueRange<float>(1f, 30f)));

            PileDriftMeters = config.Bind(
                "3 - Cairn piles", "PileDriftMeters", 4f,
                new ConfigDescription(
                    "How far an UNNAMED cairn's crown may move between sweeps and still be the " +
                    "same cairn. A crown is a computed centroid, so adding a stone shifts it — " +
                    "and on 2026-09-02 a live cairn drifted 0.8m, crossed a metre boundary, and " +
                    "was pruned and re-founded with its history erased. Within this distance the " +
                    "old FirstSeen follows the cairn to its new key instead. Defaults to the " +
                    "footprint limit: a cairn that moved less than its own width has not become " +
                    "a different place. Named cairns never need this — they are keyed on a sign, " +
                    "and signs do not move.",
                    new AcceptableValueRange<float>(0f, 20f)));

            StonePileStoneCost = config.Bind(
                "3 - Cairn piles", "StonePileStoneCost", 0,
                new ConfigDescription(
                    "Stone required to build one vanilla 'stone_pile'. 0 means DO NOT TOUCH THE " +
                    "GAME, and that is now the default. Any other value edits a vanilla recipe — " +
                    "the only thing in this mod that reaches outside its own scope — and " +
                    "announces itself in the log with the before and the after, because a mod " +
                    "that silently rewrites a recipe is exactly the surprise this studio " +
                    "dislikes in others. It briefly shipped at 10, when stone_pile at 50 stone " +
                    "looked like the only way to make cairns affordable. Then `cairn pieces " +
                    "stone` turned up Placeable_Stone at ONE stone, the problem the override " +
                    "existed to solve stopped existing, and the right move was to hand vanilla " +
                    "back. Kept as a switch for anyone who prefers heaps to stacks. " +
                    "PER PLAYER: the cost is charged by the building player's own game, so this " +
                    "applies to whoever sets it in their own config. It is not sent from the " +
                    "server, and setting it on a dedicated server changes nothing for players.",
                    new AcceptableValueRange<int>(0, 100)));

            StackableStones = config.Bind(
                "3 - Cairn piles", "StackableStones", true,
                "Lets a Placeable_Stone (Hoe, 1 stone) be placed ON another one, so a cairn can be " +
                "a real stack instead of a ring of pebbles on the ground. Vanilla refuses it: the " +
                "stone is made non-supporting, which blocks anything being placed on it. With this " +
                "on, the stone supports what is stacked on it, and ONLY another stone may be placed " +
                "on one — no hut on a one-stone foundation. This changes a VANILLA piece, and says " +
                "so in the log at start-up; false leaves it exactly as the game made it. " +
                "EVERY PLAYER'S GAME: placement is checked by the player who places, so each " +
                "player's own setting is what counts for them. Takes effect when a world loads.");

            EnableBeacons = config.Bind(
                "4 - The light", "EnableBeacons", true,
                "Draw a light on top of every cairn. Client-side and purely visual: turning it " +
                "off changes nothing the server knows, and the ledger keeps recording places.");

            BeaconSyncSeconds = config.Bind(
                "4 - The light", "BeaconSyncSeconds", 5f,
                new ConfigDescription(
                    "How often the server broadcasts every lit cairn to everyone. Absolute " +
                    "snapshots, sent whether or not anything changed: a delta scheme drifts " +
                    "forever on one dropped packet, and this heals itself within one cadence.",
                    new AcceptableValueRange<float>(5f, 120f)));

            BeaconMaxCount = config.Bind(
                "4 - The light", "BeaconMaxCount", 250,
                new ConfigDescription(
                    "This does NOT limit how many cairns you can build. Build as many as you like. " +
                    "It is the most lit cairns the server sends to players at one time. " +
                    "The server always sends the cairns nearest to a player first. " +
                    "By default a light can be seen from up to 800 m away (BeaconMaxDistanceMeters), so the " +
                    "default of 250 is plenty for any run. You do not need to raise it. " +
                    "If more cairns stand near players than this number, the farthest ones stay " +
                    "dark until someone walks closer. The server log warns you once if that " +
                    "happens, and `cairn status` shows how many are lit and how many are sent. " +
                    "The highest allowed value is 500. " +
                    "Set it on the server. In single player, and for the player hosting a game, every " +
                    "cairn lights whatever this says.",
                    new AcceptableValueRange<int>(1, 500)));

            BeaconMaxDistanceMeters = config.Bind(
                "4 - The light", "BeaconMaxDistanceMeters", 800f,
                new ConfigDescription(
                    "How far a beacon can be seen before it fades out entirely.",
                    new AcceptableValueRange<float>(50f, 4000f)));

            BeaconAngularSize = config.Bind(
                "4 - The light", "BeaconAngularSize", 0.03f,
                new ConfigDescription(
                    "THE reason a beacon is visible at range. The glow is scaled BY DISTANCE, " +
                    "so it holds roughly constant size on screen instead of shrinking away — " +
                    "0.03 means a 12m glow at 400m, about 1.7 degrees, which is a clear point " +
                    "of light rather than a smudge. A fixed-size glow would be the wrong shape " +
                    "of thing entirely.",
                    new AcceptableValueRange<float>(0.005f, 0.2f)));

            BeaconMinSizeMeters = config.Bind(
                "4 - The light", "BeaconMinSizeMeters", 1.5f,
                new ConfigDescription(
                    "Floor on the glow's world size, so standing next to a cairn does not put " +
                    "a speck on the stones.",
                    new AcceptableValueRange<float>(0.2f, 20f)));

            BeaconMaxSizeMeters = config.Bind(
                "4 - The light", "BeaconMaxSizeMeters", 14f,
                new ConfigDescription(
                    "Ceiling on the glow's world size, so a distant beacon stays a point of " +
                    "light rather than becoming a wall of it.",
                    new AcceptableValueRange<float>(1f, 100f)));

            BeaconHeightMeters = config.Bind(
                "4 - The light", "BeaconHeightMeters", 1.2f,
                new ConfigDescription(
                    "How far above the cairn's crown the light sits.",
                    new AcceptableValueRange<float>(0f, 10f)));

            // A COLOUR, not text, since 0.12.0: the Configuration Manager (F1 in game) draws a
            // colour swatch with red, green, blue and alpha sliders for any Color setting. BepInEx
            // reads the old text values ("FFB85A") as colours unchanged, so no file needs migrating.
            BeaconColour = config.Bind(
                "4 - The light", "BeaconColour", new Color(255f / 255f, 184f / 255f, 90f / 255f, 1f),
                "Colour of the light on top of a cairn. The default is firelight. " +
                "In game, press F1 (the Configuration Manager) to pick it with sliders. In this " +
                "file it is hex, RRGGBBAA, for example FFB85AFF. The last two digits (alpha) set " +
                "how strong the light is: FF is full, 00 is invisible. " +
                "Read by each player's own game, and a change shows at once.");

            UseSignColours = config.Bind(
                "4 - The light", "UseSignColours", true,
                "Light a cairn in the colour its builder gave it. Three ways to give one: a colour word " +
                "in square brackets on the sign, like [blue] Home; the sign written as " +
                "<color=#4A9EFF>Home</color>; or an item listed in HeldColours on an " +
                "item stand at the cairn (a surtling core burns red). The item wins when a cairn " +
                "has both. Cairns with neither use BeaconColour. " +
                "Set this to false to use your own BeaconColour on EVERY cairn and ignore both. " +
                "Read by each player's own game, and a change shows at once.");

            HolderPrefabs = config.Bind(
                "4 - The light", "HolderPrefabs", "itemstandh,itemstand",
                "Pieces that can hold an item to colour a cairn (0.13.0): the item stand, lying " +
                "flat (itemstandh) or on a wall (itemstand). One within LandmarkPairMeters of a " +
                "cairn, holding an item listed in HeldColours, gives that cairn the item's colour. " +
                "These may also be placed ON the stones of a cairn when StackableStones is on. " +
                "Set it on the server, and on each player's game for placing them on stones.");

            HeldColours = config.Bind(
                "4 - The light", "HeldColours",
                "Raspberry=E83A3A,Mushroom=FF3030,Bloodbag=B0102A,SurtlingCore=FF4020,Carrot=FF8A1F," +
                "Resin=FF9A2A,Cloudberry=FFB040,Dandelion=FFD21F,MushroomYellow=FFE040,Honey=FFC02A," +
                "Guck=7ACC3A,Ooze=8CE04A,TrophyGreydwarf=5BD13A,Wisp=7FF6FF,GreydwarfEye=4A9EFF," +
                "Blueberries=3A6BFF,MushroomBlue=3A7BFF,FreezeGland=A8E4FF,Thistle=A050FF," +
                "Crystal=EAF4FF,BoneFragments=F2EEE0",
                "Which held items colour a cairn, and how: ItemPrefab=RRGGBB (or RRGGBBAA), " +
                "comma-separated. These are the items' PREFAB names, such as SurtlingCore, not the " +
                "names shown in game. The defaults are cheap, everyday things on purpose: a cairn " +
                "stands in the wild. `cairn holdable` lists every item a stand will hold, and the server " +
                "log names any listed item that is unknown or that no stand will take when a world " +
                "loads. An item not on the list leaves the cairn's colour alone. " +
                "Set it on the server: it decides the colour, and every player sees the same one. " +
                "Players need the same list in their own games for an item vanilla will not put on a " +
                "stand (see StandsHoldListedItems).");

            StandsHoldListedItems = config.Bind(
                "4 - The light", "StandsHoldListedItems", true,
                "Let an item stand hold an item on the HeldColours list even when vanilla will not, " +
                "like a greydwarf eye, and draw the item's own model on the stand. Vanilla only lets a " +
                "stand hold items with a model made for it. This changes what a VANILLA piece accepts, " +
                "and only for listed items on the stands in HolderPrefabs. False leaves stands exactly " +
                "as the game made them. Read by each player's own game: it decides what fits on a " +
                "stand, and draws it.");

            BeaconOcclusion = config.Bind(
                "4 - The light", "BeaconOcclusion", true,
                "Terrain hides a beacon. LEAVE THIS ON. A glow that shines through a mountain " +
                "is not a beacon, it is a waypoint marker in a costume, and house rule A " +
                "forbids exactly that — a ridge between you and a cairn should mean you have " +
                "to move, which is the difference between navigating and being told. Off is " +
                "for diagnosing whether a beacon is missing or merely hidden.");

            EnableRavenVoice = config.Bind(
                "5 - The raven", "EnableRavenVoice", true,
                "Let Hugin say the name of a named landmark you are standing in. FLAVOUR, never " +
                "a navigation channel — vanilla refuses to land the bird below 30m of altitude, " +
                "with a hostile within 10m, or during any world event, so it is silent exactly " +
                "when you would most want it. Off changes nothing else.");

            RavenNameMeters = config.Bind(
                "5 - The raven", "RavenNameMeters", 12f,
                new ConfigDescription(
                    "How close you must stand to a NAMED landmark before the line is offered. " +
                    "Kept inside the raven's own 15m spawn distance, or the bird would be " +
                    "carrying a message about somewhere it cannot reach.",
                    new AcceptableValueRange<float>(2f, 15f)));

            RavenNameMaxLength = config.Bind(
                "5 - The raven", "RavenNameMaxLength", 64,
                new ConfigDescription(
                    "Longest spoken name before it is cut with an ellipsis. Rich text is always " +
                    "stripped before speaking — storage stays faithful to what the player typed, " +
                    "the display decides what may be said, and that is the moderation question " +
                    "the scope doc left open being answered conservatively.",
                    new AcceptableValueRange<int>(8, 200)));

            // ---- Tombstones (0.11.0) ---------------------------------------------------
            // The ONE light this mod makes without a player building it: house rule B was
            // amended for this by the owner on 2026-10-19, default off.

            EnableTombstoneLights = config.Bind(
                "6 - Tombstones", "EnableTombstoneLights", false,
                "Off by default. Turn it on to put a red light over your tombstone after you " +
                "die, so you can find your things on a world with no map. Only you see the light " +
                "over your own tombstone. Hills and walls still hide it, like a cairn's light. It " +
                "goes out once the tombstone is emptied. " +
                "Set it on the server. In single player, set it in your own game.");

            ShowTombstoneLight = config.Bind(
                "6 - Tombstones", "ShowTombstoneLight", true,
                "Draw the light over your own tombstone when the server has tombstone lights " +
                "turned on. Set it to false to hide it in your own game only. " +
                "Read by each player's own game.");

            TombstoneColour = config.Bind(
                "6 - Tombstones", "TombstoneColour", new Color(255f / 255f, 42f / 255f, 42f / 255f, 1f),
                "Colour of the light over your tombstone. The default is red, so it can never be " +
                "mistaken for a cairn. In game, press F1 (the Configuration Manager) to pick it " +
                "with sliders. In this file it is hex, RRGGBBAA, for example FF2A2AFF; the last " +
                "two digits (alpha) set how strong the light is. " +
                "Read by each player's own game, and a change shows at once.");

            // Raised for a player's request, 2026-11-13: at the cairn's 1.2 m the glow sat on the
            // tombstone itself.
            TombstoneHeightMeters = config.Bind(
                "6 - Tombstones", "TombstoneHeightMeters", 2.5f,
                new ConfigDescription(
                    "How far above your tombstone its light sits, in metres. " +
                    "Read by each player's own game.",
                    new AcceptableValueRange<float>(0f, 10f)));
        }
    }
}
