using System;
using HarmonyLib;
using RavenIron.Cairn.Config;
using RavenIron.Cairn.Core;
using UnityEngine;

namespace RavenIron.Cairn.Patches
{
    /// <summary>
    /// Stones stack. Two halves, and neither is safe without the other.
    ///
    /// 1. THE FLAG. Vanilla ships `Placeable_Stone` with `WearNTear.m_supports = false`. That one
    ///    bool refuses any placement onto the stone (`UpdatePlacementGhost`: a non-supporting
    ///    piece under the ray is Invalid) AND gives nothing resting on it any structural support
    ///    (`WearNTear.UpdateSupport` skips non-supporting pieces). Set true on the prefab when
    ///    the scene loads, so a stone can go on a stone and is held up by the one beneath it.
    ///
    /// 2. THE FENCE. The flag alone lets ANY piece stand on a pebble and draw support from it —
    ///    a hut on a one-stone foundation, which is very likely why Iron Gate set it false. So
    ///    a postfix on `Player.UpdatePlacementGhost` refuses a placement vanilla judged valid when
    ///    the ghost is not a stone and the piece under the crosshair is one. Rocks on rocks.
    ///
    /// This edits a VANILLA piece's behaviour, on by default — an amended locked decision
    /// (owner, 2026-10-16), announced at boot and switchable with `StackableStones`. It must be
    /// on every player's game: placement is checked on the placing client, and support is
    /// computed by whichever machine owns the piece. Cairn is already required on both sides.
    ///
    /// Every member here was read out of the REAL 1.0.20 assembly on 2026-10-16:
    /// `WearNTear.m_supports` and `Piece.SetInvalidPlacementHeightlight` are public, so they
    /// are used directly; `Player.m_placementStatus`, `Player.m_placementGhost` and
    /// `Player.PieceRayTest` are private, so they come through Harmony's field injection and a
    /// cached delegate (house rule 5).
    /// </summary>
    public static class Patch_StoneStacking
    {
        // ---- 1. the flag ---------------------------------------------------------------------

        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        public static class Flag
        {
            private static bool _capturedVanilla;
            private static bool _vanillaSupports;
            private static bool _announced;

            private static void Postfix(ZNetScene __instance)
            {
                try
                {
                    GameObject prefab = __instance.GetPrefab(StoneStacking.StonePrefab);
                    WearNTear wear = prefab != null ? prefab.GetComponent<WearNTear>() : null;
                    if (wear == null)
                    {
                        Cairn.Log.LogWarning(
                            $"stone stacking: no '{StoneStacking.StonePrefab}' with a WearNTear in this game. " +
                            "Stones stack exactly as vanilla allows, which is not at all.");
                        return;
                    }

                    // The prefab is an asset: it outlives the scene, so a later world in the same
                    // process sees what an earlier one set. Remember vanilla's own value once, and
                    // put it back if the switch is turned off between worlds.
                    if (!_capturedVanilla)
                    {
                        _vanillaSupports = wear.m_supports;
                        _capturedVanilla = true;
                    }

                    bool want = ModConfig.StackableStones.Value || _vanillaSupports;
                    wear.m_supports = want;

                    if (!_announced)
                    {
                        _announced = true;
                        if (ModConfig.StackableStones.Value)
                            Cairn.Log.LogWarning(
                                $"stone stacking ON: {StoneStacking.StonePrefab} now holds up what is stacked on it " +
                                $"(vanilla: supports={_vanillaSupports.ToString().ToLowerInvariant()}), and only a stone " +
                                "may be placed on a stone. This changes a VANILLA piece — set StackableStones = false " +
                                "to leave it alone.");
                        else
                            Cairn.Log.LogInfo("stone stacking off: Placeable_Stone is left exactly as vanilla made it.");
                    }
                }
                catch (Exception ex)
                {
                    // A convenience must never take the scene down with it.
                    Cairn.Log.LogWarning($"stone stacking could not be applied: {ex.Message}");
                }
            }
        }

        // ---- 2. the fence --------------------------------------------------------------------

        [HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
        public static class Fence
        {
            private delegate bool PieceRayTestFn(Player self, out Vector3 point, out Vector3 normal,
                out Piece piece, out Heightmap heightmap, out Collider waterSurface, bool water);

            private static PieceRayTestFn _rayTest;
            private static bool _rayMissingLogged;
            private static bool _failed;

            /// <summary>
            /// Resolved lazily and NEVER latched on failure (house rule 5): if it is genuinely
            /// absent, say so by name once, because that means Valheim's API moved.
            /// </summary>
            private static PieceRayTestFn RayTest()
            {
                if (_rayTest != null) return _rayTest;
                try
                {
                    var method = AccessTools.Method(typeof(Player), "PieceRayTest");
                    if (method != null) _rayTest = AccessTools.MethodDelegate<PieceRayTestFn>(method);
                }
                catch (Exception ex)
                {
                    if (!_rayMissingLogged)
                        Cairn.Log.LogError($"stone stacking: could not bind Player.PieceRayTest ({ex.Message}) — Valheim's API moved.");
                    _rayMissingLogged = true;
                }
                if (_rayTest == null && !_rayMissingLogged)
                {
                    _rayMissingLogged = true;
                    Cairn.Log.LogError("stone stacking: Player.PieceRayTest not found — Valheim's API moved. " +
                                       "Stones still stack, but any piece can be placed on one.");
                }
                return _rayTest;
            }

            /// <summary>
            /// Runs every frame the build ghost is up, so it is built to cost nothing in the common
            /// case: it leaves at once unless the switch is on, vanilla said Valid, and the ghost is
            /// NOT a stone. Only then does it cast one ray — vanilla's own, through the same method.
            ///
            /// A RESULT-DECORATING postfix in house rule 1's sense: it can only make the answer
            /// stricter. It never turns a placement vanilla or another mod refused into a valid one.
            /// </summary>
            private static void Postfix(Player __instance, GameObject ___m_placementGhost,
                                        ref Player.PlacementStatus ___m_placementStatus)
            {
                if (_failed) return;
                if (!ModConfig.StackableStones.Value) return;
                if (___m_placementStatus != Player.PlacementStatus.Valid) return;
                if (___m_placementGhost == null) return;
                if (StoneStacking.IsStone(___m_placementGhost.name)) return;   // a stone may go anywhere vanilla allows

                try
                {
                    PieceRayTestFn ray = RayTest();
                    if (ray == null) return;

                    Piece ghost = ___m_placementGhost.GetComponent<Piece>();
                    bool water = ghost != null && (ghost.m_waterPiece || ghost.m_noInWater);

                    if (!ray(__instance, out _, out _, out Piece target, out _, out _, water)) return;
                    if (target == null) return;

                    if (!StoneStacking.Refuse(true, ___m_placementGhost.name, target.gameObject.name)) return;

                    // TryPlacePiece re-runs this method and then switches on the status, so this one
                    // assignment blocks the click and shows vanilla's own "invalid placement" message.
                    ___m_placementStatus = Player.PlacementStatus.Invalid;
                    if (ghost != null) ghost.SetInvalidPlacementHeightlight(true);
                }
                catch (Exception ex)
                {
                    // House rule 3, and a per-frame method: log once and stand down rather than
                    // throw every frame. The flag stays on; only the fence is lost.
                    _failed = true;
                    Cairn.Log.LogError($"stone stacking fence disabled after an error: {ex}");
                }
            }
        }
    }
}
