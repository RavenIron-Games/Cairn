using System;
using System.Collections.Generic;
using HarmonyLib;
using RavenIron.Cairn.Config;
using RavenIron.Cairn.Core;
using UnityEngine;

namespace RavenIron.Cairn.Patches
{
    /// <summary>
    /// An item stand may hold an item on the `HeldColours` list even when vanilla has no model made
    /// for showing it on one — a greydwarf eye — and Cairn draws the item's own model there (0.13.0,
    /// the owner's call 2026-11-13, `StandsHoldListedItems`, on by default). The first change this
    /// mod makes to what a vanilla piece accepts since stone stacking, and announced the same way.
    ///
    /// Two RESULT-DECORATING postfixes (house rule 1, amended): neither replaces logic.
    ///
    /// 1. `ItemStand.CanAttach` (private, patched by name; never called): may turn a refusal into a
    ///    yes, and only for the one reason vanilla refuses a listed item — no "attach" model. The
    ///    stand's own unsupported list and supported types still apply (<see cref="Holdable.CairnHolds"/>).
    ///    Each player's own game asks this, so a server's added items need the same list on players.
    ///
    /// 2. `ItemStand.SetVisualItem` (private): vanilla records the item, logs "Failed to get attach
    ///    prefab", and draws nothing (decompiled 1.0.21). Afterwards, if nothing is drawn and the
    ///    item is one Cairn lets on, a plain copy of its model is drawn and handed to vanilla as the
    ///    stand's visual item, so vanilla destroys it on change exactly as it destroys its own. Purely
    ///    cosmetic (house rule 3): its own try/catch, and a failure leaves the stand as vanilla left it.
    ///
    /// Taking the item off is vanilla's own path, which already handles a missing attach model.
    /// </summary>
    public static class Patch_StandsHold
    {
        private static readonly HashSet<string> _announced = new HashSet<string>();

        [HarmonyPatch(typeof(ItemStand), "CanAttach")]
        public static class Accept
        {
            private static void Postfix(ItemStand __instance, ItemDrop.ItemData item, ref bool __result)
            {
                if (__result) return;   // vanilla already said yes; nothing to decorate
                try
                {
                    GameObject prefab = item?.m_dropPrefab;
                    if (prefab == null || !Holdable.CairnHolds(__instance, prefab)) return;
                    __result = true;

                    if (_announced.Add(prefab.name))
                        Cairn.Log.LogInfo(
                            $"StandsHoldListedItems: letting an item stand hold {prefab.name}, which vanilla " +
                            "refuses for want of a display model; Cairn draws the item's own model on it. " +
                            "Set StandsHoldListedItems = false to leave stands as vanilla made them.");
                }
                catch (Exception ex)
                {
                    Cairn.Log.LogWarning($"StandsHoldListedItems: check failed, leaving vanilla's answer: {ex.Message}");
                }
            }
        }

        [HarmonyPatch(typeof(ItemStand), "SetVisualItem")]
        public static class Draw
        {
            private static readonly AccessTools.FieldRef<ItemStand, GameObject> VisualItem =
                AccessTools.FieldRefAccess<ItemStand, GameObject>("m_visualItem");

            private static void Postfix(ItemStand __instance, int itemHash)
            {
                try
                {
                    if (itemHash == 0 || __instance == null) return;
                    if (VisualItem(__instance) != null) return;   // vanilla drew it, or we already did

                    ObjectDB db = ObjectDB.instance;
                    GameObject prefab = db != null ? db.GetItemPrefab(itemHash) : null;
                    if (prefab == null || !Holdable.CairnHolds(__instance, prefab)) return;

                    Transform attach = __instance.m_attachOther != null ? __instance.m_attachOther : __instance.transform;
                    GameObject copy = Holdable.DrawnCopy(prefab, attach);
                    if (copy == null) return;

                    VisualItem(__instance) = copy;
                    ItemDrop drop = prefab.GetComponent<ItemDrop>();
                    if (drop != null) __instance.m_currentItemName = drop.m_itemData.m_shared.m_name;
                }
                catch (Exception ex)
                {
                    Cairn.Log.LogWarning($"StandsHoldListedItems: could not draw the held item: {ex.Message}");
                }
            }
        }
    }
}
