using System;
using System.Collections.Generic;
using RavenIron.Cairn.Config;
using UnityEngine;

namespace RavenIron.Cairn.Core
{
    /// <summary>
    /// Can an item stand hold this item? VANILLA decides, and this is its rule copied, so Cairn can
    /// say in advance which `HeldColours` entries will never work (0.13.0).
    ///
    /// Found the hard way on 2026-11-13: a greydwarf eye, the first item the owner asked for, cannot
    /// go on a stand at all. `ItemStand.CanAttach` (1.0.21, private) wants, in this order:
    ///   1. a child named "attach" on the item's prefab — the model shown on the stand. Most
    ///      materials have none, and without it nothing else is even asked;
    ///   2. the item not in the stand's `m_unsupportedItems` (compared by shared name);
    ///   3. then either the stand's `m_supportedItems` is EMPTY or lists it (that alone accepts),
    ///      or the item's type is in `m_supportedTypes`.
    /// Every member read here is public in the real 1.0.21 assembly: `ItemStand.GetAttachPrefab`
    /// (static), the three lists, `ItemDrop.m_itemData.m_shared`, `ZNetScene.GetPrefab(string)`
    /// and `ObjectDB.m_items`. CanAttach itself is private, so it is never called (house rule 5).
    /// </summary>
    public static class Holdable
    {
        /// <summary>The item stands named in HolderPrefabs that this game has, by prefab name.</summary>
        public static List<KeyValuePair<string, ItemStand>> Stands()
        {
            var stands = new List<KeyValuePair<string, ItemStand>>(2);
            ZNetScene scene = ZNetScene.instance;
            if (scene == null) return stands;

            foreach (string raw in (ModConfig.HolderPrefabs.Value ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string name = raw.Trim();
                if (name.Length == 0) continue;
                GameObject prefab = scene.GetPrefab(name);
                ItemStand stand = prefab != null ? prefab.GetComponent<ItemStand>() : null;
                if (stand != null) stands.Add(new KeyValuePair<string, ItemStand>(name, stand));
            }
            return stands;
        }

        /// <summary>Vanilla's CanAttach, for one stand and one item prefab.</summary>
        public static bool CanHold(ItemStand stand, GameObject itemPrefab)
        {
            if (stand == null || itemPrefab == null) return false;
            ItemDrop drop = itemPrefab.GetComponent<ItemDrop>();
            if (drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null) return false;
            if (ItemStand.GetAttachPrefab(itemPrefab) == null) return false;

            ItemDrop.ItemData.SharedData shared = drop.m_itemData.m_shared;
            foreach (ItemDrop no in stand.m_unsupportedItems)
                if (no != null && no.m_itemData.m_shared.m_name == shared.m_name) return false;

            if (stand.m_supportedItems.Count == 0) return true;
            foreach (ItemDrop yes in stand.m_supportedItems)
                if (yes != null && yes.m_itemData.m_shared.m_name == shared.m_name) return true;

            return stand.m_supportedTypes.Contains(shared.m_itemType);
        }

        /// <summary>Which of the stands can hold this item: "itemstandh/itemstand", or "" for none.</summary>
        public static string HeldBy(List<KeyValuePair<string, ItemStand>> stands, GameObject itemPrefab)
        {
            var by = new List<string>(2);
            foreach (KeyValuePair<string, ItemStand> s in stands)
                if (CanHold(s.Value, itemPrefab)) by.Add(s.Key);
            return string.Join("/", by.ToArray());
        }
    }
}
