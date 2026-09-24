using HarmonyLib;
using RavenIron.Cairn.Core;

namespace RavenIron.Cairn.Patches
{
    /// <summary>
    /// Leaving a world: logout to the menu, a disconnect, or a server shutting down.
    ///
    /// The plugin's GameObject is DontDestroyOnLoad, so CairnTick.OnDestroy only runs when
    /// the application quits. ZNet, on the other hand, is destroyed with every world, which
    /// makes its OnDestroy the one reliable "this world is over" moment — and at that moment
    /// ZNet's static m_world and m_isServer still name the world that is ending, so the
    /// ledger flush resolves the right file.
    ///
    /// Observation only: nothing about ZNet's own teardown changes, and the prefix runs
    /// whether or not another mod has chosen to skip the original.
    /// </summary>
    [HarmonyPatch(typeof(ZNet), "OnDestroy")]
    public static class Patch_WorldLifecycle
    {
        private static void Prefix(ZNet __instance)
        {
            try
            {
                CairnTick.OnZNetDestroyed(__instance);
            }
            catch (System.Exception ex)
            {
                // Teardown must never fail because of us. CairnTick.Update's fallback catches
                // the world change on the next frame anyway.
                Cairn.Log.LogWarning($"world-end reset failed: {ex.Message}");
            }
        }
    }
}
