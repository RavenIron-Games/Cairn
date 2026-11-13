using System;
using System.Collections.Generic;
using RavenIron.Cairn.Config;
using UnityEngine;

namespace RavenIron.Cairn.Net
{
    /// <summary>
    /// Client → server: "a stone was just placed or removed, or a sign written or removed, HERE"
    /// (0.12.0).
    ///
    /// The full world sweep notices a new cairn within about 20 seconds, and making it run more
    /// often would cost every server time on every lap. Instead the player's own game says where
    /// something changed, and the server checks just that spot (`LandmarkSystem.QuickCheck`), so
    /// the light comes on in a couple of seconds and an idle server does no extra work at all.
    ///
    /// THE SERVER WAITS A SECOND before checking. The message and the new stone's ZDO travel
    /// separately, and the message can arrive first; checking at once would find the pile one
    /// stone short. Several notices close together in time and place merge into one check, and
    /// a check that finds nothing new is tried once more three seconds later. Each sender is
    /// limited to a few notices a second, and the server runs at most one check per frame.
    ///
    /// Mixed versions degrade cleanly: an older server ignores the unknown message (vanilla drops
    /// an unregistered routed method silently), and an older player sends none, so their cairns
    /// simply wait for the sweep as before.
    /// </summary>
    public static class CairnNotice
    {
        public const string RpcName = "com.raveniron.cairn.notice1";

        private const float WaitSeconds = 1.0f;          // let the new stone's ZDO reach the server
        private const float RetrySeconds = 3.0f;         // second look when the first found nothing new
        private const float MergeMeters = 8f;            // notices closer than this share one check
        private const int MaxPending = 64;
        private const float SenderWindowSeconds = 1f;
        private const int MaxPerSenderPerWindow = 4;
        private const float SendGapSeconds = 0.25f;      // a sender's own repeats for one spot

        // The last notice this game SENT. A stack that collapses destroys its stones over a few
        // frames, each one a notice for the same spot; one message per spot every quarter second
        // is enough, because the server pushes its check back a second on every notice it merges.
        private static Vector3 _lastSentSpot;
        private static float _lastSentAt = float.NegativeInfinity;

        private struct Pending
        {
            public Vector3 Spot;
            public float Due;
            public bool Retried;
        }

        private static readonly List<Pending> _pending = new List<Pending>(16);
        private static readonly Dictionary<long, float> _windowStart = new Dictionary<long, float>();
        private static readonly Dictionary<long, int> _windowCount = new Dictionary<long, int>();
        private static ZRoutedRpc _registeredOn;

        /// <summary>Quick checks run this session, and how many changed something. For `cairn status`.</summary>
        public static int ChecksRun { get; private set; }
        public static int ChecksThatChanged { get; private set; }

        public static void EnsureRegistered()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null || ReferenceEquals(rpc, _registeredOn)) return;
            try
            {
                rpc.Register<Vector3>(RpcName, RPC_Notice);
                _registeredOn = rpc;
            }
            catch (Exception ex)
            {
                Cairn.Log.LogWarning($"CairnNotice: register failed: {ex.Message}");
                _registeredOn = rpc;
            }
        }

        /// <summary>
        /// This game placed a stone or wrote a sign at <paramref name="spot"/>. On the authority
        /// (single player, a host) it is queued directly; on a joined player's game it goes to the
        /// server. `ZNet.GetServerPeer` and `ZNetPeer.m_uid` are public; the routed RPC's own
        /// GetServerPeerID is private at runtime and is never named.
        /// </summary>
        public static void Notify(Vector3 spot)
        {
            try
            {
                if (!ModConfig.Enabled.Value) return;

                if (Cairn.IsSimulationAuthority())
                {
                    Enqueue(spot, Time.realtimeSinceStartup);
                    return;
                }

                ZRoutedRpc rpc = ZRoutedRpc.instance;
                ZNet znet = ZNet.instance;
                ZNetPeer server = znet != null ? znet.GetServerPeer() : null;
                if (rpc == null || server == null) return;

                float now = Time.realtimeSinceStartup;
                if (now - _lastSentAt < SendGapSeconds &&
                    Core.LedgerUpdate.DistanceXZ(spot, _lastSentSpot) <= MergeMeters) return;
                _lastSentSpot = spot;
                _lastSentAt = now;

                rpc.InvokeRoutedRPC(server.m_uid, RpcName, spot);
            }
            catch (Exception ex)
            {
                // Never let a notice break placing a stone: the sweep still finds the cairn.
                Cairn.Log.LogWarning($"CairnNotice: send failed: {ex.Message}");
            }
        }

        private static void RPC_Notice(long sender, Vector3 spot)
        {
            if (!Cairn.IsSimulationAuthority()) return;
            if (!ModConfig.QuickChecks.Value) return;

            float now = Time.realtimeSinceStartup;
            if (!_windowStart.TryGetValue(sender, out float start) || now - start > SenderWindowSeconds)
            {
                _windowStart[sender] = now;
                _windowCount[sender] = 0;
            }
            if (++_windowCount[sender] > MaxPerSenderPerWindow) return;   // the sweep will still find it

            Enqueue(spot, now);
        }

        private static void Enqueue(Vector3 spot, float now)
        {
            if (!ModConfig.QuickChecks.Value) return;

            // A pile is built a stone at a time: notices close together become ONE check, a second
            // after the latest stone.
            for (int i = 0; i < _pending.Count; i++)
            {
                if (Core.LedgerUpdate.DistanceXZ(_pending[i].Spot, spot) > MergeMeters) continue;
                _pending[i] = new Pending { Spot = spot, Due = now + WaitSeconds, Retried = false };
                return;
            }

            if (_pending.Count >= MaxPending) return;
            _pending.Add(new Pending { Spot = spot, Due = now + WaitSeconds });
        }

        /// <summary>
        /// Authority, every frame: run at most ONE due check. Called from CairnTick.
        /// </summary>
        public static void Process(float now)
        {
            if (_pending.Count == 0) return;

            int due = -1;
            for (int i = 0; i < _pending.Count; i++)
                if (_pending[i].Due <= now) { due = i; break; }
            if (due < 0) return;

            Pending item = _pending[due];
            _pending.RemoveAt(due);

            Systems.LandmarkSystem system = Systems.LandmarkSystem.Instance;
            if (system == null || !system.Enabled) return;

            bool changed;
            try
            {
                changed = system.QuickCheck(item.Spot);
            }
            catch (Exception ex)
            {
                Cairn.Log.LogWarning($"CairnNotice: quick check threw: {ex.Message}");
                return;
            }

            ChecksRun++;
            if (changed)
            {
                ChecksThatChanged++;
                LandmarkSync.RequestBroadcast();
            }
            else if (!item.Retried && _pending.Count < MaxPending)
            {
                // The stone may simply not have reached the server yet. One more look.
                _pending.Add(new Pending { Spot = item.Spot, Due = now + RetrySeconds, Retried = true });
            }
        }

        public static void ResetForWorldChange()
        {
            _pending.Clear();
            _windowStart.Clear();
            _windowCount.Clear();
            _lastSentAt = float.NegativeInfinity;
            ChecksRun = 0;
            ChecksThatChanged = 0;
        }
    }
}
