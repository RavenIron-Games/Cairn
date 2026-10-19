using System.Collections.Generic;
using UnityEngine;

namespace RavenIron.Cairn.Core
{
    /// <summary>
    /// PURE. Which tombstones a player's game should light.
    ///
    /// THE ONE MOD-AUTHORED LIGHT. House rule B says a landmark is player-authored, never
    /// mod-authored; the owner amended it on 2026-10-19 for exactly this, default off. A
    /// no-map run removes vanilla's death pin, and finding your body is the hardest moment in
    /// one. The light is still an object in the world (rule A): terrain hides it, it is not a
    /// bearing, and it goes out when the tombstone is emptied, because vanilla destroys an
    /// empty tombstone and the next sweep no longer finds it.
    ///
    /// Only the dead player's own tombstone is lit. Vanilla's death pin is personal, and so is
    /// this. The server sends every tombstone; each game keeps its own, by the player ID vanilla
    /// writes into the tombstone (`TombStone.Setup(name, PlayerProfile.GetPlayerID())`, read out
    /// of 1.0.20 on 2026-10-19).
    /// </summary>
    public static class TombstoneLights
    {
        /// <summary>One tombstone, as the server knows it.</summary>
        public struct Grave
        {
            /// <summary>The dead player's profile ID (`ZDOVars.s_owner`). 0 means unknown.</summary>
            public long Owner;

            /// <summary>
            /// When they died (`ZDOVars.s_timeOfDeath`). With the owner, this names ONE tombstone
            /// across pushes even while it floats: a key made from its position would change as it
            /// drifts, and the light would fade out and back in every few seconds.
            /// </summary>
            public long TimeOfDeath;

            public Vector3 Position;
        }

        /// <summary>
        /// Most tombstones one push may carry. A world with more is not a world, it is a bug or a
        /// hostile packet, and the receiver refuses it rather than drawing it.
        /// </summary>
        public const int MaxPerPush = 256;

        /// <summary>
        /// The tombstones that belong to <paramref name="me"/>. An unknown owner (0) matches no
        /// one, and neither does an unknown player: a game that does not yet know who it is must
        /// not light somebody else's grave.
        /// </summary>
        public static List<Grave> Mine(IList<Grave> all, long me)
        {
            var mine = new List<Grave>();
            if (all == null || me == 0L) return mine;

            for (int i = 0; i < all.Count; i++)
                if (all[i].Owner == me) mine.Add(all[i]);
            return mine;
        }

        /// <summary>
        /// Has the last push gone stale? The server sends nothing at all while tombstone lights
        /// are off, so a game that stopped hearing must stop drawing: otherwise turning the setting
        /// off on the server would leave every light that was on burning forever. Three missed
        /// pushes, and never sooner than 30 seconds, so one dropped packet does not blink a light.
        /// </summary>
        public static bool Stale(float lastReceived, float now, float syncSeconds)
        {
            if (lastReceived < 0f) return true;
            float allowed = syncSeconds * 3f;
            if (allowed < 30f) allowed = 30f;
            return now - lastReceived > allowed;
        }
    }
}
