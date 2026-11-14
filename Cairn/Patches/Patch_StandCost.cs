using System;
using System.Collections.Generic;
using HarmonyLib;
using RavenIron.Cairn.Config;
using RavenIron.Cairn.Core;
using UnityEngine;

namespace RavenIron.Cairn.Patches
{
    /// <summary>
    /// The item stands cost only wood, and need no workbench (0.13.1, `ItemStandWoodCost`, default
    /// 2, the owner's call 2026-11-14): a cairn's colour stands on a lonely headland, and a player
    /// asked why a colour should cost fine wood and a trip back to a workbench.
    ///
    /// THIS EDITS A VANILLA RECIPE, for EVERY item stand in the world, trophy walls included: a
    /// recipe belongs to the piece, not to where it is placed. Making it cheap only on a cairn was
    /// rejected because knocking a stand down refunds its CURRENT recipe (`Piece.DropResources`),
    /// so a stand bought for wood could be knocked down for fine wood. Announced at boot with the
    /// before and after, like `StonePileStoneCost`; `ItemStandWoodCost = 0` leaves vanilla alone,
    /// and turning it to 0 between worlds puts vanilla's recipe back (captured once per process,
    /// because a prefab outlives the scene).
    ///
    /// The flip side, said once so nobody is surprised: a stand built while this is on and knocked
    /// down after it is turned off refunds vanilla's recipe. That is a player editing their own
    /// config, which in single player is already the whole game.
    ///
    /// PER PLAYER, like every build cost: the cost is checked and taken by the building player's
    /// own game, so each player's own setting is what counts for them.
    ///
    /// `Piece.m_resources`, `Piece.m_craftingStation` and every `Piece.Requirement` field used are
    /// public in the real 1.0.21 assembly. The wood item comes from ZNetScene (items are networked
    /// prefabs), which exists here; ObjectDB may not yet.
    /// </summary>
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    public static class Patch_StandCost
    {
        private const string WoodItem = "Wood";

        private sealed class Vanilla
        {
            public Piece.Requirement[] Resources;
            public CraftingStation Station;
        }

        private static readonly Dictionary<string, Vanilla> _vanilla = new Dictionary<string, Vanilla>();
        private static readonly HashSet<string> _announced = new HashSet<string>();

        private static void Postfix(ZNetScene __instance)
        {
            try
            {
                int wood = ModConfig.ItemStandWoodCost.Value;
                GameObject woodPrefab = __instance.GetPrefab(WoodItem);
                ItemDrop woodDrop = woodPrefab != null ? woodPrefab.GetComponent<ItemDrop>() : null;

                foreach (string raw in (ModConfig.HolderPrefabs.Value ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string name = raw.Trim();
                    if (name.Length == 0) continue;
                    GameObject prefab = __instance.GetPrefab(name);
                    Piece piece = prefab != null ? prefab.GetComponent<Piece>() : null;
                    if (piece == null) continue;

                    if (!_vanilla.ContainsKey(name))
                        _vanilla[name] = new Vanilla { Resources = piece.m_resources, Station = piece.m_craftingStation };
                    Vanilla v = _vanilla[name];

                    if (wood <= 0)
                    {
                        // Off: exactly what the game shipped, even if an earlier world here had it on.
                        piece.m_resources = v.Resources;
                        piece.m_craftingStation = v.Station;
                        continue;
                    }

                    if (woodDrop == null)
                    {
                        Cairn.Log.LogWarning($"stand cost: no '{WoodItem}' item in this game; '{name}' left as vanilla made it.");
                        continue;
                    }

                    piece.m_resources = new[]
                    {
                        new Piece.Requirement { m_resItem = woodDrop, m_amount = wood, m_amountPerLevel = 0, m_recover = true }
                    };
                    piece.m_craftingStation = null;

                    if (_announced.Add(name))
                        Cairn.Log.LogWarning(
                            $"stand cost ACTIVE: '{name}' now costs {wood} {WoodItem} and needs no crafting station; " +
                            $"vanilla was {Describe(v)}. This edits a VANILLA recipe, for every item stand, trophy " +
                            "walls included — set ItemStandWoodCost = 0 to leave the game alone.");
                }
            }
            catch (Exception ex)
            {
                // A convenience must never take the scene down with it.
                Cairn.Log.LogWarning($"stand cost could not be applied: {ex.Message}");
            }
        }

        private static string Describe(Vanilla v)
        {
            var parts = new List<string>(3);
            if (v.Resources != null)
                foreach (Piece.Requirement r in v.Resources)
                    if (r != null && r.m_resItem != null) parts.Add($"{r.m_amount} {r.m_resItem.gameObject.name}");
            string cost = parts.Count > 0 ? string.Join(" + ", parts.ToArray()) : "free";
            return cost + (v.Station != null ? $" at a {v.Station.gameObject.name}" : " with no station");
        }
    }
}
