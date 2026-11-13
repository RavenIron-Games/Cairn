using System;
using HarmonyLib;
using RavenIron.Cairn.Config;
using RavenIron.Cairn.Core;
using RavenIron.Cairn.Net;
using UnityEngine;

namespace RavenIron.Cairn.Patches
{
    /// <summary>
    /// The player's own game tells the server where a stone was placed or a sign written, so a
    /// new cairn lights in seconds (0.12.0). See <see cref="CairnNotice"/>.
    ///
    /// OBSERVING POSTFIXES. Neither changes anything the game does, its result, or what any other
    /// mod sees: they only read the arguments after vanilla has finished, and each swallows its own
    /// errors so placing a stone or writing a sign can never fail because of this. Both targets are
    /// public in the real 1.0.21 assembly: `Player.PlacePiece(Piece, Vector3, Quaternion, bool, bool)`
    /// and `Sign.SetText(string)`.
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
    }
}
