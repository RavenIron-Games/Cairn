using System;
using System.Collections.Generic;
using RavenIron.Cairn.Config;
using UnityEngine;

namespace RavenIron.Cairn.Core
{
    /// <summary>
    /// Can an item stand hold this item? VANILLA decides first, and this is its rule copied, so
    /// Cairn can say in advance which `HeldColours` entries will work (0.13.0).
    ///
    /// Found the hard way on 2026-11-13: a greydwarf eye, the first item the owner asked for, cannot
    /// go on a stand at all. `ItemStand.CanAttach` (1.0.21, private) wants, in this order:
    ///   1. a child named "attach" on the item's prefab — the model shown on the stand. Most
    ///      materials have none, and without it nothing else is even asked;
    ///   2. the item not in the stand's `m_unsupportedItems` (compared by shared name);
    ///   3. then either the stand's `m_supportedItems` is EMPTY or lists it (that alone accepts),
    ///      or the item's type is in `m_supportedTypes`.
    ///
    /// AND THEN CAIRN (`StandsHoldListedItems`, on by default, the owner's call 2026-11-13): an
    /// item on the `HeldColours` list that fails ONLY rule 1 is let onto a stand named in
    /// `HolderPrefabs`, and Cairn draws a copy of the item's own model there
    /// (<see cref="DrawnCopy"/>). Rule 2, the stand's own never-this list, still holds; rule 3, the
    /// type, does not, because no stand accepts the material type and that is what most cheap
    /// colourful things are. The owner's list is the gate.
    ///
    /// Every member read here is public in the real 1.0.21 assembly: `ItemStand.GetAttachPrefab`
    /// (static), the three lists, `ItemDrop.m_itemData.m_shared`, `ZNetScene.GetPrefab(string)` and
    /// `ObjectDB.m_items`. CanAttach itself is private and is only ever patched, never called.
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

        private static SharedOf Shared(GameObject itemPrefab)
        {
            ItemDrop drop = itemPrefab != null ? itemPrefab.GetComponent<ItemDrop>() : null;
            return drop != null && drop.m_itemData != null && drop.m_itemData.m_shared != null
                ? new SharedOf { Data = drop.m_itemData.m_shared } : null;
        }

        private sealed class SharedOf { public ItemDrop.ItemData.SharedData Data; }

        /// <summary>Rules 2 and 3 of vanilla's CanAttach: everything except the display model.</summary>
        private static bool PassesStandRules(ItemStand stand, ItemDrop.ItemData.SharedData shared)
        {
            foreach (ItemDrop no in stand.m_unsupportedItems)
                if (no != null && no.m_itemData.m_shared.m_name == shared.m_name) return false;

            if (stand.m_supportedItems.Count == 0) return true;
            foreach (ItemDrop yes in stand.m_supportedItems)
                if (yes != null && yes.m_itemData.m_shared.m_name == shared.m_name) return true;

            return stand.m_supportedTypes.Contains(shared.m_itemType);
        }

        /// <summary>Vanilla's CanAttach, for one stand and one item prefab.</summary>
        public static bool VanillaHolds(ItemStand stand, GameObject itemPrefab)
        {
            if (stand == null || itemPrefab == null) return false;
            SharedOf s = Shared(itemPrefab);
            if (s == null) return false;
            if (ItemStand.GetAttachPrefab(itemPrefab) == null) return false;
            return PassesStandRules(stand, s.Data);
        }

        private static string _listSource;
        private static Dictionary<string, string> _list = new Dictionary<string, string>();

        /// <summary>The HeldColours list, re-read only when the setting's text changes.</summary>
        public static Dictionary<string, string> Listed()
        {
            string now = ModConfig.HeldColours.Value ?? "";
            if (!ReferenceEquals(now, _listSource) && now != _listSource)
            {
                _list = HeldColour.Parse(now);
                _listSource = now;
            }
            return _list;
        }

        /// <summary>
        /// Does Cairn let this stand hold this item where vanilla would not? Only a listed item,
        /// only on a listed stand, only for want of a display model, and only with the switch on.
        /// </summary>
        public static bool CairnHolds(ItemStand stand, GameObject itemPrefab)
        {
            if (!ModConfig.StandsHoldListedItems.Value) return false;
            if (stand == null || itemPrefab == null) return false;
            if (!StoneStacking.IsListed(ModConfig.HolderPrefabs.Value, stand.gameObject.name)) return false;
            if (!Listed().ContainsKey(itemPrefab.name)) return false;
            return Drawable(stand, itemPrefab);
        }

        /// <summary>
        /// Refused by vanilla, but Cairn could put it on and draw it: no display model, a model of
        /// its own, and not on the stand's explicit never-this list. The stand's TYPE rule is NOT
        /// asked: on 2026-11-13 the first version did ask it, and every material (greydwarf eye,
        /// resin, guck, crystal) stayed off, because neither stand accepts the material type at
        /// all. Only listed items ever get this far (<see cref="CairnHolds"/>), so a list the owner
        /// writes is the gate, not the type. What `cairn holdable more` lists.
        /// </summary>
        public static bool Drawable(ItemStand stand, GameObject itemPrefab)
        {
            if (stand == null || itemPrefab == null) return false;
            if (VanillaHolds(stand, itemPrefab)) return false;                  // vanilla's own business
            if (!HasModel(itemPrefab)) return false;                            // nothing to draw
            SharedOf s = Shared(itemPrefab);
            return s != null && !NeverOn(stand, s.Data);
        }

        private static bool NeverOn(ItemStand stand, ItemDrop.ItemData.SharedData shared)
        {
            foreach (ItemDrop no in stand.m_unsupportedItems)
                if (no != null && no.m_itemData.m_shared.m_name == shared.m_name) return true;
            return false;
        }

        /// <summary>Every rule, for one item on one stand, for `cairn holdable why`.</summary>
        public static string Explain(ItemStand stand, GameObject itemPrefab)
        {
            SharedOf s = Shared(itemPrefab);
            if (s == null) return "not an item (no ItemDrop)";
            bool supportedListed = false;
            foreach (ItemDrop yes in stand.m_supportedItems)
                if (yes != null && yes.m_itemData.m_shared.m_name == s.Data.m_name) supportedListed = true;

            return $"display model: {(ItemStand.GetAttachPrefab(itemPrefab) != null ? "yes" : "NO")}; " +
                   $"own model: {ModelRenderers(itemPrefab).Count} mesh(es); " +
                   $"never-list: {(NeverOn(stand, s.Data) ? "ON IT" : "no")}; " +
                   $"stand's item list: {(stand.m_supportedItems.Count == 0 ? "empty (any)" : supportedListed ? "lists it" : $"{stand.m_supportedItems.Count} items, not this")}; " +
                   $"type {s.Data.m_itemType}: {(stand.m_supportedTypes.Contains(s.Data.m_itemType) ? "accepted" : "not accepted")} " +
                   $"→ vanilla {(VanillaHolds(stand, itemPrefab) ? "HOLDS it" : "refuses")}, " +
                   $"Cairn {(CairnHolds(stand, itemPrefab) ? "puts it on" : Drawable(stand, itemPrefab) ? "would put it on if listed" : "cannot")}";
        }

        public static bool CanHold(ItemStand stand, GameObject itemPrefab) =>
            VanillaHolds(stand, itemPrefab) || CairnHolds(stand, itemPrefab);

        /// <summary>
        /// Which of the stands can hold this item: "itemstandh/itemstand", or "" for none. Marks
        /// " (drawn by Cairn)" when only Cairn's allowance gets it on.
        /// </summary>
        public static string HeldBy(List<KeyValuePair<string, ItemStand>> stands, GameObject itemPrefab)
        {
            var by = new List<string>(2);
            bool drawn = false;
            foreach (KeyValuePair<string, ItemStand> s in stands)
            {
                if (VanillaHolds(s.Value, itemPrefab)) by.Add(s.Key);
                else if (CairnHolds(s.Value, itemPrefab)) { by.Add(s.Key); drawn = true; }
            }
            return string.Join("/", by.ToArray()) + (drawn ? " (drawn by Cairn)" : "");
        }

        // ---- the drawn copy ------------------------------------------------------------------

        /// <summary>The renderers that make up how the item looks lying on the ground: LOD 0 if
        /// it has levels of detail, otherwise every active mesh. Never particles.</summary>
        private static List<Renderer> ModelRenderers(GameObject itemPrefab)
        {
            var list = new List<Renderer>(4);
            LODGroup lod = itemPrefab.GetComponentInChildren<LODGroup>(true);
            if (lod != null)
            {
                LOD[] lods = lod.GetLODs();
                if (lods.Length > 0 && lods[0].renderers != null)
                {
                    foreach (Renderer r in lods[0].renderers)
                        if (r is MeshRenderer || r is SkinnedMeshRenderer) list.Add(r);
                    if (list.Count > 0) return list;
                }
            }
            // includeInactive TRUE, then judged by each object's own switch: a prefab asset is not
            // in a scene, so "active in hierarchy" is not a question it can answer reliably.
            foreach (Renderer r in itemPrefab.GetComponentsInChildren<Renderer>(true))
                if ((r is MeshRenderer || r is SkinnedMeshRenderer) && r.enabled && ActiveUpTo(r.transform, itemPrefab.transform))
                    list.Add(r);
            return list;
        }

        private static bool ActiveUpTo(Transform t, Transform root)
        {
            for (; t != null && t != root; t = t.parent)
                if (!t.gameObject.activeSelf) return false;
            return true;
        }

        private static Mesh MeshOf(Renderer r)
        {
            if (r is SkinnedMeshRenderer skinned) return skinned.sharedMesh;
            MeshFilter filter = r.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }

        public static bool HasModel(GameObject itemPrefab)
        {
            foreach (Renderer r in ModelRenderers(itemPrefab))
                if (MeshOf(r) != null) return true;
            return false;
        }

        /// <summary>Largest side of a drawn item, in metres; bigger models are scaled down to it.</summary>
        private const float MaxDrawnSize = 0.6f;

        /// <summary>
        /// A plain copy of the item's model, parented to <paramref name="attach"/>, standing on it.
        /// Meshes and materials are SHARED with the item's prefab, never copied or changed; no
        /// collider, no rigidbody, no ZNetView, so the network never hears of it. Null when the item
        /// has no mesh to show.
        /// </summary>
        public static GameObject DrawnCopy(GameObject itemPrefab, Transform attach)
        {
            List<Renderer> renderers = ModelRenderers(itemPrefab);
            if (renderers.Count == 0) return null;

            var root = new GameObject("cairn_held_item");
            root.layer = attach.gameObject.layer;
            root.transform.SetParent(attach, false);

            Matrix4x4 toRoot = itemPrefab.transform.worldToLocalMatrix;
            bool any = false;
            var bounds = new Bounds();
            var parts = new List<Transform>(renderers.Count);

            foreach (Renderer r in renderers)
            {
                Mesh mesh = MeshOf(r);
                if (mesh == null) continue;

                Matrix4x4 m = toRoot * r.transform.localToWorldMatrix;
                var part = new GameObject(r.name);
                part.layer = root.layer;
                part.transform.SetParent(root.transform, false);
                part.transform.localPosition = m.GetColumn(3);
                part.transform.localRotation = m.rotation;
                part.transform.localScale = m.lossyScale;

                part.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = part.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = r.sharedMaterials;

                // The mesh's own bounds, carried into the copy's space, so the model can be stood
                // on the stand and sized whatever its pivot.
                Bounds mb = mesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = mb.center + Vector3.Scale(mb.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = m.MultiplyPoint3x4(corner);
                    if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                    else bounds.Encapsulate(p);
                }
                parts.Add(part.transform);
            }

            if (!any)
            {
                UnityEngine.Object.Destroy(root);
                return null;
            }

            float largest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            float scale = largest > MaxDrawnSize ? MaxDrawnSize / largest : 1f;

            // Bottom centre of the model on the attach point.
            Vector3 shift = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
            foreach (Transform part in parts) part.localPosition += shift;
            root.transform.localScale = Vector3.one * scale;
            return root;
        }
    }
}
