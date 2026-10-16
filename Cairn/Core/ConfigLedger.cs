using System;
using System.Collections.Generic;
using System.Globalization;

namespace RavenIron.Cairn.Core
{
    /// <summary>
    /// The config migration's decisions, PURE and off-game: no BepInEx, no Unity, so the harness
    /// runs it in a second. Ported 2026-10-16 from FireFront's `Config/ConfigLedger.cs`, itself
    /// the family's shape (Wu'barrk's WingsoftheValkyrie, by way of Valkyrie's Cargo): a stamped
    /// layout version under `[0 - Meta]`, a rebase table keyed by the version it produces, and
    /// the one rule that matters â€” a stored value still equal to an OLD default belongs to the
    /// mod and moves to the new default; anything else is an admin's and is kept, by name, in
    /// the log.
    ///
    /// Why Cairn needs it now: BepInEx persists every bound value, so a changed default never
    /// reaches an install that already wrote the key. 0.9.2 raises BeaconMaxCount 100 -> 250
    /// because 100 was reached in a real playthrough, and without this every server that
    /// already has a config file â€” the one that reported it included â€” would stay at 100.
    ///
    /// NO RETIREMENT MACHINERY, deliberately. FireFront's ledger can also drop keys a build no
    /// longer binds; Cairn has nothing to retire, and machinery with no rung reads as a feature
    /// that works without ever having run. Port it in the same change as the first retire rung
    /// (FireFront's `Retire` + `ConsumeRetiredKey`, with the stamp gate on a drop that throws).
    ///
    /// Versions (nothing before 0.9.2 stamped one):
    ///   0 = any unstamped file, whatever build wrote it.
    ///   1 = 0.9.2: `4 - The light.BeaconMaxCount` still at 100 moves to 250. Current.
    /// </summary>
    public static class ConfigLedger
    {
        /// <summary>
        /// "0 - " so it sorts FIRST: BepInEx writes sections ordered by name, and a bare "Meta"
        /// lands at the bottom of the file, where Ragnarok's Wrath's now sits for good because
        /// renaming it would orphan every stamp already written. Undertow learned the same.
        /// </summary>
        public const string MetaSection = "0 - Meta";
        public const string VersionKey = "ConfigVersion";
        public const int CurrentVersion = 1;

        /// <summary>One slot's every old default. A stored value equal to ANY of them is the mod's own, not admin work.</summary>
        public sealed class Rebase
        {
            public string Section;
            public string Key;
            public string[] OldDefaults;
        }

        /// <summary>Keyed by the version the step produces: <c>Rebases[n]</c> takes a file at n-1 up to n.</summary>
        private static readonly Dictionary<int, Rebase[]> Rebases = new Dictionary<int, Rebase[]>
        {
            { 1, new[]
                {
                    // 0.9.2 moved the default 100 -> 250. At 100 a no-map waymark trail ran out, and
                    // 0.9.1 then kept the OLDEST cairns, so every new one stayed dark for joined
                    // players. BepInEx writes an int as "100", so that is the text to match.
                    new Rebase { Section = "4 - The light", Key = "BeaconMaxCount", OldDefaults = new[] { "100" } },
                }
            },
        };

        /// <summary>One slot whose stored value was NOT an old default â€” real admin work, kept and named in the log.</summary>
        public struct KeptSlot
        {
            public string Slot;
            public string Value;
        }

        /// <summary>The whole plan a file's snapshot produces. Nothing to do plans an empty one.</summary>
        public sealed class MigrationPlan
        {
            public int FromVersion;
            public int ToVersion;
            public List<string> ResetToDefault = new List<string>();
            public List<KeptSlot> Kept = new List<KeptSlot>();
        }

        public static string Slot(string section, string key) => section + "::" + key;

        /// <summary>The inverse of <see cref="Slot"/>. False for anything that is not "Section::Key".</summary>
        public static bool SplitSlot(string slot, out string section, out string key)
        {
            section = null; key = null;
            if (slot == null) return false;
            int i = slot.IndexOf("::", StringComparison.Ordinal);
            if (i <= 0 || i + 2 >= slot.Length) return false;
            section = slot.Substring(0, i);
            key = slot.Substring(i + 2);
            return true;
        }

        /// <summary>
        /// BepInEx config files are plain INI: `[Section]` headers, `#` comments, blank lines, and
        /// `Key = value` where the value may itself contain `=` or `,`. Keyed "Section::Key"; the
        /// last duplicate wins; values trimmed. Never throws.
        ///
        /// ORDINAL AND CASE-SENSITIVE, because BepInEx is: `ConfigDefinition.Equals` is the
        /// two-argument `string.Equals` over a case-sensitive hash, so `beaconmaxcount` and
        /// `BeaconMaxCount` are two different keys there â€” one binds, the other is an orphan.
        /// All three sibling ports had this wrong until 2026-09-18, and in Ragnarok's Wrath it
        /// skipped a backfill and stamped, making a silent change permanent. The snapshot is the
        /// migration's whole model of the file, so it has to be BepInEx's model.
        /// </summary>
        public static Dictionary<string, string> ParseIni(IEnumerable<string> lines)
        {
            var into = new Dictionary<string, string>(StringComparer.Ordinal);
            if (lines == null) return into;

            string section = "";
            foreach (string rawLine in lines)
            {
                if (rawLine == null) continue;
                string line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                if (line[0] == '[' && line[line.Length - 1] == ']')
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();
                if (key.Length == 0) continue;

                into[Slot(section, key)] = value;
            }
            return into;
        }

        /// <summary>The stamped version, or 0 when absent or unparseable (a pre-migration file, by definition).</summary>
        public static int ReadVersion(Dictionary<string, string> snapshot)
        {
            if (snapshot != null && snapshot.TryGetValue(Slot(MetaSection, VersionKey), out string raw) &&
                int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int version))
            {
                return version;
            }
            return 0;
        }

        /// <summary>
        /// What migrating `snapshot` from `fileVersion` to <see cref="CurrentVersion"/> does. A missing
        /// snapshot (fresh install) or a file already current plans nothing. Never throws.
        /// </summary>
        public static MigrationPlan Plan(Dictionary<string, string> snapshot, int fileVersion) =>
            Plan(snapshot, fileVersion, CurrentVersion, Rebases);

        /// <summary>
        /// The same against a caller's table â€” the whole implementation; the overload above is one
        /// line of delegation, not a parallel path. Lets the harness reach rules one shipped rung
        /// cannot: two rungs naming one key, a rung the file has already passed, an empty table.
        /// </summary>
        public static MigrationPlan Plan(
            Dictionary<string, string> snapshot,
            int fileVersion,
            int toVersion,
            Dictionary<int, Rebase[]> rebases)
        {
            var plan = new MigrationPlan { FromVersion = fileVersion, ToVersion = toVersion };
            if (snapshot == null || snapshot.Count == 0) return plan;
            if (fileVersion >= toVersion) return plan;

            // A NEGATIVE stamp is a hand-edited or corrupt file and must not become a loop bound:
            // unclamped, the window counts up from it on the boot thread â€” eighteen seconds for
            // -2000000000, measured in the sibling repos. A file claiming to predate 0 IS version 0.
            int from = fileVersion < 0 ? 0 : fileVersion;

            // One slot, one decision: every rung is judged against the SAME unchanged snapshot, so a
            // key named on two rungs would otherwise be reported and reset twice. First match owns it.
            var decided = new HashSet<string>(StringComparer.Ordinal);

            for (int version = from + 1; version <= toVersion; version++)
            {
                if (rebases == null || !rebases.TryGetValue(version, out Rebase[] steps) || steps == null)
                    continue;

                foreach (Rebase r in steps)
                {
                    if (r == null) continue;
                    string slot = Slot(r.Section, r.Key);
                    if (decided.Contains(slot)) continue;
                    if (!snapshot.TryGetValue(slot, out string stored)) continue;

                    decided.Add(slot);

                    bool wasOldDefault = false;
                    if (r.OldDefaults != null)
                        foreach (string oldDefault in r.OldDefaults)
                            if (string.Equals(stored.Trim(), oldDefault, StringComparison.Ordinal)) { wasOldDefault = true; break; }

                    if (wasOldDefault) plan.ResetToDefault.Add(slot);
                    else plan.Kept.Add(new KeptSlot { Slot = slot, Value = stored });
                }
            }
            return plan;
        }

        /// <summary>
        /// One boot line: `"config: version 0 -> 1: 1 value moved to its new default (4 - The light.BeaconMaxCount)"`,
        /// or `"... nothing to migrate"`. Kept values are named with what they hold, so an admin can see
        /// the migration read them and left them. Never throws.
        /// </summary>
        public static string Describe(MigrationPlan plan)
        {
            if (plan == null) return "config: nothing to migrate";

            var parts = new List<string>();
            if (plan.ResetToDefault.Count > 0)
                parts.Add(Count(plan.ResetToDefault.Count, "value") + " moved to " +
                          (plan.ResetToDefault.Count == 1 ? "its new default" : "their new defaults") +
                          " (" + Names(plan.ResetToDefault) + ")");
            if (plan.Kept.Count > 0)
            {
                var kept = new List<string>();
                foreach (KeptSlot k in plan.Kept) kept.Add(Dotted(k.Slot) + "=" + k.Value);
                parts.Add(Count(plan.Kept.Count, "value") + " kept as yours (" + string.Join(", ", kept.ToArray()) + ")");
            }

            string clause = parts.Count == 0 ? "nothing to migrate" : string.Join("; ", parts.ToArray());
            return "config: version " + plan.FromVersion.ToString(CultureInfo.InvariantCulture) + " -> " +
                   plan.ToVersion.ToString(CultureInfo.InvariantCulture) + ": " + clause;
        }

        private static string Count(int n, string noun) =>
            n.ToString(CultureInfo.InvariantCulture) + " " + noun + (n == 1 ? "" : "s");

        private static string Names(List<string> slots)
        {
            var names = new List<string>();
            foreach (string s in slots) names.Add(Dotted(s));
            return string.Join(", ", names.ToArray());
        }

        private static string Dotted(string slot) => slot.Replace("::", ".");
    }
}
