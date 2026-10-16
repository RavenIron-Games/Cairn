using System.Collections.Generic;
using UnityEngine;

namespace RavenIron.Cairn.Core
{
    /// <summary>
    /// PURE. Which lit cairns go into one broadcast when there are more than it may carry.
    ///
    /// Through 0.9.1 the broadcast took the ledger in insertion order and stopped at the cap,
    /// which meant the OLDEST cairns always won. Reported 2026-10-16 from a no-map playthrough:
    /// past a hundred lit cairns, every old light kept burning and every new one stayed dark —
    /// exactly the cairns a player is standing beside and waiting on. The cap's own description
    /// called it "not a limit anyone should reach"; a waymark trail reaches it.
    ///
    /// Now, over the cap, the cairns nearest a player win. A beacon past the client's draw
    /// distance (800m by default) fades to nothing anyway, so the ones dropped are the ones
    /// nobody could have seen. Under the cap NOTHING changes — same set, same order — so no
    /// world below a hundred cairns can tell this code exists.
    ///
    /// Distance is XZ-planar, like every other distance in this mod: a cairn on a headland
    /// sits tens of metres above the shore you are steering from, and that height is not
    /// "further away" in any sense that matters to whether you can see it.
    /// </summary>
    public static class BeaconSelection
    {
        /// <param name="lit">Lit cairns, in ledger order.</param>
        /// <param name="players">Where the players are. Empty means nobody to choose for.</param>
        /// <param name="max">The most one broadcast may carry.</param>
        public static List<Landmark> Choose(IList<Landmark> lit, IList<Vector3> players, int max)
        {
            var chosen = new List<Landmark>(lit.Count < max ? lit.Count : (max > 0 ? max : 0));
            if (max <= 0) return chosen;

            if (lit.Count <= max || players == null || players.Count == 0)
            {
                // Under the cap, or nobody online to rank for: ledger order, as always.
                for (int i = 0; i < lit.Count && chosen.Count < max; i++) chosen.Add(lit[i]);
                return chosen;
            }

            var ranked = new List<KeyValuePair<float, int>>(lit.Count);
            for (int i = 0; i < lit.Count; i++)
            {
                Vector3 p = lit[i].Light;
                float best = float.MaxValue;
                for (int j = 0; j < players.Count; j++)
                {
                    float dx = p.x - players[j].x;
                    float dz = p.z - players[j].z;
                    float d = dx * dx + dz * dz;
                    if (d < best) best = d;
                }
                ranked.Add(new KeyValuePair<float, int>(best, i));
            }

            // Nearest first; equal distances keep ledger order, so the set is stable from one
            // broadcast to the next and two equidistant cairns do not flicker in and out.
            ranked.Sort((a, b) => a.Key != b.Key ? a.Key.CompareTo(b.Key) : a.Value.CompareTo(b.Value));

            for (int i = 0; i < max; i++) chosen.Add(lit[ranked[i].Value]);
            return chosen;
        }
    }
}
