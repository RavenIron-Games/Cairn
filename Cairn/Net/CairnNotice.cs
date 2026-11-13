using System;
using System.Collections.Generic;
using RavenIron.Cairn.Config;
using UnityEngine;

namespace RavenIron.Cairn.Net
{
    /// <summary>
    /// Client → server: "a stone was just placed, or a sign written, HERE" (0.12.0). Removals
    /// need no message: the authority watches vanilla's own record of destroyed objects
    /// (<see cref="EnsureWatching"/>).
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
        public static void Notify(Vector3 spot, string why = "change")
        {
            try
            {
                if (!ModConfig.Enabled.Value) return;

                if (Cairn.IsSimulationAuthority())
                {
                    if (ModConfig.VerboseLogging.Value)
                        Cairn.Log.LogInfo($"CairnNotice: {why} at ({spot.x:F0}, {spot.z:F0}) queued here " +
                                          $"[{DateTime.Now:HH:mm:ss.fff}]");
                    Enqueue(spot, Time.realtimeSinceStartup);
                    return;
                }

                ZRoutedRpc rpc = ZRoutedRpc.instance;
                ZNet znet = ZNet.instance;
                ZNetPeer server = znet != null ? znet.GetServerPeer() : null;
                if (rpc == null || server == null) return;

                float now = Time.realtimeSinceStartup;
                if (now - _lastSentAt < SendGapSeconds &&
                    Core.LedgerUpdate.DistanceXZ(spot, _lastSentSpot) <= MergeMeters)
                {
                    if (ModConfig.VerboseLogging.Value)
                        Cairn.Log.LogInfo($"CairnNotice: {why} at ({spot.x:F0}, {spot.z:F0}) held back (sent one here " +
                                          $"{now - _lastSentAt:F2}s ago) [{DateTime.Now:HH:mm:ss.fff}]");
                    return;
                }
                _lastSentSpot = spot;
                _lastSentAt = now;
                if (ModConfig.VerboseLogging.Value)
                    Cairn.Log.LogInfo($"CairnNotice: {why} at ({spot.x:F0}, {spot.z:F0}) sent to the server " +
                                      $"[{DateTime.Now:HH:mm:ss.fff}]");

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
            if (++_windowCount[sender] > MaxPerSenderPerWindow)   // the sweep will still find it
            {
                if (ModConfig.VerboseLogging.Value)
                    Cairn.Log.LogInfo($"CairnNotice: notice from {sender} at ({spot.x:F0}, {spot.z:F0}) DROPPED by the " +
                                      $"per-sender limit [{DateTime.Now:HH:mm:ss.fff}]");
                return;
            }
            if (ModConfig.VerboseLogging.Value)
                Cairn.Log.LogInfo($"CairnNotice: notice from {sender} at ({spot.x:F0}, {spot.z:F0}) received " +
                                  $"[{DateTime.Now:HH:mm:ss.fff}]");

            Enqueue(spot, now);
        }

        private static ZDOMan _watchingOn;

        /// <summary>
        /// Authority, every frame: hear about every destroyed object from vanilla's own
        /// `ZDOMan.m_onZDODestroyed` (public; vanilla's ZNetScene subscribes the same way, with
        /// Delegate.Combine). It fires on the server for EVERY destroyed object, whoever destroyed
        /// it and however — the hammer, damage, a stack falling once the stone under it is gone —
        /// and on a single-player or hosted game for its own destroys too. So a removal needs no
        /// message from the player at all, and a player on an older build is covered.
        ///
        /// It replaced a client-side postfix on `WearNTear.Destroy` (0.12.0, never shipped), which
        /// on Storm10 sent ONE notice for four stones gone: the bottom two removed by hammer and
        /// two that fell. Why the other three did not reach it was not found; the server's own
        /// record of what was destroyed cannot miss one, so it stopped mattering.
        /// </summary>
        public static void EnsureWatching()
        {
            ZDOMan man = ZDOMan.instance;
            if (man == null || ReferenceEquals(man, _watchingOn)) return;
            StopWatching();
            man.m_onZDODestroyed = (Action<ZDO>)Delegate.Combine(man.m_onZDODestroyed, new Action<ZDO>(OnZdoDestroyed));
            _watchingOn = man;
        }

        private static void StopWatching()
        {
            if (_watchingOn == null) return;
            try
            {
                _watchingOn.m_onZDODestroyed = (Action<ZDO>)Delegate.Remove(_watchingOn.m_onZDODestroyed, new Action<ZDO>(OnZdoDestroyed));
            }
            catch (Exception) { /* the old world's ZDOMan is on its way out anyway */ }
            _watchingOn = null;
        }

        /// <summary>
        /// Runs INSIDE vanilla's ZDOMan.HandleDestroyedZDO, before it unlinks the object, for every
        /// object destroyed anywhere in the world. So it costs one dictionary lookup for anything
        /// that is not a stone or a sign, and it must never throw: an exception here would abort
        /// vanilla's own cleanup of that object (house rule 3).
        /// </summary>
        private static void OnZdoDestroyed(ZDO zdo)
        {
            try
            {
                if (zdo == null || !ModConfig.Enabled.Value || !ModConfig.QuickChecks.Value) return;
                Systems.LandmarkSystem system = Systems.LandmarkSystem.Instance;
                if (system == null || !system.IsWatched(zdo.GetPrefab(), out string prefab)) return;

                Vector3 spot = zdo.GetPosition();
                if (ModConfig.VerboseLogging.Value)
                    Cairn.Log.LogInfo($"CairnNotice: {prefab} destroyed at ({spot.x:F0}, {spot.z:F0}) " +
                                      $"[{DateTime.Now:HH:mm:ss.fff}]");
                // The object is already gone from the world's record: there is nothing to wait for
                // a second look at, so no retry.
                Enqueue(spot, Time.realtimeSinceStartup, mayRetry: false);
            }
            catch (Exception ex)
            {
                Cairn.Log.LogWarning($"CairnNotice: removal watch failed: {ex.Message}");
            }
        }

        private static void Enqueue(Vector3 spot, float now, bool mayRetry = true)
        {
            if (!ModConfig.QuickChecks.Value) return;

            // A pile is built a stone at a time: notices close together become ONE check, a second
            // after the latest stone. A removal merged into a pending placement keeps that
            // placement's retry; a placement merged into anything earns one.
            for (int i = 0; i < _pending.Count; i++)
            {
                if (Core.LedgerUpdate.DistanceXZ(_pending[i].Spot, spot) > MergeMeters) continue;
                _pending[i] = new Pending
                {
                    Spot = spot,
                    Due = now + WaitSeconds,
                    Retried = mayRetry ? false : _pending[i].Retried,
                };
                return;
            }

            if (_pending.Count >= MaxPending) return;
            _pending.Add(new Pending { Spot = spot, Due = now + WaitSeconds, Retried = !mayRetry });
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
            StopWatching();
            _pending.Clear();
            _windowStart.Clear();
            _windowCount.Clear();
            _lastSentAt = float.NegativeInfinity;
            ChecksRun = 0;
            ChecksThatChanged = 0;
        }
    }
}
