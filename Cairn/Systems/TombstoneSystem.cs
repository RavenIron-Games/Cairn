using System;
using System.Collections.Generic;
using RavenIron.Cairn.Config;
using RavenIron.Cairn.Core;
using UnityEngine;

namespace RavenIron.Cairn.Systems
{
    /// <summary>
    /// Finds every player tombstone in the world, so the dead can see a red light over their own.
    ///
    /// Authority only, and only while `EnableTombstoneLights` is on. Reads ZDOs, like the cairn
    /// sweep, so a tombstone far from anyone is still found. Nothing is stored: a tombstone keeps
    /// itself in the world save, and the list is rebuilt from the world on every pass.
    ///
    /// THE PREFAB NAME IS READ FROM THE GAME, not written here. `Player.m_tombstone` is the
    /// prefab vanilla instantiates on death (`Player.CreateTombStone`, 1.0.20), so its name is
    /// the right answer even if a patch or a mod renames it. "Player_tombstone" is only the
    /// fallback, and the boot line says which was used.
    /// </summary>
    public class TombstoneSystem : IWorldSystem
    {
        public string Name => "TombstoneSystem";
        public bool Enabled => ModConfig.EnableTombstoneLights.Value;
        public float IntervalSeconds => 5f;

        private const string FallbackPrefab = "Player_tombstone";

        private string _prefab;
        private readonly List<ZDO> _found = new List<ZDO>(16);
        private int _sweepIndex;

        /// <summary>
        /// The tombstones found by the last complete pass. Read by the broadcast, and by a host's
        /// or single player's own renderer, which must not wait on a network round trip to itself.
        /// </summary>
        public static List<TombstoneLights.Grave> Latest { get; private set; } = new List<TombstoneLights.Grave>();

        /// <summary>When <see cref="Latest"/> was last rebuilt, in realtime seconds. -1 before the first pass.</summary>
        public static float LatestAt { get; private set; } = -1f;

        public void Initialise()
        {
            // Once per world: nothing from the last world may be lit in this one.
            _prefab = null;
            _found.Clear();
            _sweepIndex = 0;
            Latest = new List<TombstoneLights.Grave>();
            LatestAt = -1f;
        }

        /// <summary>The world is ending. Drop the last world's tombstones.</summary>
        public static void ResetForWorldChange()
        {
            Latest = new List<TombstoneLights.Grave>();
            LatestAt = -1f;
        }

        public void Tick(float deltaSeconds)
        {
            Game game = Game.instance;
            if (game != null && game.IsShuttingDown()) return;

            ZDOMan man = ZDOMan.instance;
            if (man == null) return;

            if (_prefab == null)
            {
                _prefab = ResolvePrefab(out string how);
                if (_prefab == null) return;   // ZNetScene not up yet: try again next pass
                Cairn.Log.LogInfo($"[{Name}] tombstone lights ON — looking for '{_prefab}' ({how}).");
            }

            try
            {
                bool done = false;
                while (!done)
                    done = man.GetAllZDOsWithPrefabIterative(_prefab, _found, ref _sweepIndex);
            }
            catch (Exception ex)
            {
                Cairn.Log.LogWarning($"[{Name}] sweep failed: {ex.Message}");
                _found.Clear();
                _sweepIndex = 0;
                return;
            }

            var graves = new List<TombstoneLights.Grave>(_found.Count);
            for (int i = 0; i < _found.Count; i++)
            {
                ZDO zdo = _found[i];
                if (zdo == null || !zdo.IsValid()) continue;

                graves.Add(new TombstoneLights.Grave
                {
                    Owner = zdo.GetLong(ZDOVars.s_owner, 0L),
                    TimeOfDeath = zdo.GetLong(ZDOVars.s_timeOfDeath, 0L),
                    Position = zdo.GetPosition(),
                });
            }

            _found.Clear();
            _sweepIndex = 0;

            if (graves.Count != Latest.Count && ModConfig.VerboseLogging.Value)
                Cairn.Log.LogInfo($"[{Name}] {graves.Count} tombstone(s) in the world (was {Latest.Count}).");

            Latest = graves;
            LatestAt = Time.realtimeSinceStartup;
        }

        /// <summary>
        /// The tombstone prefab's name, from the Player prefab's own `m_tombstone` field. Null while
        /// ZNetScene is not ready yet. `ZNetScene.GetPrefab(string)` and `Player.m_tombstone` are
        /// both public in the real 1.0.20 assembly.
        /// </summary>
        private static string ResolvePrefab(out string how)
        {
            how = "";
            ZNetScene scene = ZNetScene.instance;
            if (scene == null) return null;

            try
            {
                GameObject playerPrefab = scene.GetPrefab("Player");
                Player player = playerPrefab != null ? playerPrefab.GetComponent<Player>() : null;
                if (player != null && player.m_tombstone != null)
                {
                    how = "read from the Player prefab";
                    return player.m_tombstone.name;
                }
            }
            catch (Exception ex)
            {
                Cairn.Log.LogWarning($"[TombstoneSystem] could not read Player.m_tombstone: {ex.Message}");
            }

            how = "FALLBACK name: the Player prefab did not say";
            return FallbackPrefab;
        }
    }
}
