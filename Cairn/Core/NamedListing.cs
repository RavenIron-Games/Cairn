using System.Collections.Generic;
using UnityEngine;

namespace RavenIron.Cairn.Core
{
    /// <summary>
    /// PURE. Which named places to list for a player, nearest first.
    ///
    /// `cairn landmarks` read only the ledger, and a joined player's game has no ledger: it lives
    /// on the server. So on a dedicated server the command answered "no landmarks" to every
    /// player, while 0.10.1's own README and config text told them it "lists every name". Found
    /// 2026-10-18 by checking each promise those texts make against the code before upload.
    /// A player's game DOES hold the names of the lit cairns the server sends it, the same list
    /// Hugin speaks from, so that is what the command lists there.
    /// </summary>
    public static class NamedListing
    {
        /// <summary>
        /// Indices of the entries that carry a name, ordered by XZ distance from <paramref name="from"/>,
        /// ties in input order. Unnamed entries are left out: they are waymarks, not names.
        /// </summary>
        public static List<int> NamedByDistance(IList<string> names, IList<Vector3> positions, Vector3 from)
        {
            var picked = new List<int>();
            if (names == null || positions == null) return picked;

            int n = names.Count < positions.Count ? names.Count : positions.Count;
            for (int i = 0; i < n; i++)
                if (!string.IsNullOrWhiteSpace(names[i])) picked.Add(i);

            var dist = new float[n];
            for (int i = 0; i < n; i++) dist[i] = DistanceXZ(positions[i], from);

            // Stable: List.Sort is not, so ties fall back to the index explicitly.
            picked.Sort((a, b) =>
            {
                int c = dist[a].CompareTo(dist[b]);
                return c != 0 ? c : a.CompareTo(b);
            });
            return picked;
        }

        public static float DistanceXZ(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return (float)System.Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
