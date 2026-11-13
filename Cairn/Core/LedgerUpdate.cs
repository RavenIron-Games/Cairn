using System;
using System.Collections.Generic;
using UnityEngine;

namespace RavenIron.Cairn.Core
{
    /// <summary>
    /// PURE (over the ledger). Turn a set of stones and named signs into lit cairns and named
    /// places, inside a SCOPE.
    ///
    /// This was the body of `LandmarkSystem.CompleteRotation`, and it still is: the full sweep
    /// calls it with a scope covering the whole world. Since 0.12.0 the quick check calls it too,
    /// with a scope of a few dozen metres around the spot where a player just placed a stone or
    /// wrote a sign, so the light comes on in seconds without the server searching the world
    /// again. One copy of the rules, two callers.
    ///
    /// THE SCOPE IS WHAT MAKES A PARTIAL PICTURE SAFE. Piles, signs and ledger rows outside it are
    /// left exactly as they are: never written, never pruned. Inside it the caller promises the
    /// stones and signs it passed are COMPLETE, so a row in scope that is not seen is really gone.
    /// Pairing still runs over everything passed in, so a sign near the edge of the scope goes to
    /// the same pile it would in a world sweep.
    /// </summary>
    public static class LedgerUpdate
    {
        public struct Result
        {
            public int Piles;      // piles found in scope
            public int Skipped;    // piles found OUTSIDE the scope, left exactly as they were
            public int Changed;    // ledger rows added or altered
            public int Pruned;     // rows removed
            public int Carried;    // histories carried across a crown drift
        }

        public static Result Apply(
            IList<Vector3> stones,
            IList<Vector3> signPositions, IList<string> signNames, IList<string> signAuthors,
            Func<Vector3, bool> inScope,
            bool allowPrune,
            long nowUtcTicks)
        {
            var result = new Result();

            List<PileDetection.Pile> piles = PileDetection.Find(
                stones,
                Config.ModConfig.PileLinkMeters.Value,
                Config.ModConfig.PileMinPieces.Value,
                Config.ModConfig.PileMaxPieces.Value,
                Config.ModConfig.PileMaxExtentMeters.Value);

            var seen = new HashSet<LandmarkKey>();
            var claimedSign = new bool[signPositions.Count];
            var newUnnamedPiles = new List<LandmarkKey>(8);

            // Each sign names at most one pile, the nearest, over EVERYTHING passed in: a sign at
            // the edge of the scope must not go to a different pile than a world sweep would give it.
            var pileTops = new List<Vector3>(piles.Count);
            foreach (PileDetection.Pile pile in piles) pileTops.Add(pile.Top);
            int[] signFor = PileDetection.PairSigns(pileTops, signPositions, Config.ModConfig.LandmarkPairMeters.Value);

            for (int p = 0; p < piles.Count; p++)
            {
                PileDetection.Pile pile = piles[p];
                int sign = signFor[p];

                // A sign claimed by a pile OUTSIDE the scope is still claimed: it must not be
                // written as a named place with no light just because its pile was not in scope.
                if (sign >= 0) claimedSign[sign] = true;
                if (!inScope(pile.Top)) { result.Skipped++; continue; }

                result.Piles++;

                // A named sign is the landmark's identity when one is in reach: the sign does not
                // move, the pile's centroid does.
                LandmarkKey key = sign >= 0
                    ? LandmarkKey.FromPosition(signPositions[sign])
                    : LandmarkKey.FromPosition(pile.Top);

                string name = sign >= 0 ? signNames[sign] : "";
                string author = sign >= 0 ? signAuthors[sign] : SignReading.UnknownAuthor;

                if (LandmarkStore.Upsert(key, name, author, true, pile.Top, nowUtcTicks)) result.Changed++;
                seen.Add(key);
                if (sign < 0) newUnnamedPiles.Add(key);
            }

            // Named signs no pile has claimed: a place with a name and no light.
            for (int i = 0; i < signPositions.Count; i++)
            {
                if (claimedSign[i]) continue;
                if (!inScope(signPositions[i])) continue;

                LandmarkKey key = LandmarkKey.FromPosition(signPositions[i]);
                if (seen.Contains(key)) continue;

                if (LandmarkStore.Upsert(key, signNames[i], signAuthors[i], false, default, nowUtcTicks)) result.Changed++;
                seen.Add(key);
            }

            if (!allowPrune) return result;

            float drift = Config.ModConfig.PileDriftMeters.Value;
            foreach (Landmark landmark in LandmarkStore.Snapshot())
            {
                if (seen.Contains(landmark.Key)) continue;
                if (!inScope(KeyPosition(landmark.Key))) continue;

                // An unnamed cairn is identified by its own crown, and a crown is a computed
                // centroid: add a stone and it shifts. When it rounds to a new key, its history
                // belongs to the row replacing it (live on 2026-09-02: a cairn drifted 0.8 m and
                // was re-founded with its FirstSeen erased). Named landmarks are keyed on a sign,
                // and signs do not move.
                if (drift > 0f && !landmark.IsNamed && landmark.HasPile)
                {
                    int heir = PileDetection.NearestKey(landmark.Key, newUnnamedPiles, drift);
                    if (heir >= 0 && LandmarkStore.CarryHistory(newUnnamedPiles[heir], landmark.FirstSeenUtcTicks))
                        result.Carried++;
                }

                if (LandmarkStore.Remove(landmark.Key)) result.Pruned++;   // Snapshot is a copy
            }

            return result;
        }

        /// <summary>Where a ledger key sits, in metres.</summary>
        public static Vector3 KeyPosition(LandmarkKey key) => new Vector3(key.X, key.Y, key.Z);

        /// <summary>Ground distance, height ignored, as every distance in this mod.</summary>
        public static float DistanceXZ(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
