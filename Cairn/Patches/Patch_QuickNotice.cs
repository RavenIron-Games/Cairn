using System;
using HarmonyLib;
using RavenIron.Cairn.Config;
using RavenIron.Cairn.Core;
using RavenIron.Cairn.Net;
using UnityEngine;

namespace RavenIron.Cairn.Patches
{
    /// <summary>
    /// The player's own game tells the server where a stone was placed, a sign written, or
    /// either one removed, so a cairn lights, renames or goes out in seconds (0.12.0). See
    /// <see cref="CairnNotice"/>.
    ///
    /// OBSERVING POSTFIXES. Neither changes anything the game does, its result, or what any other
    /// mod sees: they only read the arguments after vanilla has finished, and each swallows its own
    /// errors so placing a stone or writing a sign can never fail because of this. In the real
    /// 1.0.21 assembly `Player.PlacePiece(Piece, Vector3, Quaternion, bool, bool)` and
    /// `Sign.SetText(string)` are public; `WearNTear.Destroy(HitData, bool)` is private, which
    /// matters only to a direct call (house rule 5): Harmony patches it by name, and nothing here
    /// calls it.
    /// </summary>
    public static class Patch_QuickNotice
    {
        [HarmonyPatch(typeof(Player), nameof(Player.PlacePiece))]
        public static class Placed
        {
            private static void Postfix(Player __instance, Piece piece, Vector3 pos)
            {
                try
                {
                    if (piece == null || __instance != Player.m_localPlayer) return;

                    string prefab = piece.gameObject.name;
                    if (StoneStacking.IsListed(ModConfig.StonePrefabs.Value, prefab) ||
                        StoneStacking.IsListed(ModConfig.SignPrefabs.Value, prefab))
                        CairnNotice.Notify(pos);
                }
                catch (Exception ex)
                {
                    Cairn.Log.LogWarning($"QuickNotice: placement notice failed: {ex.Message}");
                }
            }
        }

        [HarmonyPatch(typeof(Sign), nameof(Sign.SetText))]
        public static class Written
        {
            private static void Postfix(Sign __instance)
            {
                try
                {
                    if (__instance != null) CairnNotice.Notify(__instance.transform.position);
                }
                catch (Exception ex)
                {
                    Cairn.Log.LogWarning($"QuickNotice: sign notice failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// A stone or sign is gone. `WearNTear.Destroy` is where EVERY removal ends: the hammer or
        /// hoe (`Player.RemovePiece` → `Remove` → `RPC_Remove`, run by the owner), damage, and a
        /// stacked stone falling once the stone under it is taken. It runs once, on the machine
        /// that owns the piece, so exactly one game sends the notice — the server's own, when it
        /// owns the piece. Anything that is not a stone or a sign costs a name comparison and
        /// nothing else. The transform is still readable: Unity destroys at the end of the frame.
        /// </summary>
        [HarmonyPatch(typeof(WearNTear), "Destroy", new[] { typeof(HitData), typeof(bool) })]
        public static class Removed
        {
            private static void Postfix(WearNTear __instance)
            {
                try
                {
                    if (__instance == null) return;
                    string prefab = __instance.gameObject.name;
                    if (StoneStacking.IsListed(ModConfig.StonePrefabs.Value, prefab) ||
                        StoneStacking.IsListed(ModConfig.SignPrefabs.Value, prefab))
                        CairnNotice.Notify(__instance.transform.position);
                }
                catch (Exception ex)
                {
                    Cairn.Log.LogWarning($"QuickNotice: removal notice failed: {ex.Message}");
                }
            }
        }
    }
}
