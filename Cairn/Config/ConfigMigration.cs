// Ported 2026-10-16 from FireFront's Config/ConfigMigration.cs (from Valkyrie's Cargo's, itself
// from WingsoftheValkyrie's, Wu'barrk, RGlabs84) - the family's shape - minus the retirement
// machinery Cairn has no rung for, plus two fixes found after FireFront's copy was written.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx.Configuration;
using RavenIron.Cairn.Core;

namespace RavenIron.Cairn.Config
{
    /// <summary>
    /// The engine-facing half of the config migration; the decisions are <see cref="ConfigLedger"/>
    /// (PURE, off-game). Snapshot the raw file BEFORE any bind, a stamped layout version, a backup
    /// beside the file, and a failed migration never stops the mod loading.
    ///
    /// <see cref="Begin"/> runs before `ModConfig.Bind`; <see cref="Finish"/> after it, so every
    /// entry it might reset exists.
    /// </summary>
    public static class ConfigMigration
    {
        /// <summary>
        /// What Begin decided, so Finish can tell "nothing to do" from "tried and could not tell".
        /// Stamping a version the file never actually reached makes the failure permanent — the
        /// next boot sees a current file and never retries. Only Failed blocks the stamp.
        /// </summary>
        private enum MigrationState { Fresh, AlreadyCurrent, Planned, Failed }

        private static ConfigLedger.MigrationPlan _plan;
        private static string _path;
        private static MigrationState _state = MigrationState.Fresh;
        private static bool _backedUp;

        /// <summary>
        /// Steps Apply REFUSED this boot. Counted because LastSummary is written in Begin from the
        /// plan's INTENT, before any step runs, and `cairn status` reads it back verbatim — a
        /// refusal has to be folded back in or the one line an owner reads is confidently wrong.
        /// </summary>
        private static int _refused;

        /// <summary>This boot's migration line, for `cairn status`. Empty when nothing ran.</summary>
        public static string LastSummary { get; private set; } = "";

        public static string StatusLine() =>
            string.IsNullOrEmpty(LastSummary)
                ? "no migration this boot (file already at layout version " +
                  ConfigLedger.CurrentVersion.ToString(CultureInfo.InvariantCulture) + ", or a first install)"
                : LastSummary;

        public static void Begin(ConfigFile cfg)
        {
            _plan = null;
            _path = null;
            _state = MigrationState.Fresh;
            _backedUp = false;
            _refused = 0;
            LastSummary = "";

            try
            {
                if (cfg == null) return;
                string path = cfg.ConfigFilePath;
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;   // first install: new defaults bind on their own

                Dictionary<string, string> snapshot = ConfigLedger.ParseIni(ReadLinesShared(path));
                int fileVersion = ConfigLedger.ReadVersion(snapshot);
                if (fileVersion >= ConfigLedger.CurrentVersion) { _state = MigrationState.AlreadyCurrent; return; }

                _path = path;
                _plan = ConfigLedger.Plan(snapshot, fileVersion);
                _state = MigrationState.Planned;
                LastSummary = ConfigLedger.Describe(_plan);

                bool touchesFile = _plan.ResetToDefault.Count > 0;
                // Only a plan that changes a value needs the previous file kept. A stamp-only boot
                // leaves every value exactly as it was.
                _backedUp = !touchesFile || Backup(path, fileVersion);

                if (!touchesFile)
                    Cairn.Log.LogInfo(LastSummary + " (stamping the layout version)");
                else if (_backedUp)
                    Cairn.Log.LogWarning(LastSummary + " (the previous file is backed up beside it, .v" +
                                         fileVersion.ToString(CultureInfo.InvariantCulture) + ".bak)");
                else
                    Cairn.Log.LogWarning(LastSummary + " (no backup could be written, so nothing is changed and " +
                                         "the version is left unstamped; this migration retries next boot)");
            }
            catch (Exception ex)
            {
                // A failed migration must never stop the mod loading. Every value binds exactly as it
                // always did, and Finish neither changes nor stamps anything.
                _state = MigrationState.Failed;
                Cairn.Log.LogError("Config migration could not start. Every value binds as it always did and " +
                                   "the migration retries on the next boot. Reason: " + ex);
            }
        }

        /// <summary>
        /// After every bind: move the slots found at an old default, stamp the version, save.
        ///
        /// The stamp is gated on Begin having succeeded and, when the plan changes a value, on the
        /// backup having landed. Skipping it is always safe — an unstamped file is exactly a
        /// pre-migration file, and the next boot retries from scratch.
        /// </summary>
        public static void Finish(ConfigFile cfg, ConfigEntry<int> versionEntry)
        {
            bool attempted = false, safe = false, clean = true;
            try
            {
                attempted = _path != null;
                safe = _state != MigrationState.Failed && (!attempted || _backedUp);

                if (_plan != null && safe && cfg != null) clean = Apply(cfg, _plan);

                if (_state == MigrationState.Failed)
                    Cairn.Log.LogWarning("Config migration did not run this boot; every value is as it was, " +
                                         "and it will be migrated on the next successful boot.");

                if (versionEntry != null)
                {
                    // THE STAMP ONLY EVER GOES UP. A higher stamp was written by a newer build whose
                    // rungs already ran; dragging it down on a rollback makes the next upgrade replay
                    // them over values the owner has since chosen, which a rebase cannot tell apart
                    // from the old default they happen to equal.
                    if (safe && clean)
                    {
                        if (versionEntry.Value < ConfigLedger.CurrentVersion)
                            versionEntry.Value = ConfigLedger.CurrentVersion;
                    }
                    else
                        Cairn.Log.LogWarning("Config migration did not finish cleanly; ConfigVersion is left " +
                                             "unstamped so the next boot retries instead of treating this one as done.");
                }

                if (cfg != null && !TrySave(cfg))
                {
                    clean = false;
                    Cairn.Log.LogError("Config migration could not save the config file. It has been put back " +
                                       "exactly as it was, and the migration retries next boot.");
                }
            }
            catch (Exception ex)
            {
                clean = false;
                Cairn.Log.LogError("Config migration could not finish - check the backup beside your config file. Reason: " + ex);
            }
            finally
            {
                if (_refused > 0)
                    LastSummary += " - but " + _refused.ToString(CultureInfo.InvariantCulture) +
                                   " step(s) were REFUSED; see the warnings in the log";
                if (_state == MigrationState.Failed)
                    LastSummary = "migration FAILED this boot; every value is as it was and it retries next boot (see the error in the log)";
                else if (attempted && !(safe && clean))
                    LastSummary += " - NOT STAMPED; the next boot retries this migration";

                _plan = null;
                _path = null;
                _state = MigrationState.Fresh;
                _backedUp = false;
                _refused = 0;
            }
        }

        /// <summary>
        /// Apply a plan to the bound entries. Returns false only for a failure that could succeed
        /// next time; a ledger row naming a key this build does not bind cannot, so it warns by name
        /// and proceeds rather than withholding the stamp and re-running every boot forever.
        /// </summary>
        internal static bool Apply(ConfigFile cfg, ConfigLedger.MigrationPlan plan)
        {
            if (cfg == null || plan == null) return true;

            foreach (string slot in plan.ResetToDefault)
            {
                ConfigEntryBase entry = Lookup(cfg, slot);
                if (entry == null)
                {
                    _refused++;
                    Cairn.Log.LogWarning("Config migration wanted to touch " + slot + " but this build binds no " +
                                         "such key. Nothing was changed for it. This is a bug in ConfigLedger, not in your file.");
                    continue;
                }

                entry.BoxedValue = entry.DefaultValue;

                // READ IT BACK. ConfigEntry<T>'s setter CLAMPS into the entry's AcceptableValueRange
                // rather than refusing, so a write can land on something other than what was asked
                // for and still look like it moved. Found in Ragnarok's Wrath's migration, 2026-09-18.
                if (!Equals(entry.BoxedValue, entry.DefaultValue))
                {
                    _refused++;
                    Cairn.Log.LogWarning("Config migration set " + slot + " to its default " + entry.DefaultValue +
                                         " but it reads back as " + entry.BoxedValue + ".");
                }
            }
            return true;
        }

        /// <summary>The bound entry for a "Section::Key" slot, or null when this build does not bind it.</summary>
        private static ConfigEntryBase Lookup(ConfigFile cfg, string slot)
        {
            if (!ConfigLedger.SplitSlot(slot, out string section, out string key)) return null;

            // ConfigDefinition's constructor THROWS on whitespace at the ends and on = \n \t \ " ' [ ],
            // so a malformed ledger row must cost only its own step.
            try
            {
                var def = new ConfigDefinition(section, key);
                return cfg.ContainsKey(def) ? cfg[def] : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Save, and put the file back if the save fails part-way.
        ///
        /// BepInEx's `ConfigFile.Save` is NOT atomic: it opens a StreamWriter with append:false,
        /// which EMPTIES the file before writing, so a throw part-way (a full disk, a transient I/O
        /// error) leaves a truncated file rather than the untouched one a try/catch suggests. Found
        /// by Ragnarok's Wrath's 0.28.0 review; FireFront's copy of this file predates it.
        /// </summary>
        private static bool TrySave(ConfigFile cfg)
        {
            string path = cfg.ConfigFilePath;
            byte[] before = null;
            bool existed = false;
            try
            {
                existed = !string.IsNullOrEmpty(path) && File.Exists(path);
                if (existed) before = File.ReadAllBytes(path);
            }
            catch
            {
                // Cannot read it to protect it; saving would risk the only copy, so do not.
                return false;
            }

            try
            {
                cfg.Save();
                return true;
            }
            catch (Exception ex)
            {
                Cairn.Log.LogWarning("Saving the config file failed: " + ex.Message);
                try
                {
                    if (existed) File.WriteAllBytes(path, before);
                    else if (File.Exists(path)) File.Delete(path);
                }
                catch (Exception restore)
                {
                    Cairn.Log.LogError("...and putting it back failed too (" + restore.Message +
                                       "). The backup beside it holds the file as it was before this boot.");
                }
                return false;
            }
        }

        /// <summary>
        /// The raw file, opened with FileShare.ReadWrite so a config manager, editor or sync tool
        /// holding it open does not fail the migration the way File.ReadAllLines would.
        /// </summary>
        private static List<string> ReadLinesShared(string path)
        {
            var lines = new List<string>();
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            {
                string line;
                while ((line = reader.ReadLine()) != null) lines.Add(line);
            }
            return lines;
        }

        /// <summary>
        /// Copies `path` beside itself as `path.v{from}.bak`, never overwriting a different copy (a
        /// half-finished earlier run's would be someone's only clean one) — falls back to a
        /// timestamped name instead. True when a copy now exists for this migration. Never throws.
        /// </summary>
        internal static bool Backup(string path, int fromVersion)
        {
            try
            {
                string v = fromVersion.ToString(CultureInfo.InvariantCulture);
                string bak = path + ".v" + v + ".bak";
                if (File.Exists(bak))
                {
                    if (BytesEqual(bak, path)) return true;
                    bak = path + ".v" + v + "." +
                          DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + ".bak";
                }
                File.Copy(path, bak, overwrite: false);
                return true;
            }
            catch (Exception ex)
            {
                Cairn.Log.LogError("Could not back up the config before migrating (" + ex.Message + "). " +
                                   "Nothing is changed and the version is left unstamped so this retries next boot.");
                return false;
            }
        }

        private static bool BytesEqual(string a, string b)
        {
            try
            {
                byte[] x = File.ReadAllBytes(a), y = File.ReadAllBytes(b);
                if (x.Length != y.Length) return false;
                for (int i = 0; i < x.Length; i++) if (x[i] != y[i]) return false;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
