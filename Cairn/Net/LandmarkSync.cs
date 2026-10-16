using System;
using System.Collections.Generic;
using RavenIron.Cairn.Core;
using UnityEngine;

namespace RavenIron.Cairn.Net
{
    /// <summary>
    /// Server → client: where the lit cairns are.
    ///
    /// The beacon is drawn client-side from this cache and never from a networked object,
    /// which is the locked decision and the reason it can be seen at all. At beacon range the
    /// cairn's own ZDOs are not instantiated on the client — `ZDO.Distant` comes off the
    /// prefab and vanilla stone is near-only — so anything that waited for the object to load
    /// would light up only once you had already arrived.
    ///
    /// ABSOLUTE SNAPSHOTS, BROADCAST, UNCONDITIONAL. Every push carries every lit cairn in
    /// the world, sent to everyone on a fixed cadence whether or not anything changed. Deltas
    /// drift forever on one dropped packet, and a per-peer ring — which is right for zone
    /// state — is exactly wrong here: the whole point of a beacon is the one you can see from
    /// somewhere else. A hundred cairns is a few kilobytes.
    ///
    /// The RPC name carries a version suffix. A version-skewed pair then no-ops cleanly
    /// instead of misparsing a payload whose shape moved.
    /// </summary>
    public static class LandmarkSync
    {
        public const string RpcName = "com.raveniron.cairn.beacons1";

        /// <summary>One lit cairn, as a client knows it.</summary>
        public struct Beacon
        {
            public LandmarkKey Key;
            public Vector3 Light;
            public string Name;
        }

        private static readonly List<Beacon> _cache = new List<Beacon>(32);
        private static ZRoutedRpc _registeredOn;

        /// <summary>Bumped on every received push, so a renderer can rebuild only on change.</summary>
        public static int Revision { get; private set; }

        /// <summary>
        /// The lit cairns this machine knows about. The authority reads its own store — a
        /// listen host must not wait on a network round-trip to itself — and everyone else
        /// reads the synced cache.
        /// </summary>
        public static List<Beacon> Current()
        {
            if (!Persistence.IsLoaded) return _cache;

            var live = new List<Beacon>(16);
            foreach (Landmark l in LandmarkStore.Snapshot())
            {
                if (!l.HasPile) continue;
                live.Add(new Beacon { Key = l.Key, Light = l.Light, Name = l.Name });
            }
            return live;
        }

        /// <summary>
        /// The world is ending: drop the last world's beacons so they cannot be drawn in the
        /// next world, or at the same coordinates on the next server.
        /// </summary>
        public static void ResetForWorldChange()
        {
            // _registeredOn is left alone: every session builds a new ZRoutedRpc, so the
            // reference check re-registers on the next world by itself, and re-registering on
            // the same instance would throw on the duplicate name.
            _cache.Clear();
            Revision++;
            LastLit = -1;
            LastSent = -1;
            _warnedCap = false;
        }

        public static void EnsureRegistered()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null || ReferenceEquals(rpc, _registeredOn)) return;

            try
            {
                rpc.Register<ZPackage>(RpcName, RPC_Beacons);
                _registeredOn = rpc;
                _cache.Clear();
                Revision++;
            }
            catch (Exception ex)
            {
                Cairn.Log.LogWarning($"LandmarkSync: register failed: {ex.Message}");
                _registeredOn = rpc;   // do not retry every tick against a broken registration
            }
        }

        /// <summary>Lit cairns in the ledger at the last broadcast. -1 before the first.</summary>
        public static int LastLit { get; private set; } = -1;

        /// <summary>How many of those the last broadcast actually carried.</summary>
        public static int LastSent { get; private set; } = -1;

        private static bool _warnedCap;

        /// <summary>
        /// Authority-side: broadcast the lit cairns to everyone — all of them, or, past
        /// `BeaconMaxCount`, the ones nearest a player (see BeaconSelection for why the
        /// oldest-first cut this replaced left every NEW cairn dark).
        ///
        /// The packet's shape is unchanged from 0.9.1, so a 0.9.1 client reads a 0.9.2
        /// server's broadcast exactly as before. Only the choice of WHICH cairns moved.
        /// </summary>
        public static void Broadcast(int maxBeacons)
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null) return;

            try
            {
                var pkg = new ZPackage();
                var all = new List<Landmark>(16);
                foreach (Landmark l in LandmarkStore.Snapshot())
                    if (l.HasPile) all.Add(l);

                List<Landmark> lit = all.Count > maxBeacons
                    ? BeaconSelection.Choose(all, PlayerPositions(), maxBeacons)
                    : all;

                LastLit = all.Count;
                LastSent = lit.Count;

                if (all.Count > maxBeacons && !_warnedCap)
                {
                    // Once per session. The cut used to be silent, and silence is what cost a
                    // player an hour of waiting beside a cairn that was never going to light.
                    _warnedCap = true;
                    Cairn.Log.LogWarning(
                        $"LandmarkSync: {all.Count} cairns are lit and BeaconMaxCount is {maxBeacons} — " +
                        $"each broadcast carries the {maxBeacons} nearest a player. Cairns further " +
                        "out stay unlit for clients until someone comes near. Raise BeaconMaxCount " +
                        "(up to 500, on the server) to send more. `cairn status` shows the numbers.");
                }

                pkg.Write(lit.Count);
                foreach (Landmark l in lit)
                {
                    pkg.Write(l.Key.X);
                    pkg.Write(l.Key.Y);
                    pkg.Write(l.Key.Z);
                    pkg.Write(l.Light.x);
                    pkg.Write(l.Light.y);
                    pkg.Write(l.Light.z);
                    pkg.Write(l.Name ?? "");
                }

                rpc.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcName, pkg);
            }
            catch (Exception ex)
            {
                Cairn.Log.LogWarning($"LandmarkSync: broadcast failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Where the players are, as the authority knows it: every ready peer's reference
        /// position (what vanilla itself loads zones around), plus the host's own on a listen
        /// server. `ZNet.GetPeers`, `ZNetPeer.m_refPos` and `ZNet.GetReferencePosition` are
        /// all public in the REAL assembly (checked on the 2026-10-15 build), not only under
        /// publicization, so they are called directly.
        ///
        /// A dedicated server's own reference position is not a player and is left out: it
        /// would rank cairns by their distance from wherever the server happens to point.
        /// </summary>
        private static List<Vector3> PlayerPositions()
        {
            var positions = new List<Vector3>(8);
            ZNet znet = ZNet.instance;
            if (znet == null) return positions;

            foreach (ZNetPeer peer in znet.GetPeers())
                if (peer != null && peer.IsReady()) positions.Add(peer.m_refPos);

            if (!znet.IsDedicated()) positions.Add(znet.GetReferencePosition());
            return positions;
        }

        /// <summary>
        /// Receiver. Refuses its own broadcast on a listen host, which would otherwise
        /// overwrite a cache the authority reads straight from the store anyway.
        /// </summary>
        private static void RPC_Beacons(long sender, ZPackage pkg)
        {
            if (Persistence.IsLoaded) return;   // authority: the store is the truth here

            // Only the server sends beacons. The server already drops client-sent pushes
            // before relaying them (Patch_RoutedRpcGuard); this is the client's own check, and
            // it also refuses anything that arrives before the server connection is up.
            if (!FromServer(sender))
            {
                WarnRejected($"sender {sender} is not the server");
                return;
            }

            try
            {
                var received = new List<Beacon>(16);
                int count = pkg.ReadInt();

                // A real server never sends more than BeaconMaxCount allows. A count past that
                // is not a world, it is an attack or a corrupt packet, and neither is drawn.
                if (count < 0 || count > MaxBeaconsPerPush)
                {
                    WarnRejected($"{count} beacons in one push (the most a server sends is {MaxBeaconsPerPush})");
                    return;
                }

                for (int i = 0; i < count; i++)
                {
                    int kx = pkg.ReadInt();
                    int ky = pkg.ReadInt();
                    int kz = pkg.ReadInt();
                    float lx = pkg.ReadSingle();
                    float ly = pkg.ReadSingle();
                    float lz = pkg.ReadSingle();
                    string name = pkg.ReadString() ?? "";

                    // The ledger never stores a longer name, so a longer one did not come from it.
                    if (name.Length > Landmark.MaxNameLength) name = name.Substring(0, Landmark.MaxNameLength);

                    received.Add(new Beacon
                    {
                        Key = new LandmarkKey(kx, ky, kz),
                        Light = new Vector3(lx, ly, lz),
                        Name = name
                    });
                }

                _cache.Clear();
                _cache.AddRange(received);
                Revision++;
            }
            catch (Exception ex)
            {
                // A malformed payload must not poison a cache that is about to be replaced
                // wholesale by the next push anyway.
                Cairn.Log.LogWarning($"LandmarkSync: receive failed: {ex.Message}");
            }
        }

        /// <summary>The upper bound of the BeaconMaxCount config range.</summary>
        private const int MaxBeaconsPerPush = 500;

        private static float _lastRejectWarn = -999f;

        /// <summary>
        /// Is this routed sender the server we are connected to? `GetServerPeer` is public;
        /// `ZRoutedRpc.GetServerPeerID` reads public in the publicized assembly but is private
        /// at runtime, so it is never named here.
        /// </summary>
        private static bool FromServer(long sender)
        {
            ZNet znet = ZNet.instance;
            if (znet == null) return false;

            ZNetPeer server = znet.GetServerPeer();
            return server != null && server.m_uid == sender;
        }

        private static void WarnRejected(string why)
        {
            float now = Time.realtimeSinceStartup;
            if (now - _lastRejectWarn < 60f) return;
            _lastRejectWarn = now;
            Cairn.Log.LogWarning($"LandmarkSync: ignored a beacon push — {why}.");
        }
    }
}
