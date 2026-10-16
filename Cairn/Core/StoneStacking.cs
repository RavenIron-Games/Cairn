namespace RavenIron.Cairn.Core
{
    /// <summary>
    /// PURE. Which placements the stacking rule refuses.
    ///
    /// Vanilla ships `Placeable_Stone` with `WearNTear.m_supports = false`, and that one flag
    /// is why a stone cannot go on a stone: `Player.UpdatePlacementGhost` marks any placement
    /// onto a non-supporting piece Invalid (read out of 1.0.20, 2026-10-16; found by the
    /// `cairn pieces` stack readout, which reported rigidbody=no, collider on `piece`,
    /// supports=false). Flipping the flag lets stones stack — and, on its own, would let ANY
    /// piece stand on a pebble and draw structural support from it, which is almost certainly
    /// what the flag exists to prevent. So the flag is flipped AND this rule puts the fence
    /// back for everything that is not a stone: rocks on rocks, not a one-stone foundation.
    /// </summary>
    public static class StoneStacking
    {
        /// <summary>The one prefab this mod makes stackable.</summary>
        public const string StonePrefab = "Placeable_Stone";

        /// <summary>
        /// A prefab name with Unity's instance suffix stripped. A placed stone is named
        /// "Placeable_Stone(Clone)"; the ghost carries the bare prefab name.
        /// </summary>
        public static string PrefabName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            int paren = name.IndexOf('(');
            string bare = paren >= 0 ? name.Substring(0, paren) : name;
            return bare.Trim();
        }

        public static bool IsStone(string name) =>
            string.Equals(PrefabName(name), StonePrefab, System.StringComparison.Ordinal);

        /// <summary>
        /// Should a placement vanilla judged VALID be refused? Only when stacking is on, the
        /// thing being placed is not a stone, and the piece it would stand on is one. Every
        /// other case is left exactly as vanilla decided — this rule can only make a placement
        /// stricter, never allow one vanilla refused.
        /// </summary>
        public static bool Refuse(bool stackingEnabled, string ghostName, string targetName)
        {
            if (!stackingEnabled) return false;
            if (string.IsNullOrEmpty(targetName)) return false;   // ground, water, nothing
            if (!IsStone(targetName)) return false;               // not standing on a stone
            return !IsStone(ghostName);                            // a stone on a stone is the point
        }
    }
}
