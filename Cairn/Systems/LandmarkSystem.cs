using System;
using System.Collections.Generic;
using RavenIron.Cairn.Config;
using RavenIron.Cairn.Core;
using UnityEngine;

namespace RavenIron.Cairn.Systems
{
    /// <summary>
    /// The sweep: stacked stone becomes a cairn, and a named sign beside it gives it a name.
    ///
    /// Reads ZDOs rather than instantiated objects, so it sees everything the server knows
    /// about and not merely what is near a player. That is the whole reason the ledger can
    /// be complete on an empty server.
    ///
    /// THE WALK. `ZDOMan.GetAllZDOsWithPrefabIterative` is vanilla's own self-chunking
    /// traversal (decompile-verified 2026-09-02: appends matches by prefab hash, advances an
    /// index through the sector array, returns true once past the end and the outside-sector
    /// list is drained). One WHOLE prefab is drained per tick — RW learned that resuming one
    /// chunk per tick stretches a rotation across the better part of an hour, and vanilla's
    /// own callers drain it in a loop within one frame.
    ///
    /// A ROTATION covers every sign prefab and every stone prefab. Nothing is applied until
    /// it completes, because a pile cannot be judged from the stone of one prefab and a sign
    /// cannot be paired with a pile that has not been found yet.
    ///
    /// PREFAB NAMES ARE CONFIG, NOT CODE. They are data about the game's content, they drift
    /// with patches and modded pieces, and a wrong one costs a SILENT zero matches — which is
    /// why every completed rotation logs its per-prefab counts, always.
    /// </summary>
    public class LandmarkSystem : IWorldSystem
    {
        public string Name => "LandmarkSystem";
        public bool Enabled => ModConfig.EnableLandmarks.Value;

        /// <summary>
        /// Seconds between sweeping ONE prefab, derived so that a FULL ROTATION takes
        /// `LandmarkRotationSeconds`.
        ///
        /// This used to be the configured value directly, which meant the interval governed
        /// each prefab and the rotation cost scaled with how many were listed: four prefabs
        /// at 45s was three minutes before a newly built cairn was even noticed. Latency
        /// should not be a tax on the length of a config list. Now adding a prefab makes each
        /// step shorter rather than the wait longer.
        /// </summary>
        public float IntervalSeconds =>
            Mathf.Max(0.5f, ModConfig.LandmarkRotationSeconds.Value / Mathf.Max(1, _targets.Count));

        private struct Target
        {
            public string Prefab;
            public bool IsSign;
        }

        private readonly List<Target> _targets = new List<Target>(8);
        private int _cursor;

        // Every stone and sign prefab by its hash, so a destroyed object can be recognised with
        // one lookup (CairnNotice.OnZdoDestroyed). Rebuilt with _targets.
        private readonly Dictionary<int, string> _watchedHashes = new Dictionary<int, string>(8);

        /// <summary>Is this prefab hash a stone or sign the sweep looks for? Names it if so.</summary>
        public bool IsWatched(int prefabHash, out string prefab) => _watchedHashes.TryGetValue(prefabHash, out prefab);

        // GetAllZDOsWithPrefabIterative's resume state for the prefab currently mid-walk.
        private readonly List<ZDO> _found = new List<ZDO>(64);
        private int _sweepIndex;

        // Accumulated across the WHOLE rotation, applied only when it completes.
        private readonly List<Vector3> _stones = new List<Vector3>(256);
        private readonly List<Vector3> _signPositions = new List<Vector3>(32);
        private readonly List<string> _signNames = new List<string>(32);
        private readonly List<string> _signAuthors = new List<string>(32);

        private readonly Dictionary<string, int> _foundCounts = new Dictionary<string, int>(8);
        private readonly Dictionary<string, int> _keptCounts = new Dictionary<string, int>(8);

        /// <summary>
        /// False once any prefab in this rotation failed. Pruning is skipped for an unclean
        /// rotation: a sweep that threw halfway has not proved a landmark is gone, and
        /// deleting on incomplete evidence is how a ledger quietly empties itself.
        /// </summary>
        private bool _rotationClean = true;

        public void Initialise()
        {
            // Initialise runs once PER WORLD. A rotation cut short by leaving the last world
            // must not carry that world's stone and signs into this one, where they would pair
            // with this world's piles and be saved to this world's ledger.
            _cursor = 0;
            _found.Clear();
            _sweepIndex = 0;
            _stones.Clear();
            _signPositions.Clear();
            _signNames.Clear();
            _signAuthors.Clear();
            _foundCounts.Clear();
            _keptCounts.Clear();
            _rotationClean = true;
            _quickAreas.Clear();

            _targets.Clear();
            AddTargets(ModConfig.SignPrefabs.Value, isSign: true);
            AddTargets(ModConfig.StonePrefabs.Value, isSign: false);

            var signs = new List<string>();
            var stones = new List<string>();
            _watchedHashes.Clear();
            foreach (Target t in _targets)
            {
                (t.IsSign ? signs : stones).Add(t.Prefab);
                _watchedHashes[t.Prefab.GetStableHashCode()] = t.Prefab;
            }

            Cairn.Log.LogInfo(
                $"[{Name}] sweeping every {IntervalSeconds:F1}s per prefab, {ModConfig.LandmarkRotationSeconds.Value:F0}s per rotation — " +
                $"signs: {(signs.Count > 0 ? string.Join(", ", signs.ToArray()) : "none")}; " +
                $"stone: {(stones.Count > 0 ? string.Join(", ", stones.ToArray()) : "none")}. " +
                $"A pile is {ModConfig.PileMinPieces.Value}-{ModConfig.PileMaxPieces.Value} pieces " +
                $"within {ModConfig.PileMaxExtentMeters.Value:F1}m of footprint; a sign names one " +
                $"from {ModConfig.LandmarkPairMeters.Value:F0}m.");
        }

        private void AddTargets(string csv, bool isSign)
        {
            foreach (string raw in (csv ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string prefab = raw.Trim();
                if (prefab.Length > 0) _targets.Add(new Target { Prefab = prefab, IsSign = isSign });
            }
        }

        public void Tick(float deltaSeconds)
        {
            if (_targets.Count == 0) return;

            // Logout empties ZDOMan (ZNet.Shutdown -> ZDOMan.ShutDown) a frame before the scene
            // swap destroys ZNet. A sweep in that frame finds an empty world without throwing,
            // completes "cleanly", prunes every row, and the world-end flush would then save
            // that pruned ledger into the world's own file. Nothing is swept while shutting down.
            Game game = Game.instance;
            if (game != null && game.IsShuttingDown()) return;

            ZDOMan man = ZDOMan.instance;
            if (man == null) return;

            Target target = _targets[_cursor];

            try
            {
                bool done = false;
                while (!done)
                    done = man.GetAllZDOsWithPrefabIterative(target.Prefab, _found, ref _sweepIndex);
            }
            catch (Exception ex)
            {
                // Do not advance the cursor: the same prefab is retried next tick. An
                // unrecognised prefab name does not throw — it returns no matches — so a throw
                // here means something else, and losing the rotation is the safe response.
                Cairn.Log.LogWarning($"[{Name}] sweep failed on '{target.Prefab}': {ex.Message}");
                _found.Clear();
                _sweepIndex = 0;
                _rotationClean = false;
                return;
            }

            int valid = 0, kept = 0;
            for (int i = 0; i < _found.Count; i++)
            {
                ZDO zdo = _found[i];
                if (zdo == null || !zdo.IsValid()) continue;
                valid++;

                if (target.IsSign)
                {
                    string rawText = zdo.GetString(ZDOVars.s_text, "");
                    string rawAuthor = zdo.GetString(ZDOVars.s_author, "");
                    if (!SignReading.TryRead(rawText, rawAuthor, out string name, out string author))
                        continue;   // a blank sign is not a place

                    _signPositions.Add(zdo.GetPosition());
                    _signNames.Add(name);
                    _signAuthors.Add(author);
                    kept++;
                }
                else
                {
                    _stones.Add(zdo.GetPosition());
                    kept++;
                }
            }

            _foundCounts[target.Prefab] = valid;
            _keptCounts[target.Prefab] = kept;

            _found.Clear();
            _sweepIndex = 0;

            _cursor++;
            if (_cursor < _targets.Count) return;

            _cursor = 0;
            CompleteRotation();
        }

        /// <summary>
        /// Every prefab has been walked, so what was collected is the COMPLETE picture of the
        /// world's stone and named signs, which is what makes both pairing and pruning safe.
        ///
        /// Since 0.12.0 the rules live in <see cref="LedgerUpdate.Apply"/>, shared with the quick
        /// check. The scope here is the whole world EXCEPT the spots a quick check refreshed while
        /// this rotation was running: this rotation's picture of those spots may predate the stone
        /// that was just placed, and applying it would put the new light out until the next lap.
        /// </summary>
        private void CompleteRotation()
        {
            long now = DateTime.UtcNow.Ticks;
            var fresh = new List<Vector3>(_quickAreas);

            LedgerUpdate.Result r = LedgerUpdate.Apply(
                _stones, _signPositions, _signNames, _signAuthors,
                p => !NearAny(p, fresh, QuickRadiusMeters),
                allowPrune: _rotationClean,
                nowUtcTicks: now);

            if (r.Changed > 0 || r.Pruned > 0) Net.LandmarkSync.RequestBroadcast();

            LogRotation(r.Piles + r.Skipped, r.Skipped, r.Changed, r.Pruned, r.Carried);

            _stones.Clear();
            _signPositions.Clear();
            _signNames.Clear();
            _signAuthors.Clear();
            _foundCounts.Clear();
            _keptCounts.Clear();
            _quickAreas.Clear();
            _rotationClean = true;
        }

        // ---- the quick check (0.12.0) -----------------------------------------------------

        /// <summary>
        /// How far from a placed stone or written sign the quick check updates cairns. Inside it
        /// the picture is complete (see QuickCheck), so rows there may be written and pruned.
        /// </summary>
        public const float QuickRadiusMeters = 24f;

        /// <summary>Spots quick-checked since the current rotation began; the rotation leaves them alone.</summary>
        private readonly List<Vector3> _quickAreas = new List<Vector3>(8);

        /// <summary>The live system, so the quick-check queue can reach it. Null before the first world.</summary>
        public static LandmarkSystem Instance { get; private set; }

        public LandmarkSystem() { Instance = this; }

        private static bool NearAny(Vector3 p, List<Vector3> spots, float radius)
        {
            for (int i = 0; i < spots.Count; i++)
                if (LedgerUpdate.DistanceXZ(p, spots[i]) <= radius) return true;
            return false;
        }

        /// <summary>
        /// Update the cairns around one spot NOW, without searching the world.
        ///
        /// Reads only the 3x3 zones around the spot (`ZDOMan.FindSectorObjects` with a one-zone
        /// classic radius, about 192 m square; public in 1.0.21), keeps the stone and sign
        /// prefabs, and applies the usual rules to cairns within <see cref="QuickRadiusMeters"/>.
        /// That inner circle sits at least 40 m inside the zones read, so every stone and sign
        /// that can affect a cairn there was seen: a pile is under 4 m across, a sign names it
        /// from 6 m, and a chain long enough to reach the zone edge is a wall, refused anyway.
        ///
        /// Returns true when the ledger changed, so the caller can send the light at once.
        /// </summary>
        public bool QuickCheck(Vector3 spot)
        {
            if (_targets.Count == 0) return false;
            Game game = Game.instance;
            if (game != null && game.IsShuttingDown()) return false;
            ZDOMan man = ZDOMan.instance;
            if (man == null || ZoneSystem.instance == null) return false;

            var stoneHashes = new HashSet<int>();
            var signHashes = new HashSet<int>();
            foreach (Target t in _targets)
                (t.IsSign ? signHashes : stoneHashes).Add(t.Prefab.GetStableHashCode());

            var found = new List<ZDO>(256);
            try
            {
                man.FindSectorObjects(ZoneSystem.GetZone(spot), new SimulationDistance(1, 0, classic: true), found);
            }
            catch (Exception ex)
            {
                Cairn.Log.LogWarning($"[{Name}] quick check failed near {spot}: {ex.Message}");
                return false;
            }

            var stones = new List<Vector3>(64);
            var signPositions = new List<Vector3>(8);
            var signNames = new List<string>(8);
            var signAuthors = new List<string>(8);

            foreach (ZDO zdo in found)
            {
                if (zdo == null || !zdo.IsValid()) continue;
                int prefab = zdo.GetPrefab();

                if (stoneHashes.Contains(prefab))
                {
                    stones.Add(zdo.GetPosition());
                }
                else if (signHashes.Contains(prefab))
                {
                    if (!SignReading.TryRead(zdo.GetString(ZDOVars.s_text, ""), zdo.GetString(ZDOVars.s_author, ""),
                                             out string name, out string author))
                        continue;
                    signPositions.Add(zdo.GetPosition());
                    signNames.Add(name);
                    signAuthors.Add(author);
                }
            }

            LedgerUpdate.Result r = LedgerUpdate.Apply(
                stones, signPositions, signNames, signAuthors,
                p => LedgerUpdate.DistanceXZ(p, spot) <= QuickRadiusMeters,
                allowPrune: true,
                nowUtcTicks: DateTime.UtcNow.Ticks);

            _quickAreas.Add(spot);

            bool changed = r.Changed > 0 || r.Pruned > 0;
            if (changed || ModConfig.VerboseLogging.Value)
                Cairn.Log.LogInfo(
                    $"[{Name}] quick check at ({spot.x:F0}, {spot.z:F0}): {stones.Count} stone(s), " +
                    $"{signPositions.Count} named sign(s) in {found.Count} nearby object(s) — " +
                    $"{r.Piles} pile(s) here, {r.Changed} changed, {r.Pruned} pruned [{DateTime.Now:HH:mm:ss.fff}]");
            return changed;
        }

        /// <summary>
        /// EVERY completed rotation logs, unconditionally.
        ///
        /// Logging only "interesting" rotations makes SILENCE the answer in the ordinary case,
        /// and silence cannot be told apart from a stopped tick, a disabled system or a
        /// crashed server. On 2026-09-02 a live test sat through several quiet rotations and
        /// the log could not say which of those it was watching. One line per rotation is not
        /// noise; an unfalsifiable quiet is.
        /// </summary>
        private void LogRotation(int pileCount, int leftToQuick, int changed, int pruned, int carried)
        {
            var parts = new List<string>(_foundCounts.Count);
            int signsFound = 0, stonesFound = 0;

            foreach (Target t in _targets)
            {
                _foundCounts.TryGetValue(t.Prefab, out int found);
                _keptCounts.TryGetValue(t.Prefab, out int kept);
                parts.Add($"{t.Prefab} found={found} kept={kept}");

                if (t.IsSign) signsFound += found; else stonesFound += found;
            }

            // The zero cases mean different things and must never share a message.
            string tail = "";
            if (signsFound == 0 && stonesFound == 0)
                tail = " — NOTHING FOUND AT ALL: either this world is bare, or every prefab name " +
                       "is wrong. Check with `cairn prefabs sign` and `cairn prefabs stone`.";
            else if (stonesFound > 0 && pileCount == 0)
                tail = " — stone exists but none of it is shaped like a cairn: too few pieces, " +
                       "too many, or too wide a footprint.";

            Cairn.Log.LogInfo(
                $"[{Name}] sweep complete ({string.Join(", ", parts.ToArray())}) — " +
                $"{pileCount} pile(s), {LandmarkStore.Count} landmark(s), {changed} changed, {pruned} pruned" +
                (leftToQuick > 0 ? $" ({leftToQuick} pile(s) left as a quick check just set them)" : "") +
                (carried > 0 ? $", {carried} history carried across a drift" : "") +
                (_rotationClean ? "" : ", PRUNING SKIPPED (a prefab failed this rotation)") +
                tail + (ModConfig.VerboseLogging.Value ? $" [{DateTime.Now:HH:mm:ss.fff}]" : ""));
        }
    }
}
