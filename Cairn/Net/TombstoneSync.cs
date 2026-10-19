using System;
using System.Collections.Generic;
using RavenIron.Cairn.Config;
using RavenIron.Cairn.Core;
using UnityEngine;

namespace RavenIron.Cairn.Net
{
    /// <summary>
    /// Server → client: where the tombstones are, so each player can light their own.
    ///
    /// A SEPARATE RPC from the cairn beacons, on purpose. The beacon packet's shape is unchanged,
    /// so an older Cairn still reads a 0.11.0 server's cairns, and an older game that has never
    /// registered this name simply ignores it: `ZRoutedRpc.HandleRoutedRPC` drops an unknown
    /// method hash without a word (read out of 1.0.20).
    ///
    /// Sent only while `EnableTombstoneLights` is on. Off, the server sends nothing at all, and
    /// a game that stops hearing lets its lights go stale and out (TombstoneLights.Stale).
    /// Absolute snapshots on the beacon cadence, for the same reason as the beacons: a dropped
    /// packet heals itself with the next.
    /// </summary>
    public static class TombstoneSync
    {
        public const string RpcName = "com.raveniron.cairn.tombstones1";

        private static readonly List<TombstoneLights.Grave> _cache = new List<TombstoneLights.Grave>(8);
        private static float _received = -1f;
        private static ZRoutedRpc _registeredOn;

        /// <summary>Bumped on every received push, so a renderer can rebuild only on change.</summary>
        public static int Revision { get; private set; }

        /// <summary>How many tombstones the last push carried, all owners. -1 before the first.</summary>
        public static int LastReceivedCount { get; private set; } = -1;

        /// <summary>How many the last broadcast sent. -1 before the first.</summary>
        public static int LastSent { get; private set; } = -1;

        public static void EnsureRegistered()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null || ReferenceEquals(rpc, _registeredOn)) return;

            try
            {
                rpc.Register<ZPackage>(RpcName, RPC_Tombstones);
                _registeredOn = rpc;
                _cache.Clear();
                _received = -1f;
                Revision++;
            }
            catch (Exception ex)
            {
                Cairn.Log.LogWarning($"TombstoneSync: register failed: {ex.Message}");
                _registeredOn = rpc;
            }
        }

        /// <summary>
        /// THIS player's tombstones, to light. Empty when tombstone lights are off, when the last
        /// push has gone stale, or when this game does not yet know who its player is.
        ///
        /// The authority reads the sweep directly; everyone else reads the synced cache.
        /// </summary>
        public static List<TombstoneLights.Grave> Mine()
        {
            long me = LocalPlayerId();
            float now = Time.realtimeSinceStartup;

            if (Cairn.IsSimulationAuthority())
            {
                if (!ModConfig.EnableTombstoneLights.Value) return new List<TombstoneLights.Grave>();
                return TombstoneLights.Mine(Systems.TombstoneSystem.Latest, me);
            }

            if (TombstoneLights.Stale(_received, now, ModConfig.BeaconSyncSeconds.Value))
                return new List<TombstoneLights.Grave>();
            return TombstoneLights.Mine(_cache, me);
        }

        /// <summary>
        /// The local player's profile ID, the same number vanilla writes into a tombstone. 0 when
        /// there is no profile (a dedicated server, or before a character is loaded).
        /// `Game.instance`, `Game.GetPlayerProfile` and `PlayerProfile.GetPlayerID` are public in
        /// the real 1.0.20 assembly.
        /// </summary>
        public static long LocalPlayerId()
        {
            try
            {
                Game game = Game.instance;
                PlayerProfile profile = game != null ? game.GetPlayerProfile() : null;
                return profile != null ? profile.GetPlayerID() : 0L;
            }
            catch (Exception)
            {
                return 0L;
            }
        }

        /// <summary>Authority side: every tombstone to everyone. Each game keeps only its own.</summary>
        public static void Broadcast(List<TombstoneLights.Grave> graves)
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null || graves == null) return;

            try
            {
                int n = Math.Min(graves.Count, TombstoneLights.MaxPerPush);
                var pkg = new ZPackage();
                pkg.Write(n);
                for (int i = 0; i < n; i++)
                {
                    TombstoneLights.Grave g = graves[i];
                    pkg.Write(g.Owner);
                    pkg.Write(g.TimeOfDeath);
                    pkg.Write(g.Position.x);
                    pkg.Write(g.Position.y);
                    pkg.Write(g.Position.z);
                }

                LastSent = n;
                rpc.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcName, pkg);
            }
            catch (Exception ex)
            {
                Cairn.Log.LogWarning($"TombstoneSync: broadcast failed: {ex.Message}");
            }
        }

        private static void RPC_Tombstones(long sender, ZPackage pkg)
        {
            // A listen host reads its own sweep; its own broadcast arriving back changes nothing.
            if (Cairn.IsSimulationAuthority()) return;
            if (!FromServer(sender)) return;

            try
            {
                int count = pkg.ReadInt();
                if (count < 0 || count > TombstoneLights.MaxPerPush)
                {
                    Cairn.Log.LogWarning($"TombstoneSync: ignored a push of {count} tombstones.");
                    return;
                }

                var received = new List<TombstoneLights.Grave>(count);
                for (int i = 0; i < count; i++)
                {
                    long owner = pkg.ReadLong();
                    long died = pkg.ReadLong();
                    float x = pkg.ReadSingle();
                    float y = pkg.ReadSingle();
                    float z = pkg.ReadSingle();
                    received.Add(new TombstoneLights.Grave { Owner = owner, TimeOfDeath = died, Position = new Vector3(x, y, z) });
                }

                _cache.Clear();
                _cache.AddRange(received);
                _received = Time.realtimeSinceStartup;
                LastReceivedCount = count;
                Revision++;
            }
            catch (Exception ex)
            {
                Cairn.Log.LogWarning($"TombstoneSync: receive failed: {ex.Message}");
            }
        }

        public static void ResetForWorldChange()
        {
            _cache.Clear();
            _received = -1f;
            LastReceivedCount = -1;
            LastSent = -1;
            Revision++;
        }

        private static bool FromServer(long sender)
        {
            ZNet znet = ZNet.instance;
            if (znet == null) return false;
            ZNetPeer server = znet.GetServerPeer();
            return server != null && server.m_uid == sender;
        }
    }
}
