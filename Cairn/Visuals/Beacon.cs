using System;
using System.Collections.Generic;
using RavenIron.Cairn.Config;
using RavenIron.Cairn.Core;
using RavenIron.Cairn.Net;
using UnityEngine;

namespace RavenIron.Cairn.Visuals
{
    /// <summary>
    /// The light on top of a cairn: the thing you actually navigate by.
    ///
    /// Client-side only, drawn from <see cref="LandmarkSync"/>'s cache and never from a
    /// networked object. At beacon range the cairn's own stone is not instantiated here, so
    /// anything waiting on the object would light up only once you had arrived.
    ///
    /// TWO RULES SHAPE EVERYTHING ELSE.
    ///
    /// 1. CONSTANT ANGULAR SIZE. A fixed-size glow shrinks to nothing with distance, which is
    ///    the opposite of what a beacon is for. The quad is scaled by range instead, so it
    ///    holds roughly the same size on screen from 50m or 500m — the way a real fire at
    ///    night reads as a point of light rather than as something that gets small.
    ///
    /// 2. IT MUST BE OCCLUDED BY THE WORLD. A glow that shines through a mountain is not a
    ///    beacon, it is a waypoint marker wearing a costume, and house rule A forbids exactly
    ///    that. Terrain hides it; a ridge between you and a cairn means you have to move.
    ///    This is the difference between navigating and being told.
    ///
    /// No assets: the glow texture is generated, and the shader is picked from a candidate
    /// chain because Valheim strips Unity's standard particle shaders. The chosen one is
    /// logged — two clients disagreeing about a visual is otherwise undiagnosable.
    /// </summary>
    public class Beacon : MonoBehaviour
    {
        /// <summary>
        /// Shaders Valheim's build might actually contain. "Particles/Standard Unlit" is
        /// CONFIRMED stripped and kept only for future Unity versions; "Sprites/Default" is
        /// the first that ships. Lifted from Ragnarok's Wrath's ParticleKit, where the same
        /// list encodes the same afternoon of nothing rendering anywhere.
        /// </summary>
        private static readonly string[] CandidateShaders =
        {
            "Particles/Standard Unlit",
            "Legacy Shaders/Particles/Additive",
            "Sprites/Default",
            "UI/Default",
            "Legacy Shaders/Particles/Alpha Blended",
        };

        private sealed class Lit
        {
            public GameObject Go;
            public Renderer Renderer;
            public Material Material;     // this beacon's own instance; destroyed with it
            public Vector3 Position;
            public float Visibility;      // 0..1, smoothed — never pops
            public bool Blocked;
            public bool Tomb;             // a tombstone light (0.11.0), drawn in its own colour
        }

        /// <summary>
        /// One light: a cairn, keyed by its place, or a tombstone (0.11.0), keyed by its owner and
        /// time of death so a floating tombstone keeps ONE light rather than a new one per metre.
        /// </summary>
        private struct LightKey : IEquatable<LightKey>
        {
            public bool Tomb;
            public LandmarkKey Place;
            public long Owner;
            public long Died;

            public static LightKey ForCairn(LandmarkKey k) => new LightKey { Place = k };

            public static LightKey ForTomb(TombstoneLights.Grave g) => new LightKey
            {
                Tomb = true,
                Owner = g.Owner,
                Died = g.TimeOfDeath,
                // A tombstone whose owner has not stamped a time of death yet is told apart by place.
                Place = g.TimeOfDeath == 0L ? LandmarkKey.FromPosition(g.Position) : default(LandmarkKey),
            };

            public bool Equals(LightKey o) => Tomb == o.Tomb && Place == o.Place && Owner == o.Owner && Died == o.Died;
            public override bool Equals(object obj) => obj is LightKey k && Equals(k);
            public override int GetHashCode()
            {
                unchecked
                {
                    int h = Place.GetHashCode();
                    h = h * 31 + Owner.GetHashCode();
                    h = h * 31 + Died.GetHashCode();
                    return Tomb ? ~h : h;
                }
            }

            public override string ToString() => Tomb ? $"TOMBSTONE (died {Died})" : Place.ToString();
        }

        private readonly Dictionary<LightKey, Lit> _lit = new Dictionary<LightKey, Lit>(16);
        private readonly List<LightKey> _scratch = new List<LightKey>(16);

        private Material _material;
        private Mesh _quad;
        private bool _failed;
        private int _seenRevision = -1;
        private float _sinceRebuild;
        private int _occlusionCursor;
        private int _occlusionMask = -1;

        /// <summary>Firelight, and the fallback when a configured colour will not parse.</summary>
        private static readonly Color DefaultEmber = new Color(1f, 0.72f, 0.35f);
        private Color _colour = DefaultEmber;
        private string _colourText;

        /// <summary>Red: a tombstone light must never be mistaken for a cairn.</summary>
        private static readonly Color DefaultTomb = new Color(1f, 0.165f, 0.165f);
        private Color _tombColour = DefaultTomb;
        private string _tombColourText;
        private int _seenTombRevision = -1;

        /// <summary>The live renderer, so the console can ask it what it is doing.</summary>
        public static Beacon Instance { get; private set; }

        private void Awake() => Instance = this;

        /// <summary>
        /// Why is that cairn dark? A beacon can be absent for four different reasons — never
        /// synced, out of range, occluded, or the renderer never started — and from outside
        /// they look identical. This says which, per beacon, in the game.
        /// </summary>
        public List<string> Describe()
        {
            var lines = new List<string>();

            if (_failed) { lines.Add("  renderer DISABLED after an error — see the log"); return lines; }
            if (_material == null) lines.Add("  (no material built yet — nothing has been drawn)");

            Camera cam = Camera.main;
            Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
            float maxDist = ModConfig.BeaconMaxDistanceMeters.Value;

            foreach (KeyValuePair<LightKey, Lit> kv in _lit)
            {
                Lit lit = kv.Value;
                float dist = cam != null ? Vector3.Distance(camPos, lit.Position) : -1f;

                string why =
                    lit.Blocked ? "HIDDEN (terrain in the way)" :
                    dist > maxDist ? $"OUT OF RANGE (>{maxDist:F0}m)" :
                    lit.Visibility > 0.01f ? "lit" : "fading";

                lines.Add(
                    $"  {kv.Key}  {dist:F0}m  vis={lit.Visibility:F2}  {why}");
            }

            if (_lit.Count == 0) lines.Add("  no beacons known — nothing has been synced to this client");
            return lines;
        }

        private void Update()
        {
            if (_failed) return;

            // Cairn lights and tombstone lights switch independently (0.11.0): a player may hide
            // one and keep the other.
            if (!ModConfig.EnableBeacons.Value && !ModConfig.ShowTombstoneLight.Value)
            {
                if (_lit.Count > 0) ClearAll();
                return;
            }

            Camera cam = Camera.main;
            if (cam == null) return;

            try
            {
                _sinceRebuild += Time.deltaTime;
                if (_seenRevision != LandmarkSync.Revision || _seenTombRevision != TombstoneSync.Revision
                    || _sinceRebuild >= 5f)
                {
                    _seenRevision = LandmarkSync.Revision;
                    _seenTombRevision = TombstoneSync.Revision;
                    _sinceRebuild = 0f;
                    Rebuild();
                }

                if (_lit.Count == 0) return;

                StepOcclusion(cam);
                Draw(cam);
            }
            catch (Exception ex)
            {
                // House rule 3: a cosmetic must never take the frame down with it, and a
                // visual that throws every frame is worse than one that is absent.
                _failed = true;
                ClearAll();
                Cairn.Log.LogError($"Beacon disabled after an error: {ex}");
            }
        }

        // ---- the set of lights -------------------------------------------------------------

        private void Rebuild()
        {
            var wanted = new HashSet<LightKey>();
            float lift = ModConfig.BeaconHeightMeters.Value;

            if (ModConfig.EnableBeacons.Value)
            {
                foreach (LandmarkSync.Beacon b in LandmarkSync.Current())
                {
                    LightKey key = LightKey.ForCairn(b.Key);
                    if (!Place(key, b.Light + Vector3.up * lift, tomb: false, wanted)) return;
                }
            }

            if (ModConfig.ShowTombstoneLight.Value)
            {
                foreach (TombstoneLights.Grave g in TombstoneSync.Mine())
                {
                    LightKey key = LightKey.ForTomb(g);
                    if (!Place(key, g.Position + Vector3.up * lift, tomb: true, wanted)) return;
                }
            }

            _scratch.Clear();
            foreach (KeyValuePair<LightKey, Lit> kv in _lit)
                if (!wanted.Contains(kv.Key)) _scratch.Add(kv.Key);

            foreach (LightKey gone in _scratch)
            {
                Discard(_lit[gone]);
                _lit.Remove(gone);
            }
        }

        /// <summary>Keep or make the light for one key. False when no material can be built.</summary>
        private bool Place(LightKey key, Vector3 position, bool tomb, HashSet<LightKey> wanted)
        {
            wanted.Add(key);

            if (!_lit.TryGetValue(key, out Lit lit))
            {
                lit = Create();
                if (lit == null) return false;    // material unavailable; Create logged it
                lit.Tomb = tomb;
                _lit[key] = lit;
            }

            lit.Position = position;
            return true;
        }

        private Lit Create()
        {
            if (_material == null)
            {
                _material = BuildMaterial();
                if (_material == null) { _failed = true; return null; }
            }
            if (_quad == null) _quad = BuildQuad();

            var go = new GameObject("cairn_beacon");
            go.transform.SetParent(transform, worldPositionStays: true);

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = _quad;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            // Its own material instance: alpha is per-beacon, because occlusion and range are.
            // Kept on the Lit so it can be destroyed with the beacon — destroying the
            // GameObject does not free a material, so every removed beacon used to leak one.
            var own = new Material(_material);
            renderer.material = own;

            return new Lit { Go = go, Renderer = renderer, Material = own, Visibility = 0f };
        }

        /// <summary>
        /// The world is ending. Drop every light now, rather than waiting for a camera and a
        /// rebuild to notice, so nothing from the last world is ever drawn in the next.
        /// </summary>
        public void ClearForWorldChange()
        {
            ClearAll();
            _seenRevision = -1;
            _seenTombRevision = -1;
            _occlusionCursor = 0;
        }

        private void ClearAll()
        {
            foreach (KeyValuePair<LightKey, Lit> kv in _lit) Discard(kv.Value);
            _lit.Clear();
        }

        private static void Discard(Lit lit)
        {
            if (lit.Go != null) Destroy(lit.Go);
            if (lit.Material != null) Destroy(lit.Material);
        }

        // ---- per-frame ---------------------------------------------------------------------

        private void Draw(Camera cam)
        {
            Vector3 camPos = cam.transform.position;
            float maxDist = ModConfig.BeaconMaxDistanceMeters.Value;
            float angular = ModConfig.BeaconAngularSize.Value;
            float minSize = ModConfig.BeaconMinSizeMeters.Value;
            float maxSize = ModConfig.BeaconMaxSizeMeters.Value;
            float fade = Mathf.Clamp01(Time.deltaTime * 4f);

            Color warm = ConfiguredColour();
            Color red = TombColour();

            foreach (KeyValuePair<LightKey, Lit> kv in _lit)
            {
                Lit lit = kv.Value;
                if (lit.Go == null) continue;

                float dist = Vector3.Distance(camPos, lit.Position);

                // Target visibility: range first, then whether the world is in the way.
                float target = dist > maxDist ? 0f : 1f;
                if (lit.Blocked) target = 0f;

                // Near the limit it thins out rather than vanishing on a step.
                if (target > 0f && dist > maxDist * 0.8f)
                    target = Mathf.InverseLerp(maxDist, maxDist * 0.8f, dist);

                lit.Visibility = Mathf.Lerp(lit.Visibility, target, fade);

                if (lit.Visibility <= 0.01f)
                {
                    if (lit.Go.activeSelf) lit.Go.SetActive(false);
                    continue;
                }
                if (!lit.Go.activeSelf) lit.Go.SetActive(true);

                // CONSTANT ANGULAR SIZE — the whole reason this is visible at range.
                float size = Mathf.Clamp(dist * angular, minSize, maxSize);

                Transform t = lit.Go.transform;
                t.position = lit.Position;
                t.rotation = Quaternion.LookRotation(lit.Position - camPos);   // billboard
                t.localScale = new Vector3(size, size, 1f);

                Color c = lit.Tomb ? red : warm;
                c.a = lit.Visibility;
                lit.Material.color = c;
            }
        }

        /// <summary>
        /// The beacon's colour, from config, re-read only when the string actually changes —
        /// parsing hex sixteen times a second for a value that almost never moves would be
        /// silly, and caching it without noticing edits would be worse.
        ///
        /// A value that will not parse falls back to the ember default and says so ONCE. The
        /// alternative is a beacon that silently draws black, which looks exactly like a
        /// beacon that is not drawing at all.
        /// </summary>
        /// <summary>The tombstone light's colour, parsed only when the setting changes; red if it will not parse.</summary>
        private Color TombColour()
        {
            string text = ModConfig.TombstoneColour.Value;
            if (text == _tombColourText) return _tombColour;

            _tombColourText = text;
            if (HexColour.TryParse(text, out float r, out float g, out float b, out float _))
            {
                _tombColour = new Color(r, g, b);
            }
            else
            {
                _tombColour = DefaultTomb;
                Cairn.Log.LogWarning($"Beacon: TombstoneColour '{text}' is not a hex colour; using red.");
            }
            return _tombColour;
        }

        private Color ConfiguredColour()
        {
            string text = ModConfig.BeaconColour.Value;
            if (text == _colourText) return _colour;

            _colourText = text;

            if (HexColour.TryParse(text, out float r, out float g, out float b, out float _))
            {
                _colour = new Color(r, g, b);
                Cairn.Log.LogInfo($"Beacon: colour set to #{text.TrimStart('#')}.");
            }
            else
            {
                _colour = DefaultEmber;
                Cairn.Log.LogWarning(
                    $"Beacon: '{text}' is not a hex colour (try FFB85A or #F80). " +
                    "Falling back to the default ember rather than drawing black.");
            }

            return _colour;
        }

        /// <summary>
        /// One raycast per frame, round-robin. A beacon behind a ridge must go dark, and
        /// checking every beacon every frame would be the most expensive thing this mod does
        /// for the least reason — nothing moves fast enough to need it.
        /// </summary>
        private void StepOcclusion(Camera cam)
        {
            if (!ModConfig.BeaconOcclusion.Value)
            {
                foreach (KeyValuePair<LightKey, Lit> kv in _lit) kv.Value.Blocked = false;
                return;
            }

            if (_occlusionMask == -1)
            {
                _occlusionMask = LayerMask.GetMask("terrain", "static_solid", "Default");
                if (_occlusionMask == 0)
                {
                    // Never silently: an unoccluded beacon is a waypoint marker, and that is
                    // the one thing this mod may not become.
                    Cairn.Log.LogWarning(
                        "Beacon: no occlusion layers resolved, so beacons will shine through " +
                        "terrain. Set BeaconOcclusion = false to accept that deliberately.");
                }
                else
                {
                    Cairn.Log.LogInfo($"Beacon: occlusion mask resolved ({_occlusionMask}).");
                }
            }

            if (_occlusionMask == 0 || _lit.Count == 0) return;

            _scratch.Clear();
            foreach (KeyValuePair<LightKey, Lit> kv in _lit) _scratch.Add(kv.Key);

            if (_occlusionCursor >= _scratch.Count) _occlusionCursor = 0;
            LightKey key = _scratch[_occlusionCursor];
            _occlusionCursor++;

            if (!_lit.TryGetValue(key, out Lit target)) return;

            Vector3 camPos = cam.transform.position;
            Vector3 toBeacon = target.Position - camPos;

            // Stop just short of the cairn: the stones themselves would otherwise block their
            // own light whenever they are loaded.
            float len = toBeacon.magnitude - 1.5f;
            if (len <= 0f) { target.Blocked = false; return; }

            target.Blocked = Physics.Raycast(
                camPos, toBeacon.normalized, len, _occlusionMask, QueryTriggerInteraction.Ignore);
        }

        // ---- generated, never shipped ------------------------------------------------------

        private static Mesh BuildQuad()
        {
            var mesh = new Mesh { name = "cairn_beacon_quad" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f), new Vector3(0.5f,  0.5f, 0f),
            };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Material BuildMaterial()
        {
            Shader shader = null;
            foreach (string name in CandidateShaders)
            {
                shader = Shader.Find(name);
                if (shader != null)
                {
                    Cairn.Log.LogInfo($"Beacon: using shader '{name}'.");
                    break;
                }
            }

            if (shader == null)
            {
                Cairn.Log.LogError(
                    "Beacon: no usable shader found — Valheim's build has stripped every " +
                    "candidate. Beacons cannot draw; the ledger is unaffected.");
                return null;
            }

            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false);
            float half = (size - 1) / 2f;

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x - half) / half;
                float dy = (y - half) / half;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - d);
                // Squared falloff plus a hot core: a flame reads as a bright point with a
                // soft halo, not as an evenly lit disc.
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a * (0.55f + 0.45f * a)));
            }
            tex.Apply();

            return new Material(shader) { mainTexture = tex };
        }

        private void OnDestroy() => ClearAll();
    }
}
