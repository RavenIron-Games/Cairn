using System;
using HarmonyLib;
using RavenIron.Cairn.Net;
using UnityEngine;

namespace RavenIron.Cairn.Patches
{
    /// <summary>
    /// Only the server may send beacons.
    ///
    /// Vanilla's routed RPC lets any client address a message to everybody, and the server
    /// relays it (`ZRoutedRpc.RPC_RoutedRPC`: anything not addressed to the server itself is
    /// passed to `RouteRPC`). Without this, one modded client could replace every player's
    /// beacons with hundreds of fake lights, or put arbitrary text in every raven's mouth.
    ///
    /// The server is the only legitimate sender of <see cref="LandmarkSync.RpcName"/>, and
    /// its own broadcasts never arrive through RPC_RoutedRPC — that method only handles
    /// packets that came in from a peer. So on the server, ANY incoming routed packet
    /// carrying the beacon method is a client pretending, and it is dropped before it can be
    /// handled or relayed. This does not rely on the packet's sender id, which the client
    /// writes itself and can forge.
    ///
    /// Server-side only; a client runs the original untouched. The packet is PEEKED: the
    /// read position is restored, so every other routed message is handled exactly as
    /// vanilla would. Only public ZPackage/ZNet members are named — publicized members that
    /// are private at runtime fail when the method is compiled, which has killed a server
    /// mid-boot once already.
    /// </summary>
    [HarmonyPatch(typeof(ZRoutedRpc), "RPC_RoutedRPC")]
    public static class Patch_RoutedRpcGuard
    {
        private static int _beaconHash;
        private static bool _hashed;
        private static float _lastWarn = -999f;
        private static int _dropped;

        [HarmonyPriority(Priority.Low)]
        private static bool Prefix(ZPackage pkg, bool __runOriginal)
        {
            if (!__runOriginal) return false;
            if (pkg == null) return true;

            ZNet znet = ZNet.instance;
            if (znet == null || !znet.IsServer()) return true;

            if (!_hashed)
            {
                _beaconHash = LandmarkSync.RpcName.GetStableHashCode();
                _hashed = true;
            }

            int pos = pkg.GetPos();
            try
            {
                // RoutedRPCData.Deserialize order on 1.0.15: msgID, sender, target, targetZDO,
                // methodHash, parameters.
                pkg.ReadLong();
                long claimedSender = pkg.ReadLong();
                pkg.ReadLong();
                pkg.ReadZDOID();
                int methodHash = pkg.ReadInt();

                if (methodHash != _beaconHash) return true;

                _dropped++;
                float now = Time.realtimeSinceStartup;
                if (now - _lastWarn >= 60f)
                {
                    _lastWarn = now;
                    Cairn.Log.LogWarning(
                        $"Dropped a beacon push sent by a client (claimed sender {claimedSender}); " +
                        $"only the server sends beacons. {_dropped} dropped so far.");
                }
                return false;
            }
            catch (Exception)
            {
                // Not ours to judge: let vanilla deserialize it and fail its own way.
                return true;
            }
            finally
            {
                pkg.SetPos(pos);
            }
        }
    }
}
