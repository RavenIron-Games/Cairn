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
    /// cairn lights or renames in seconds (0.12.0). See <see cref="CairnNotice"/>. A REMOVAL needs
    /// no message: the server hears of every destroyed object from vanilla itself
    /// (`CairnNotice.EnsureWatching`).
    ///
    /// OBSERVING POSTFIXES. Neither changes anything the game does, its result, or what any other
    /// mod sees: they only read the arguments after vanilla has finished, and each swallows its own
    /// errors so placing a stone or writing a sign can never fail because of this. In the real
    /// 1.0.21 assembly `Player.PlacePiece(Piece, Vector3, Quaternion, bool, bool)` and
    /// `Sign.SetText(string)` are public.
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
                        StoneStacking.IsListed(ModConfig.SignPrefabs.Value, prefab) ||
                        StoneStacking.IsListed(ModConfig.HolderPrefabs.Value, prefab))
                        CairnNotice.Notify(pos, "placed " + prefab);
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
                    if (__instance != null) CairnNotice.Notify(__instance.transform.position, "sign written");
                }
                catch (Exception ex)
                {
                    Cairn.Log.LogWarning($"QuickNotice: sign notice failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// An item was put on a stand (0.13.0), which may give a cairn its colour. `UseItem` is
        /// public in 1.0.21 and returns true when the stand accepted the item. The stand records
        /// it a moment later (`UpdateAttach`, on the stand's owner); the server's one-second wait,
        /// and its second look three seconds on, are what cover that gap.
        /// </summary>
        [HarmonyPatch(typeof(ItemStand), nameof(ItemStand.UseItem))]
        public static class ItemPut
        {
            private static void Postfix(ItemStand __instance, bool __result)
            {
                try
                {
                    if (__result && __instance != null)
                        CairnNotice.Notify(__instance.transform.position, "item put on a stand");
                }
                catch (Exception ex)
                {
                    Cairn.Log.LogWarning($"QuickNotice: item-stand notice failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// An item was taken off a stand (0.13.0): `Interact` on a stand holding something drops
        /// it. Public in 1.0.21; true when it acted. The prefix only records whether the stand
        /// held anything, so an empty stand's interaction sends nothing; it changes nothing.
        /// </summary>
        [HarmonyPatch(typeof(ItemStand), nameof(ItemStand.Interact))]
        public static class ItemTaken
        {
            private static void Prefix(ItemStand __instance, out bool __state)
            {
                __state = false;
                try { __state = __instance != null && __instance.HaveAttachment(); }
                catch (Exception) { /* observing only */ }
            }

            private static void Postfix(ItemStand __instance, bool __result, bool __state)
            {
                try
                {
                    if (__result && __state && __instance != null)
                        CairnNotice.Notify(__instance.transform.position, "item taken off a stand");
                }
                catch (Exception ex)
                {
                    Cairn.Log.LogWarning($"QuickNotice: item-stand notice failed: {ex.Message}");
                }
            }
        }
    }
}
