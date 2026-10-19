using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using RavenIron.Cairn.Config;
using RavenIron.Cairn.Core;
using UnityEngine;

namespace RavenIron.Cairn.Patches
{
    /// <summary>
    /// The `cairn` console — the locked-decision prefix. Registered from an InitTerminal
    /// postfix; the ConsoleCommand constructor assigns into Terminal's command map by
    /// lowered name (decompile-verified 2026-09-01), so it overwrites its own entry and the
    /// repeat call per terminal is harmless.
    ///
    /// Reads answer everywhere; mutations follow Ragnarok's Wrath's self-gating rule — they
    /// run only where the store lives, and a pure client is refused with directions rather
    /// than trusted.
    /// </summary>
    [HarmonyPatch(typeof(Terminal), "InitTerminal")]
    public static class Patch_Terminal_Cairn
    {
        private static bool _confirmed;

        /// <summary>
        /// Resolved lazily and never latched on failure, per house style rule 5.
        /// </summary>
        private static FieldInfo _commandsField;

        private static void Postfix()
        {
            try
            {
                new Terminal.ConsoleCommand("cairn", "Cairn: status | landmarks | prefabs | pieces | beacons | raven | save", Run);

                if (_confirmed) return;
                _confirmed = true;

                // Read it back rather than assuming. "Registered a command" and "the command
                // exists" are different claims, and only one of them can be checked.
                IDictionary map = ReadCommandMap();
                if (map == null)
                {
                    Cairn.Log.LogWarning(
                        "cairn console: registered, but Terminal's command map could not be read " +
                        "back, so registration is UNCONFIRMED. The command may still work. If " +
                        "Valheim renamed the field, this instrument needs updating.");
                }
                else if (map.Contains("cairn"))
                {
                    Cairn.Log.LogInfo(
                        $"cairn console registered — confirmed present in Terminal's command map " +
                        $"({map.Count} command(s) total).");
                }
                else
                {
                    Cairn.Log.LogError(
                        "cairn console did NOT appear in Terminal's command map after registration. " +
                        "The console is the only instrument this build has; treat this as fatal.");
                }
            }
            catch (Exception ex)
            {
                Cairn.Log.LogWarning($"cairn console: register failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Terminal's command dictionary, by reflection. Returns null when it cannot be read.
        ///
        /// HOUSE STYLE RULE 5, and the most expensive way to learn it: `Terminal.commands` is
        /// `public static` in the PUBLICIZED reference assembly and private at runtime. Naming
        /// it directly in source produced
        ///
        ///     FieldAccessException: Field `Terminal:commands' is inaccessible
        ///
        /// on a dedicated server on 2026-09-02 — and the try/catch wrapped around it did not
        /// help, because Mono raises that when the METHOD IS COMPILED, not when the line runs.
        /// The whole Postfix aborted, taking Terminal.Awake and Chat.Awake with it, and the
        /// server shut down before it finished booting. A clean build had reported 0 warnings.
        ///
        /// Two lessons, both load-bearing: reach private members only through reflection, and
        /// never treat try/catch as protection against an inaccessible member — the exception
        /// arrives too early for it. Returned as a non-generic IDictionary on purpose: that
        /// names neither the field's type nor its generic arguments anywhere in our IL.
        /// </summary>
        private static IDictionary ReadCommandMap()
        {
            try
            {
                if (_commandsField == null)
                    _commandsField = AccessTools.Field(typeof(Terminal), "commands");

                if (_commandsField == null)
                {
                    Cairn.Log.LogWarning(
                        "cairn console: Terminal has no field named 'commands' — Valheim's API moved.");
                    return null;
                }

                return _commandsField.GetValue(null) as IDictionary;
            }
            catch (Exception ex)
            {
                Cairn.Log.LogWarning($"cairn console: reading Terminal's command map threw: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Every line this console says goes to the screen AND to the log.
        ///
        /// `args.Context.AddString` draws to the in-game console and nowhere else, so a
        /// diagnostic answered that way exists only on one person's monitor. `cairn prefabs`
        /// was built specifically to carry an answer back to a developer and, on its first
        /// live run on 2026-09-02, told nobody who could act on it. A console command is an
        /// instrument; an instrument that leaves no trace is a conversation.
        /// </summary>
        private static void Say(Terminal.ConsoleEventArgs args, string line)
        {
            args.Context?.AddString(line);
            Cairn.Log.LogInfo("cairn> " + line);
        }

        private static void Run(Terminal.ConsoleEventArgs args)
        {
            try
            {
                string sub = args.Args.Length > 1 ? args.Args[1].ToLowerInvariant() : "help";
                switch (sub)
                {
                    case "status":    Status(args); return;
                    case "landmarks": Landmarks(args); return;
                    case "prefabs":   Prefabs(args); return;
                    case "pieces":    Pieces(args); return;
                    case "beacons":   Beacons(args); return;
                    case "raven":     RavenVoice(args); return;
                    case "save":      SaveNow(args); return;
                    default:          Help(args); return;
                }
            }
            catch (Exception ex)
            {
                Say(args, "cairn: " + ex.Message);
                Cairn.Log.LogWarning($"cairn console threw: {ex}");
            }
        }

        private static void Help(Terminal.ConsoleEventArgs args)
        {
            Say(args, "cairn status         — what this process is, and what is running");
            Say(args, "cairn landmarks      — every landmark in the ledger");
            Say(args, "cairn prefabs <text> — every prefab name containing <text>");
            Say(args, "cairn pieces <text>  — only BUILDABLE pieces, with tool and cost");
            Say(args, "cairn beacons        — every light this client knows, and why it is dark");
            Say(args, "cairn raven          — why Hugin is or is not saying anything");
            Say(args, "cairn save           — flush the ledger now (authority only)");
        }

        /// <summary>
        /// Ask the GAME what its prefabs are called.
        ///
        /// This exists because guessing cost a whole debugging cycle. Prefab names live in
        /// asset bundles rather than the assemblies, so they cannot be decompiled, and an
        /// attempt to recover them by searching world saves for the stable hash produced a
        /// confident wrong answer — the control prefab returned hits in one save and none in
        /// another, which is the tell that the instrument, not the data, was broken. The only
        /// authority on a prefab name is a loaded ZNetScene.
        ///
        /// Everything here goes through reflection, including the singleton. Naming a member
        /// that turns out to be private at runtime is a FieldAccessException raised when this
        /// METHOD IS COMPILED, which no try/catch inside it can catch — that is what killed
        /// the server on 2026-09-02.
        /// </summary>
        private static void Prefabs(Terminal.ConsoleEventArgs args)
        {
            string filter = (args.Args.Length > 2 ? args.Args[2] : "sign").ToLowerInvariant();

            try
            {
                Type sceneType = AccessTools.TypeByName("ZNetScene");
                if (sceneType == null) { Say(args, "cairn: no ZNetScene type."); return; }

                object scene = ReadSingleton(sceneType);
                if (scene == null)
                {
                    Say(args, "cairn: ZNetScene is not loaded — join or start a world first.");
                    return;
                }

                FieldInfo prefabsField = AccessTools.Field(sceneType, "m_prefabs");
                if (prefabsField == null)
                {
                    Say(args, "cairn: ZNetScene has no m_prefabs — Valheim's API moved.");
                    return;
                }

                if (!(prefabsField.GetValue(scene) is IEnumerable all))
                {
                    Say(args, "cairn: m_prefabs was not enumerable.");
                    return;
                }

                var hits = new List<string>();
                int total = 0;
                foreach (object o in all)
                {
                    var go = o as UnityEngine.Object;
                    if (go == null) continue;
                    total++;
                    string n = go.name;
                    if (n != null && n.ToLowerInvariant().Contains(filter)) hits.Add(n);
                }

                hits.Sort(StringComparer.OrdinalIgnoreCase);
                Say(args, 
                    $"cairn: {hits.Count} of {total} prefab(s) contain \"{filter}\"");
                foreach (string n in hits) Say(args, "  " + n);
            }
            catch (Exception ex)
            {
                Say(args, "cairn: prefab listing failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Only the things a player can actually BUILD, with the tool that builds them and
        /// what they cost.
        ///
        /// `cairn prefabs` lists every prefab the scene knows — terrain rocks, VFX, sound
        /// effects, location props — and only a fraction are placeable. Inferring
        /// buildability from a prefab NAME cost three round trips in a single day, so this
        /// asks the only things that actually decide it: does the prefab carry a `Piece`
        /// component, and is it in some tool's `PieceTable`.
        /// </summary>
        private static void Pieces(Terminal.ConsoleEventArgs args)
        {
            string filter = (args.Args.Length > 2 ? args.Args[2] : "stone").ToLowerInvariant();

            try
            {
                Type sceneType = AccessTools.TypeByName("ZNetScene");
                object scene = sceneType != null ? ReadSingleton(sceneType) : null;
                if (scene == null)
                {
                    Say(args, "cairn: ZNetScene is not loaded — join or start a world first.");
                    return;
                }

                FieldInfo prefabsField = AccessTools.Field(sceneType, "m_prefabs");
                if (!(prefabsField?.GetValue(scene) is IEnumerable all))
                {
                    Say(args, "cairn: could not read ZNetScene.m_prefabs — Valheim's API moved.");
                    return;
                }

                var prefabs = new List<GameObject>();
                foreach (object o in all) if (o is GameObject go) prefabs.Add(go);

                // Which tool builds what. The TABLE decides the tool, not the piece, so this
                // is the only way to answer "is it in the cultivator?".
                Dictionary<GameObject, List<string>> tools = MapPiecesToTools(prefabs);

                var lines = new List<string>();
                var details = new Dictionary<string, string>();
                foreach (GameObject go in prefabs)
                {
                    if (go == null || go.name == null) continue;
                    if (go.name.ToLowerInvariant().IndexOf(filter, StringComparison.Ordinal) < 0) continue;

                    Component piece = go.GetComponent("Piece");
                    if (piece == null) continue;   // not buildable — the whole point of this

                    string tool = tools.TryGetValue(go, out List<string> t) && t.Count > 0
                        ? string.Join("/", t.ToArray())
                        : "no table";

                    string row = $"  {go.name}  [{tool}]  {DescribeCost(piece)}  {DescribePlacement(piece)}";
                    lines.Add(row);
                    // The second line answers the question the first one cannot: can a piece
                    // be placed ON one of these? Prefab names are unique, so the row is the key.
                    details[row] = "      stack: " + DescribeStacking(go, piece);
                }

                lines.Sort(StringComparer.OrdinalIgnoreCase);
                Say(args, $"cairn: {lines.Count} BUILDABLE piece(s) containing \"{filter}\"");
                foreach (string l in lines)
                {
                    Say(args, l);
                    if (details.TryGetValue(l, out string d)) Say(args, d);
                }
            }
            catch (Exception ex)
            {
                Say(args, "cairn: piece listing failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Every build tool carries a PieceTable listing what it can place. Walked the other
        /// way round here, so a piece can name its tool.
        /// </summary>
        private static Dictionary<GameObject, List<string>> MapPiecesToTools(List<GameObject> prefabs)
        {
            var map = new Dictionary<GameObject, List<string>>();

            foreach (GameObject go in prefabs)
            {
                if (go == null) continue;

                try
                {
                    Component drop = go.GetComponent("ItemDrop");
                    if (drop == null) continue;

                    object itemData = AccessTools.Field(drop.GetType(), "m_itemData")?.GetValue(drop);
                    object shared = itemData != null
                        ? AccessTools.Field(itemData.GetType(), "m_shared")?.GetValue(itemData)
                        : null;
                    object table = shared != null
                        ? AccessTools.Field(shared.GetType(), "m_buildPieces")?.GetValue(shared)
                        : null;
                    if (table == null) continue;   // not a build tool

                    if (!(AccessTools.Field(table.GetType(), "m_pieces")?.GetValue(table) is IEnumerable pieces))
                        continue;

                    foreach (object p in pieces)
                    {
                        var piece = p as GameObject;
                        if (piece == null) continue;

                        if (!map.TryGetValue(piece, out List<string> list))
                        {
                            list = new List<string>(2);
                            map[piece] = list;
                        }
                        if (!list.Contains(go.name)) list.Add(go.name);
                    }
                }
                catch
                {
                    // One malformed tool must not cost the whole listing.
                }
            }

            return map;
        }

        /// <summary>
        /// Placement rules, which is what decides whether a piece can be STACKED.
        ///
        /// A cairn is stones piled on stones. A piece that snaps to terrain can only ever be
        /// scattered on the ground, however many of them you place - four ground-locked stones
        /// in a four-metre circle is a scatter, not a waymark. So the flags matter as much as
        /// the cost, and neither is guessable from a prefab name.
        /// </summary>
        private static string DescribePlacement(Component piece)
        {
            try
            {
                Type t = piece.GetType();
                var flags = new List<string>(4);

                // EVERY true bool on the Piece, not a hand-picked seven. The first version
                // listed seven flags, reported Placeable_Stone as "free-placement", and was
                // right about all seven — while m_noClipping, which it never looked at, is
                // one of the three things Player.UpdatePlacementGhost can refuse a stack on.
                // A readout that checks a subset is a readout that says "fine" for the wrong
                // reason. Iterating the type also survives Valheim adding flags, which 1.0
                // did (m_allowedInDeepSnow, m_requireDeepSnow, m_vegetationGroundOnly).
                foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (f.FieldType != typeof(bool)) continue;
                    if (!(f.GetValue(piece) is bool b) || !b) continue;

                    switch (f.Name)
                    {
                        // Defaults that are true on every piece and mean nothing here.
                        case "m_enabled":
                        case "m_canRotate":
                        case "m_canBeRemoved":
                            continue;
                        // The ones that decide a stack, shouted.
                        case "m_groundPiece": flags.Add("SNAPS-TO-GROUND"); break;
                        case "m_groundOnly": flags.Add("GROUND-ONLY"); break;
                        case "m_noClipping": flags.Add("NO-CLIPPING"); break;
                        default:
                            flags.Add(f.Name.StartsWith("m_") ? f.Name.Substring(2) : f.Name);
                            break;
                    }
                }

                return flags.Count > 0 ? string.Join(" ", flags.ToArray()) : "free-placement";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// Can a piece be placed ON one of these? Read straight off the prefab, one line.
        ///
        /// Player.UpdatePlacementGhost (1.0.15, decompiled 2026-09-21) can refuse a stone on a
        /// stone in four ways, and none of them is visible from the Piece flags alone:
        ///
        ///   1. PieceRayTest throws away any hit whose collider has an attachedRigidbody, so a
        ///      stone that carries a Rigidbody can NEVER be built on — the ghost simply
        ///      vanishes when you aim at it (NoRayHits). Kinematic makes no difference.
        ///   2. The hit collider's layer has to be in m_placeRayMask, or the ray passes
        ///      through the stone to whatever is under it.
        ///   3. If the piece underneath has a WearNTear with m_supports == false, the status
        ///      is Invalid outright. m_supports is authored on the prefab, never computed.
        ///   4. If the NEW piece has m_noClipping and its collider penetrates the one below
        ///      by more than 0.2m, Invalid. Two rounded stones touching do exactly that.
        ///
        /// Each is prefab data, so each is unknowable off-game and settled by this line.
        /// </summary>
        private static string DescribeStacking(GameObject go, Component piece)
        {
            try
            {
                var blocks = new List<string>(3);
                var notes = new List<string>(6);

                // 1. Rigidbody anywhere on the prefab: every collider under it reports an
                //    attachedRigidbody, and the placement ray refuses those.
                Rigidbody[] bodies = go.GetComponentsInChildren<Rigidbody>(true);
                if (bodies.Length > 0)
                {
                    notes.Add($"rigidbody=YES({(bodies[0].isKinematic ? "kinematic" : "dynamic")})");
                    blocks.Add("rigidbody — the placement ray ignores it, ghost vanishes");
                }
                else notes.Add("rigidbody=no");

                // 2. Collider layers against the placement mask. The names are Player.Awake's
                //    as of 1.0.15; if this ever reports a vanilla stone as unhittable, the
                //    mask moved and this list is what to re-read.
                int placeMask = LayerMask.GetMask(
                    "Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain", "vehicle");
                Collider[] cols = go.GetComponentsInChildren<Collider>(true);
                var layers = new List<string>(2);
                int solid = 0;
                bool offMask = false;
                foreach (Collider c in cols)
                {
                    if (c == null || c.isTrigger) continue;
                    solid++;
                    string layer = LayerMask.LayerToName(c.gameObject.layer);
                    if (!layers.Contains(layer)) layers.Add(layer);
                    if (((1 << c.gameObject.layer) & placeMask) == 0) offMask = true;
                }
                notes.Add($"colliders={solid}({string.Join(",", layers.ToArray())})");
                if (solid == 0) blocks.Add("no solid collider — nothing for the ray to hit");
                else if (offMask) blocks.Add("collider layer outside the placement mask — ray passes through");

                // 3. WearNTear.m_supports on the prefab.
                Component wnt = go.GetComponent("WearNTear");
                if (wnt == null) notes.Add("wearntear=none");
                else
                {
                    Type wt = wnt.GetType();
                    object sup = AccessTools.Field(wt, "m_supports")?.GetValue(wnt);
                    object mat = AccessTools.Field(wt, "m_materialType")?.GetValue(wnt);
                    bool supports = sup is bool sb && sb;
                    notes.Add($"wearntear=yes supports={supports.ToString().ToLowerInvariant()} material={mat}");
                    if (!supports) blocks.Add("supports=false — anything placed on it is Invalid");
                }

                // 4. The clipping test applies to the piece being PLACED, so it is a risk on
                //    the stone going on top rather than a property of the one beneath.
                if (Flag(piece, piece.GetType(), "m_noClipping"))
                    notes.Add("RISK: noClipping — rounded stones penetrate >0.2m and go red");

                // Snap points: none means the ghost sits by collider contact, which is what a
                // loose pile wants. Reflected because GetSnapPoints reads public here and this
                // file has been burned by that before.
                try
                {
                    var snaps = new List<Transform>();
                    // Two overloads exist (a static 4-arg one too), so the name alone is an
                    // ambiguous match and would resolve to nothing.
                    AccessTools.Method(piece.GetType(), "GetSnapPoints", new[] { typeof(List<Transform>) })
                        ?.Invoke(piece, new object[] { snaps });
                    notes.Add($"snaps={snaps.Count}");
                }
                catch { /* the count is a nicety; the verdict does not depend on it */ }

                string verdict = blocks.Count == 0
                    ? "STACKABLE by every rule the engine has"
                    : "BLOCKED: " + string.Join("; ", blocks.ToArray());

                return verdict + "  [" + string.Join("  ", notes.ToArray()) + "]";
            }
            catch (Exception ex)
            {
                return "could not read the prefab: " + ex.Message;
            }
        }

        private static bool Flag(object obj, Type t, string field)
        {
            object v = AccessTools.Field(t, field)?.GetValue(obj);
            return v is bool b && b;
        }

        /// <summary>Build cost as "item xN" pairs. Blank when it cannot be read.</summary>
        private static string DescribeCost(Component piece)
        {
            try
            {
                if (!(AccessTools.Field(piece.GetType(), "m_resources")?.GetValue(piece) is IEnumerable reqs))
                    return "";

                var parts = new List<string>(3);
                foreach (object req in reqs)
                {
                    if (req == null) continue;

                    var item = AccessTools.Field(req.GetType(), "m_resItem")?.GetValue(req) as Component;
                    object amount = AccessTools.Field(req.GetType(), "m_amount")?.GetValue(req);
                    if (item == null || item.gameObject == null || amount == null) continue;

                    parts.Add($"{item.gameObject.name} x{amount}");
                }

                return parts.Count > 0 ? string.Join(", ", parts.ToArray()) : "free";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// What the renderer is actually doing. A beacon can be absent for four different
        /// reasons - never synced, out of range, occluded, or the renderer never started - and
        /// from outside the game they look identical.
        /// </summary>
        private static void Beacons(Terminal.ConsoleEventArgs args)
        {
            if (!Cairn.HasRenderer)
            {
                Say(args, "cairn: no renderer on this process — beacons are drawn client-side.");
                return;
            }

            Visuals.Beacon beacon = Visuals.Beacon.Instance;
            if (beacon == null)
            {
                Say(args, "cairn: the beacon renderer was never created. Is EnableBeacons off?");
                return;
            }

            List<string> lines = beacon.Describe();
            Say(args, $"cairn: {lines.Count} beacon line(s), occlusion={ModConfig.BeaconOcclusion.Value}");
            foreach (string l in lines) Say(args, l);
        }

        /// <summary>Why the bird is quiet — four causes that look identical from in the world.</summary>
        private static void RavenVoice(Terminal.ConsoleEventArgs args)
        {
            if (!Cairn.HasRenderer)
            {
                Say(args, "cairn: no renderer here — the raven is client-side.");
                return;
            }

            Voice.HuginVoice voice = Voice.HuginVoice.Instance;
            if (voice == null)
            {
                Say(args, "cairn: the voice component was never created. Is EnableRavenVoice off?");
                return;
            }

            Say(args, "cairn: the raven");
            foreach (string l in voice.Describe()) Say(args, l);
        }

        /// <summary>A game singleton, however the class spells it, without naming it in our IL.</summary>
        private static object ReadSingleton(Type t)
        {
            // "however the class spells it" now includes s_instance: Valheim 1.0.7 renamed
            // ZoneSystem's backing field m_instance -> s_instance. The property fallback below
            // already covered that by luck; naming it here makes the coverage deliberate.
            FieldInfo f = AccessTools.Field(t, "m_instance")
                          ?? AccessTools.Field(t, "s_instance")
                          ?? AccessTools.Field(t, "instance");
            object v = f?.GetValue(null);

            if (v == null)
            {
                PropertyInfo p = AccessTools.Property(t, "instance");
                v = p?.GetValue(null, null);
            }

            if (v is UnityEngine.Object uo && uo == null) return null;   // Unity's fake null
            return v;
        }

        /// <summary>
        /// Reads answer everywhere. Where the ledger lives (single player, a host, the server's
        /// own console) this lists every landmark. On a joined player's game, which has no
        /// ledger, it lists the NAMED lit cairns the server has sent, nearest first (0.10.3):
        /// until then it answered "no landmarks" to every player on a dedicated server, while
        /// the README promised it listed every name.
        /// </summary>
        private static void Landmarks(Terminal.ConsoleEventArgs args)
        {
            var c = CultureInfo.InvariantCulture;

            // A joined player's game has no ledger: it lives on the server. What this game DOES
            // hold is the names of the lit cairns the server sends it, nearest a player first,
            // which is what Hugin speaks from. List those, nearest first, rather than "none".
            if (!Persistence.IsLoaded)
            {
                List<Net.LandmarkSync.Beacon> sent = Net.LandmarkSync.Current();
                var names = new List<string>(sent.Count);
                var spots = new List<Vector3>(sent.Count);
                foreach (Net.LandmarkSync.Beacon b in sent) { names.Add(b.Name); spots.Add(b.Light); }

                Player me = Player.m_localPlayer;
                Vector3 here = me != null ? me.transform.position : Vector3.zero;
                List<int> order = NamedListing.NamedByDistance(names, spots, here);

                if (order.Count == 0)
                {
                    Say(args, sent.Count == 0
                        ? "cairn: no named cairns yet. The server has not sent any lit cairns to this game."
                        : $"cairn: none of the {sent.Count.ToString(c)} lit cairn(s) near you has a name. " +
                          $"Write on a sign within {ModConfig.LandmarkPairMeters.Value.ToString("0.#", c)} m of a cairn to name it.");
                    return;
                }

                Say(args, $"cairn: {order.Count.ToString(c)} named cairn(s) near you, nearest first " +
                          "(the server sends the lit cairns nearest its players)");
                foreach (int i in order)
                {
                    float d = NamedListing.DistanceXZ(spots[i], here);
                    Say(args, $"  \"{names[i]}\"  {d.ToString("0", c)} m  at {sent[i].Key}");
                }
                return;
            }

            List<Landmark> all = LandmarkStore.Snapshot();

            if (all.Count == 0)
            {
                Say(args, 
                    $"cairn: no landmarks (store loaded={Persistence.IsLoaded}, " +
                    $"authority={Cairn.IsSimulationAuthority()})");
                return;
            }

            Say(args, $"cairn: {all.Count.ToString(c)} landmark(s)");
            foreach (Landmark l in all)
            {
                Say(args, 
                    $"  {l.Key}  \"{l.Name}\"  by {l.Author}  first seen " +
                    new DateTime(l.FirstSeenUtcTicks, DateTimeKind.Utc).ToString("u", c));
            }
        }

        /// <summary>
        /// Mutations run only where the store lives. A pure client is refused with directions
        /// rather than trusted — Ragnarok's Wrath's self-gating rule, inherited.
        /// </summary>
        private static void SaveNow(Terminal.ConsoleEventArgs args)
        {
            if (!Persistence.IsLoaded)
            {
                Say(args, 
                    "cairn: no ledger on this process — the store lives on the server. " +
                    "Type this at the server's own console, or on a listen host.");
                return;
            }

            Persistence.Save(force: true);
            Say(args, $"cairn: saved {LandmarkStore.Count.ToString(CultureInfo.InvariantCulture)} landmark(s).");
        }

        /// <summary>
        /// The whole instrument for this build. It answers the three questions the skeleton
        /// exists to settle: did the plugin load, what process does it think it is, and is
        /// the tick actually running.
        /// </summary>
        private static void Status(Terminal.ConsoleEventArgs args)
        {
            var c = CultureInfo.InvariantCulture;

            Say(args, $"Cairn v{Cairn.PluginVersion}");
            Say(args, 
                $"  role       : {CairnTick.Role()} (authority={Cairn.IsSimulationAuthority()}, " +
                $"dedicated={Cairn.IsDedicated()})");
            Say(args, $"  renderer   : {Cairn.HasRenderer}");
            Say(args, 
                $"  tick       : {(CairnTick.WorldSeen ? "running" : "not yet — no world")}, " +
                $"{CairnTick.SystemCount.ToString(c)} system(s), " +
                $"budget {ModConfig.TickBudgetMs.Value.ToString("0.##", c)}ms/frame");
            Say(args,
                $"  enabled    : {ModConfig.Enabled.Value} (verbose={ModConfig.VerboseLogging.Value})");
            Say(args,
                $"  config     : layout {ModConfig.ConfigVersion.Value.ToString(c)} — {ConfigMigration.StatusLine()}");
            Say(args, 
                $"  ledger     : {(Persistence.IsLoaded ? "loaded" : "not on this process")}, " +
                $"{LandmarkStore.Count.ToString(c)} landmark(s)" +
                (LandmarkStore.IsDirty ? ", unsaved changes" : ""));

            // Lit vs sent. Only the authority broadcasts, so a pure client has nothing to say
            // here — and says so, rather than printing a zero that reads like "none are lit".
            int cap = ModConfig.BeaconMaxCount.Value;
            if (!Persistence.IsLoaded)
                Say(args, $"  broadcast  : the server's — on a client, `cairn beacons` shows what arrived");
            else if (Net.LandmarkSync.LastLit < 0)
                Say(args, $"  broadcast  : none sent yet (cap {cap.ToString(c)})");
            else
                Say(args,
                    $"  broadcast  : {Net.LandmarkSync.LastLit.ToString(c)} lit, " +
                    $"{Net.LandmarkSync.LastSent.ToString(c)} sent, cap {cap.ToString(c)}" +
                    (Net.LandmarkSync.LastLit > Net.LandmarkSync.LastSent
                        ? " — OVER THE CAP: only the cairns nearest a player reach clients"
                        : ""));

            // Tombstone lights (0.11.0). Says which side decided and what it saw, because a
            // missing red light has four causes that look the same: off on the server, hidden in
            // this game, nothing received, or a tombstone that is not this player's.
            int mine = Net.TombstoneSync.Mine().Count;
            string show = ModConfig.ShowTombstoneLight.Value ? "shown" : "HIDDEN in this game";
            if (Persistence.IsLoaded)
            {
                string world = Systems.TombstoneSystem.LatestAt < 0f
                    ? "not swept yet"
                    : $"{Systems.TombstoneSystem.Latest.Count.ToString(c)} in the world";
                Say(args, ModConfig.EnableTombstoneLights.Value
                    ? $"  tombstones : lights ON — {world}, {mine.ToString(c)} yours ({show})"
                    : "  tombstones : lights OFF (EnableTombstoneLights = false here)");
            }
            else
            {
                int got = Net.TombstoneSync.LastReceivedCount;
                Say(args, got < 0
                    ? "  tombstones : nothing received — the server has tombstone lights off, or is older than 0.11.0"
                    : $"  tombstones : last push {got.ToString(c)} (all players), {mine.ToString(c)} yours ({show}), " +
                      $"you are {Net.TombstoneSync.LocalPlayerId().ToString(c)}");
            }
        }
    }
}
