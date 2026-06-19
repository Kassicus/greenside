using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Splines;

namespace Greenside
{
    /// <summary>
    /// Phase 4a: generates one procedural hole.
    ///   - A seeded Spline centerline (tee -> pin) with a dogleg bend.
    ///   - A grid mesh with gentle Perlin undulation.
    ///   - Per-vertex surface (green / fairway / rough) from distance-to-centerline
    ///     and distance-to-pin; the corridor is flattened, the green flattened more.
    ///   - Flat-shaded (split-vertex face normals), one submesh + material per surface.
    ///   - A MeshCollider for physics, plus tee and pin markers; the ball is placed
    ///     on the tee.
    ///
    /// Same seed -> same hole (reproducible). Hole-out, hazards and validation come
    /// in later increments.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
    public class HoleGenerator : MonoBehaviour
    {
        public enum Surface { Fairway = 0, Green = 1, Rough = 2, Sand = 3 }

        [Header("Seed")]
        public int seed = 12345;
        [Tooltip("Pick a fresh random seed each time the scene plays.")]
        public bool randomizeSeedOnPlay = false;

        [Header("Layout (meters)")]
        public float holeLength = 320f;
        [Tooltip("Max lateral offset of the mid bend / pin (dogleg amount).")]
        public float maxDogleg = 45f;
        public float fairwayWidth = 30f;
        public float greenRadius = 12f;
        [Tooltip("Extra ground beyond the fairway on each side (the rough margin).")]
        public float roughMargin = 40f;

        [Header("Tee box")]
        [Tooltip("Radius of the flat, raised pad at the start of the hole.")]
        public float teeboxRadius = 6f;
        [Tooltip("How far the tee box sits above the surrounding fairway.")]
        public float teeboxRaise = 0.5f;
        [Tooltip("Height of the tee peg the ball sits on.")]
        public float teePegHeight = 0.35f;

        [Header("Cup")]
        [Tooltip("Capture radius of the cup at the pin (arcade-generous).")]
        public float cupRadius = 0.6f;

        [Header("Lie")]
        [Tooltip("Top-end power multiplier when playing from the rough (the club's minimum power still applies).")]
        [Range(0.2f, 1f)] public float roughPowerMultiplier = 0.6f;

        [Header("Bunkers")]
        [Tooltip("Top-end power multiplier from sand (heavier than rough).")]
        [Range(0.1f, 1f)] public float sandPowerMultiplier = 0.4f;
        [Tooltip("How deep bunkers are carved into the terrain (m).")]
        public float bunkerDepth = 2.2f;

        [Header("Surface roll (x ground-roll drag — higher stops the ball sooner)")]
        public float greenRoll = 0.4f;
        public float fairwayRoll = 1f;
        public float roughRoll = 2.5f;
        public float sandRoll = 4f;

        [Header("Hazards")]
        [Tooltip("Out-of-bounds margin inside the terrain edge (m).")]
        public float obMargin = 3f;
        [Tooltip("Chance (0-1) a hole has a water hazard.")]
        [Range(0f, 1f)] public float waterChance = 0.4f;
        [Tooltip("How deep the water basin is carved (m).")]
        public float waterCarveDepth = 3f;
        public Color waterColor = new Color(0.15f, 0.35f, 0.6f);

        [Header("Mesh")]
        [Tooltip("Grid cell size — larger = chunkier low-poly facets.")]
        public float cellSize = 4f;
        [Tooltip("Sideways jitter of grid vertices (fraction of a cell) — breaks up the regular grid into an organic layout.")]
        [Range(0f, 0.5f)] public float gridJitter = 0.4f;
        [Tooltip("Width (m) of the colour blend between surfaces — larger = softer, curvier edges.")]
        public float surfaceBlend = 6f;

        [Header("Undulation")]
        public float noiseAmplitude = 7f;
        public float noiseScale = 0.018f;
        [Range(0f, 1f)] public float fairwayFlatten = 0.45f;
        [Range(0f, 1f)] public float greenFlatten = 0.05f;
        [Tooltip("Width of the fairway->rough height blend.")]
        public float transitionBand = 8f;
        [Tooltip("Max elevation change from tee to green (m), + or - — uphill / downhill holes.")]
        public float elevationRange = 15f;

        [Header("Surface colors (placeholder until Phase 8 art pass)")]
        public Color fairwayColor = new Color(0.20f, 0.38f, 0.14f);
        public Color greenColor = new Color(0.40f, 0.70f, 0.27f);
        public Color roughColor = new Color(0.09f, 0.19f, 0.07f);
        public Color sandColor = new Color(0.82f, 0.74f, 0.52f);

        [Header("References")]
        public BallController ball;

        [Header("Hole preview")]
        [Tooltip("A Cinemachine camera (priority higher than the follow cam) that reveals " +
                 "the hole at the start, then blends to the tee follow cam.")]
        public Transform previewCamera;
        [Tooltip("Seconds to show the hole overview before blending to the follow camera.")]
        public float previewDuration = 3f;

        // --- generated state ---
        private struct Bunker { public Vector2 center; public float radius; }
        private readonly List<Bunker> _bunkers = new List<Bunker>();
        private struct Pond { public Vector2 center; public float radius; public float waterY; }
        private readonly List<Pond> _ponds = new List<Pond>();
        private float _minX, _maxX, _minZ, _maxZ;
        private readonly List<Vector3> _centerline = new List<Vector3>();
        private Vector3 _teePos;
        private Vector3 _pinPos;
        private float _teeboxHeight;
        private float _elevationChange;
        private Vector3 _teePegTop;
        private Transform _markers;
        private Mesh _colliderMesh;
        private float _previewEndTime = -1f;

        private void Start()
        {
            // If a RoundManager is present it drives every hole; otherwise build one
            // hole so the generator still works standalone.
            if (FindAnyObjectByType<RoundManager>() == null)
            {
                if (randomizeSeedOnPlay) seed = (int)(System.DateTime.Now.Ticks & 0x7fffffff);
                BuildHole(seed, holeLength);
            }
        }

        /// <summary>
        /// Build a specific hole: generate the terrain for the given seed and length,
        /// tee up the ball, set the cup, and play the preview. Called per hole by the
        /// RoundManager.
        /// </summary>
        public void BuildHole(int holeSeed, float length)
        {
            seed = holeSeed;
            holeLength = length;
            Generate();
            PlaceBallOnTee();
            if (ball != null) ball.SetHole(_pinPos, cupRadius, this);
            StartPreview();
        }

        private void Update()
        {
            // End the hole preview after its duration: disabling the higher-priority
            // preview camera lets the Cinemachine brain blend down to the follow cam.
            if (_previewEndTime > 0f && Time.time >= _previewEndTime)
            {
                if (previewCamera != null) previewCamera.gameObject.SetActive(false);
                _previewEndTime = -1f;
            }
        }

        private void StartPreview()
        {
            if (previewCamera == null || previewDuration <= 0f) return;

            // Frame the whole hole from up and behind the tee, looking toward the green.
            Vector3 dir = _pinPos - _teePos;
            dir.y = 0f;
            dir = dir.sqrMagnitude < 1e-3f ? Vector3.forward : dir.normalized;
            float len = Vector3.Distance(_teePos, _pinPos);
            Vector3 mid = (_teePos + _pinPos) * 0.5f;
            Vector3 camPos = _teePos + Vector3.up * (len * 0.35f) - dir * (len * 0.12f);

            previewCamera.SetPositionAndRotation(camPos, Quaternion.LookRotation((mid - camPos).normalized, Vector3.up));
            previewCamera.gameObject.SetActive(true);
            _previewEndTime = Time.time + previewDuration;
        }

        private void PlaceBallOnTee()
        {
            if (ball == null) return;

            float radius = 0.2f;
            var sphere = ball.GetComponent<SphereCollider>();
            if (sphere != null) radius = sphere.radius * ball.transform.lossyScale.x;

            // Rest the ball on the tee peg's flat top. The peg's primitive collider is
            // live immediately, so the ball is held from frame zero.
            ball.PlaceOnTee(_teePegTop + Vector3.up * (radius + 0.01f));
        }

        [ContextMenu("Regenerate")]
        public void Generate()
        {
            ClearGenerated();

            var rng = new System.Random(seed);

            BuildCenterline(rng);
            BuildBunkers(rng);
            BuildWater(rng);
            Mesh mesh = BuildMesh();

            GetComponent<MeshFilter>().sharedMesh = mesh;
            var meshCollider = GetComponent<MeshCollider>();
            meshCollider.sharedMesh = null;            // force the collision shape to rebuild
            meshCollider.convex = false;
            meshCollider.sharedMesh = _colliderMesh;
            GetComponent<MeshRenderer>().sharedMaterial = TerrainMaterial();

            BuildMarkers();
        }

        // Existing components keep their old serialized colors after a script change,
        // so this re-applies the defaults with one click.
        [ContextMenu("Reset Surface Colors to Defaults")]
        private void ResetSurfaceColors()
        {
            fairwayColor = new Color(0.20f, 0.38f, 0.14f);
            greenColor = new Color(0.40f, 0.70f, 0.27f);
            roughColor = new Color(0.09f, 0.19f, 0.07f);
        }

        // Applies the larger terrain/bunker sizing to an existing component (serialized
        // fields keep their old values after a script default change).
        [ContextMenu("Apply Default Sizing")]
        private void ApplyDefaultSizing()
        {
            noiseAmplitude = 7f;
            fairwayFlatten = 0.45f;
            bunkerDepth = 2.2f;
            elevationRange = 15f;
            greenRoll = 0.4f;
            fairwayRoll = 1f;
            roughRoll = 2.5f;
            sandRoll = 4f;
            obMargin = 3f;
            waterChance = 0.4f;
            waterCarveDepth = 3f;
            sandColor = new Color(0.82f, 0.74f, 0.52f);
            waterColor = new Color(0.15f, 0.35f, 0.6f);
        }

        // ---- centerline ---------------------------------------------------------

        private void BuildCenterline(System.Random rng)
        {
            float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            // Tee at the origin so the default aim (+Z) points down the hole.
            float midX = Range(-maxDogleg, maxDogleg);
            float pinX = Range(-maxDogleg * 0.5f, maxDogleg * 0.5f);

            var spline = new Spline();
            spline.Add(new BezierKnot(new float3(0f, 0f, 0f)));
            spline.Add(new BezierKnot(new float3(midX, 0f, holeLength * 0.5f)));
            spline.Add(new BezierKnot(new float3(pinX, 0f, holeLength)));
            for (int i = 0; i < spline.Count; i++)
                spline.SetTangentMode(i, TangentMode.AutoSmooth);

            // Park the spline on a SplineContainer so it's visible/inspectable in-scene.
            var go = new GameObject("Hole Centerline");
            go.transform.SetParent(transform, false);
            go.AddComponent<SplineContainer>().Spline = spline;

            // Sample the spline into a polyline we can measure distance against.
            _centerline.Clear();
            int samples = Mathf.Max(16, Mathf.CeilToInt(holeLength / 4f));
            for (int i = 0; i <= samples; i++)
            {
                float3 p = spline.EvaluatePosition(i / (float)samples);
                _centerline.Add(new Vector3(p.x, p.y, p.z));
            }

            _teePos = _centerline[0];
            _pinPos = _centerline[_centerline.Count - 1];

            // Tee box: a flat, slightly raised pad at the start. Precompute its height
            // from the surrounding fairway height so HeightAt can return it directly
            // (and avoid recursion).
            float off = seed * 0.001f;
            float teeNoise = (Mathf.PerlinNoise(_teePos.x * noiseScale + off, _teePos.z * noiseScale + off) * 2f - 1f) * noiseAmplitude;
            _teeboxHeight = teeNoise * fairwayFlatten + teeboxRaise;

            // Seeded tee->green elevation change (uphill/downhill hole).
            _elevationChange = Range(-elevationRange, elevationRange);

            _teePos.y = _teeboxHeight;
            _pinPos.y = HeightAt(_pinPos.x, _pinPos.z);
        }

        // ---- bunkers ------------------------------------------------------------

        private void BuildBunkers(System.Random rng)
        {
            _bunkers.Clear();
            float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            void AddCircle(Vector2 c, float r) => _bunkers.Add(new Bunker { center = c, radius = r });

            // Fairway bunkers: round or bean-shaped, straddling the corridor edge.
            int fairwayCount = Mathf.RoundToInt(Mathf.Clamp(holeLength / 170f, 1f, 4f));
            for (int i = 0; i < fairwayCount; i++)
            {
                float t = Range(0.35f, 0.85f);
                Vector3 cp = CenterlinePoint(t);
                Vector2 dir = CenterlineDir(t);
                Vector2 perp = Perp(dir);
                float side = rng.NextDouble() < 0.5 ? -1f : 1f;
                float radius = Range(8f, 15f);
                Vector2 center = new Vector2(cp.x, cp.z) + perp * (side * (fairwayWidth * 0.5f + Range(-2f, 8f)));

                if (rng.NextDouble() < 0.5)
                {
                    AddCircle(center, radius);                                   // round
                }
                else
                {
                    // Bean: three circles along a gently curved axis.
                    Vector2 axis = rng.NextDouble() < 0.5 ? perp : dir;
                    Vector2 cross = Perp(axis);
                    float len = radius * Range(0.7f, 1.1f);
                    float bend = Range(-0.4f, 0.4f) * radius;
                    AddCircle(center - axis * len + cross * (bend * 0.3f), radius * 0.7f);
                    AddCircle(center + cross * bend, radius);
                    AddCircle(center + axis * len + cross * (bend * 0.3f), radius * 0.65f);
                }
            }

            // Greenside bunkers: a narrow arc of circles wrapping part of the green.
            int greenCount = rng.NextDouble() < 0.5 ? 1 : 2;
            for (int i = 0; i < greenCount; i++)
            {
                float startAng = Range(0f, Mathf.PI * 2f);
                float span = Range(0.6f, 1.5f);                                 // ~35-85 deg of wrap
                float r = Range(3.5f, 6f);                                      // narrow
                float dist = greenRadius + r + Range(0.5f, 2.5f);
                int blobs = Mathf.Max(2, Mathf.RoundToInt(span / 0.32f));
                for (int b = 0; b < blobs; b++)
                {
                    float a = startAng + span * (b / (float)(blobs - 1));
                    AddCircle(new Vector2(_pinPos.x + Mathf.Cos(a) * dist, _pinPos.z + Mathf.Sin(a) * dist), r);
                }
            }
        }

        private void BuildWater(System.Random rng)
        {
            _ponds.Clear();
            if (rng.NextDouble() > waterChance) return;

            // Try a few placements; commit one only if no circle touches a bunker
            // (water and sand must never overlap). Otherwise the hole has no water.
            for (int attempt = 0; attempt < 6; attempt++)
            {
                var candidate = WaterCandidate(rng);
                bool touches = false;
                for (int i = 0; i < candidate.Count; i++)
                    if (TouchesBunker(candidate[i].center, candidate[i].radius)) { touches = true; break; }
                if (!touches) { _ponds.AddRange(candidate); return; }
            }
        }

        // Builds the water circles for a random style (pond / crossing / meandering
        // river). Each circle takes its own surface level so streams follow the terrain.
        private List<Pond> WaterCandidate(System.Random rng)
        {
            float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var list = new List<Pond>();
            void Add(Vector2 c, float r) =>
                list.Add(new Pond { center = c, radius = r, waterY = NaturalHeight(c.x, c.y) - 0.6f });

            double style = rng.NextDouble();
            if (style < 0.5)
            {
                // Pond: a small cluster off the fairway.
                float t = Range(0.35f, 0.8f);
                Vector3 cp = CenterlinePoint(t);
                Vector2 perp = Perp(CenterlineDir(t));
                float side = rng.NextDouble() < 0.5 ? -1f : 1f;
                Vector2 center = new Vector2(cp.x, cp.z) + perp * (side * (fairwayWidth * 0.5f + Range(-4f, 6f)));
                float r = Range(10f, 18f);
                Add(center, r);
                Add(center + perp * (side * r * 0.6f), r * 0.7f);
            }
            else if (style < 0.78)
            {
                // Crossing river: a band of circles straight across the hole.
                float t = Range(0.3f, 0.75f);
                Vector3 cp = CenterlinePoint(t);
                Vector2 perp = Perp(CenterlineDir(t));
                float r = Range(5f, 8f);
                float halfSpan = fairwayWidth * 0.5f + roughMargin * 0.6f;
                int n = Mathf.Max(3, Mathf.RoundToInt(halfSpan * 2f / (r * 1.2f)));
                for (int i = 0; i < n; i++)
                {
                    float u = Mathf.Lerp(-halfSpan, halfSpan, i / (float)(n - 1));
                    Add(new Vector2(cp.x, cp.z) + perp * u, r);
                }
            }
            else
            {
                // Meandering river: a band roughly along the hole with lateral wiggle.
                float t0 = Range(0.25f, 0.45f);
                float t1 = t0 + Range(0.25f, 0.4f);
                float r = Range(4f, 7f);
                float baseSide = (rng.NextDouble() < 0.5 ? -1f : 1f) * (fairwayWidth * 0.5f + Range(2f, 8f));
                float wiggleAmp = Range(4f, 10f);
                float phase = Range(0f, Mathf.PI * 2f);
                int n = Mathf.Max(4, Mathf.RoundToInt((t1 - t0) * holeLength / (r * 1.2f)));
                for (int i = 0; i < n; i++)
                {
                    float t = Mathf.Lerp(t0, t1, i / (float)(n - 1));
                    Vector3 cp = CenterlinePoint(t);
                    Vector2 perp = Perp(CenterlineDir(t));
                    float lateral = baseSide + Mathf.Sin(phase + i * 0.8f) * wiggleAmp;
                    Add(new Vector2(cp.x, cp.z) + perp * lateral, r);
                }
            }
            return list;
        }

        private static Vector2 Perp(Vector2 dir) => new Vector2(-dir.y, dir.x);

        // True if a circle comes within a margin of any bunker circle.
        private bool TouchesBunker(Vector2 c, float r)
        {
            const float margin = 4f;
            for (int i = 0; i < _bunkers.Count; i++)
                if (Vector2.Distance(c, _bunkers[i].center) < r + _bunkers[i].radius + margin) return true;
            return false;
        }

        private Vector3 CenterlinePoint(float t)
        {
            if (_centerline.Count == 0) return _teePos;
            float f = Mathf.Clamp01(t) * (_centerline.Count - 1);
            int i = Mathf.Min(Mathf.FloorToInt(f), _centerline.Count - 2);
            return Vector3.Lerp(_centerline[i], _centerline[i + 1], f - i);
        }

        private Vector2 CenterlineDir(float t)
        {
            if (_centerline.Count < 2) return Vector2.up;
            int i = Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(t) * (_centerline.Count - 1)), 0, _centerline.Count - 2);
            Vector3 d = _centerline[i + 1] - _centerline[i];
            Vector2 dir = new Vector2(d.x, d.z);
            return dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.up;
        }

        private bool InBunker(float x, float z)
        {
            for (int i = 0; i < _bunkers.Count; i++)
                if (HorizDist(x, z, _bunkers[i].center.x, _bunkers[i].center.y) <= _bunkers[i].radius) return true;
            return false;
        }

        // Downward carve at a point (deepest overlapping bunker), bowl-shaped.
        private float BunkerDepression(float x, float z)
        {
            float deepest = 0f;
            for (int i = 0; i < _bunkers.Count; i++)
            {
                float d = HorizDist(x, z, _bunkers[i].center.x, _bunkers[i].center.y);
                if (d < _bunkers[i].radius)
                {
                    float bowl = Smooth01(0f, 1f, 1f - d / _bunkers[i].radius);
                    deepest = Mathf.Max(deepest, bunkerDepth * bowl);
                }
            }
            return deepest;
        }

        // Smooth 0..1 sand presence for colour blending.
        private float BunkerFactor(float x, float z)
        {
            float f = 0f;
            for (int i = 0; i < _bunkers.Count; i++)
            {
                float d = HorizDist(x, z, _bunkers[i].center.x, _bunkers[i].center.y);
                f = Mathf.Max(f, 1f - Smooth01(_bunkers[i].radius - surfaceBlend, _bunkers[i].radius + surfaceBlend, d));
            }
            return f;
        }

        // ---- mesh ---------------------------------------------------------------

        private Mesh BuildMesh()
        {
            // Grid bounds = centerline bounding box + corridor + margin.
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var p in _centerline)
            {
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
            }
            float pad = fairwayWidth * 0.5f + roughMargin;
            minX -= pad; maxX += pad; minZ -= pad; maxZ += pad;
            _minX = minX; _maxX = maxX; _minZ = minZ; _maxZ = maxZ;

            int cols = Mathf.Max(1, Mathf.CeilToInt((maxX - minX) / cellSize));
            int rows = Mathf.Max(1, Mathf.CeilToInt((maxZ - minZ) / cellSize));

            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var colors = new List<Color>();
            var tris = new List<int>();
            var colliderTris = new List<int>();

            void AddTri(Vector3 a, Vector3 b, Vector3 c)
            {
                int i0 = verts.Count;
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                if (n.y < 0f) n = -n; // terrain faces up; keep lighting correct
                verts.Add(a); verts.Add(b); verts.Add(c);
                normals.Add(n); normals.Add(n); normals.Add(n);
                // Smooth per-vertex colour from the surface field -> curvy boundaries
                // (split vertices keep the lighting faceted).
                colors.Add(ColorAt(a.x, a.z)); colors.Add(ColorAt(b.x, b.z)); colors.Add(ColorAt(c.x, c.z));
                tris.Add(i0); tris.Add(i0 + 1); tris.Add(i0 + 2);
                // Collider gets BOTH windings so the ball can't fall through a back face.
                colliderTris.Add(i0); colliderTris.Add(i0 + 1); colliderTris.Add(i0 + 2);
                colliderTris.Add(i0); colliderTris.Add(i0 + 2); colliderTris.Add(i0 + 1);
            }

            Vector3 Corner(int i, int j)
            {
                float bx = minX + i * cellSize;
                float bz = minZ + j * cellSize;
                float x = bx, z = bz;
                // Smooth Perlin jitter, deterministic per grid node (so shared corners
                // still match), breaks the regular grid into an organic layout.
                if (gridJitter > 0f)
                {
                    x += (Mathf.PerlinNoise(bx * 0.25f + 11.3f, bz * 0.25f + 4.7f) - 0.5f) * cellSize * gridJitter;
                    z += (Mathf.PerlinNoise(bx * 0.25f + 91.7f, bz * 0.25f + 23.1f) - 0.5f) * cellSize * gridJitter;
                }
                return new Vector3(x, HeightAt(x, z), z);
            }

            for (int j = 0; j < rows; j++)
            {
                for (int i = 0; i < cols; i++)
                {
                    Vector3 c00 = Corner(i, j);
                    Vector3 c10 = Corner(i + 1, j);
                    Vector3 c11 = Corner(i + 1, j + 1);
                    Vector3 c01 = Corner(i, j + 1);
                    AddTri(c00, c01, c11);
                    AddTri(c00, c11, c10);
                }
            }

            var indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;

            var mesh = new Mesh { name = "Hole", indexFormat = indexFormat };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.subMeshCount = 1;
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();

            // Dedicated double-sided collision mesh.
            _colliderMesh = new Mesh { name = "Hole Collider", indexFormat = indexFormat };
            _colliderMesh.SetVertices(verts);
            _colliderMesh.SetTriangles(colliderTris, 0);
            _colliderMesh.RecalculateBounds();

            return mesh;
        }

        // ---- surface + height ---------------------------------------------------

        private Surface SurfaceAt(float x, float z)
        {
            if (HorizDist(x, z, _pinPos.x, _pinPos.z) <= greenRadius) return Surface.Green;
            if (InBunker(x, z)) return Surface.Sand;
            if (DistanceToCenterline(x, z) <= fairwayWidth * 0.5f) return Surface.Fairway;
            return Surface.Rough;
        }

        /// <summary>World position of the pin / cup.</summary>
        public Vector3 PinPosition => _pinPos;

        /// <summary>The surface under a world position (XZ).</summary>
        public Surface SurfaceAtWorld(Vector3 worldPos) => SurfaceAt(worldPos.x, worldPos.z);

        /// <summary>Top-end power multiplier for the lie at a world position.</summary>
        public float PowerMultiplierAt(Vector3 worldPos)
        {
            switch (SurfaceAt(worldPos.x, worldPos.z))
            {
                case Surface.Rough: return roughPowerMultiplier;
                case Surface.Sand: return sandPowerMultiplier;
                default: return 1f;
            }
        }

        /// <summary>Ground-roll drag multiplier for the lie at a world position.</summary>
        public float RollFactorAt(Vector3 worldPos)
        {
            switch (SurfaceAt(worldPos.x, worldPos.z))
            {
                case Surface.Green: return greenRoll;
                case Surface.Rough: return roughRoll;
                case Surface.Sand: return sandRoll;
                default: return fairwayRoll;
            }
        }

        /// <summary>True if the position is outside the playable terrain.</summary>
        public bool IsOutOfBounds(Vector3 p)
            => p.x < _minX + obMargin || p.x > _maxX - obMargin
            || p.z < _minZ + obMargin || p.z > _maxZ - obMargin;

        /// <summary>True if the ball has dropped below the surface of a water hazard.</summary>
        public bool IsInWater(Vector3 p)
        {
            for (int i = 0; i < _ponds.Count; i++)
                if (HorizDist(p.x, p.z, _ponds[i].center.x, _ponds[i].center.y) <= _ponds[i].radius
                    && p.y <= _ponds[i].waterY)
                    return true;
            return false;
        }

        // Smoothly blended surface colour at a point, for curvy (non-blocky) edges.
        private Color ColorAt(float x, float z)
        {
            float green = 1f - Smooth01(greenRadius - surfaceBlend, greenRadius + surfaceBlend,
                                        HorizDist(x, z, _pinPos.x, _pinPos.z));
            float fairway = 1f - Smooth01(fairwayWidth * 0.5f - surfaceBlend, fairwayWidth * 0.5f + surfaceBlend,
                                          DistanceToCenterline(x, z));
            Color c = Color.Lerp(roughColor, fairwayColor, fairway);
            c = Color.Lerp(c, greenColor, green);
            return Color.Lerp(c, sandColor, BunkerFactor(x, z));
        }

        private static float Smooth01(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / Mathf.Max(1e-4f, edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        private float HeightAt(float x, float z)
            => NaturalHeight(x, z) - BunkerDepression(x, z) - WaterCarve(x, z);

        // Surface height (elevation + undulation), before any carved hazards. Used to
        // set water levels from the surrounding rim.
        private float NaturalHeight(float x, float z)
        {
            // Flat, raised tee box at the start of the hole.
            if (HorizDist(x, z, _teePos.x, _teePos.z) <= teeboxRadius) return _teeboxHeight;

            // Perlin hills, with a seeded offset so different seeds undulate differently.
            float off = seed * 0.001f;
            float n = Mathf.PerlinNoise(x * noiseScale + off, z * noiseScale + off) * 2f - 1f;
            float baseH = n * noiseAmplitude;

            float h, baseElevation;
            float dPin = HorizDist(x, z, _pinPos.x, _pinPos.z);
            if (dPin <= greenRadius)
            {
                h = baseH * greenFlatten;
                baseElevation = _elevationChange;                          // green sits flat at the pin's elevation
            }
            else
            {
                // Blend fairway flatten -> full rough amplitude across the transition band.
                float d2c = DistanceToCenterline(x, z);
                float edge = fairwayWidth * 0.5f;
                float t = Mathf.Clamp01((d2c - edge) / Mathf.Max(0.01f, transitionBand));
                float amp = Mathf.Lerp(fairwayFlatten, 1f, t);
                h = baseH * amp;
                baseElevation = _elevationChange * CenterlineProgress(x, z); // ramp tee -> green
            }

            return h + baseElevation;
        }

        // Downward carve for water basins (bowl-shaped), like bunkers but deeper.
        private float WaterCarve(float x, float z)
        {
            float deepest = 0f;
            for (int i = 0; i < _ponds.Count; i++)
            {
                float d = HorizDist(x, z, _ponds[i].center.x, _ponds[i].center.y);
                if (d < _ponds[i].radius)
                {
                    float bowl = Smooth01(0f, 1f, 1f - d / _ponds[i].radius);
                    deepest = Mathf.Max(deepest, waterCarveDepth * bowl);
                }
            }
            return deepest;
        }

        // Progress (0 at tee, 1 at pin) of the nearest point on the centerline.
        private float CenterlineProgress(float x, float z)
        {
            float best = float.MaxValue, bestT = 0f;
            var p = new Vector2(x, z);
            int n = _centerline.Count;
            for (int i = 0; i < n - 1; i++)
            {
                var a = new Vector2(_centerline[i].x, _centerline[i].z);
                var b = new Vector2(_centerline[i + 1].x, _centerline[i + 1].z);
                Vector2 ab = b - a;
                float len2 = ab.sqrMagnitude;
                float seg = len2 < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
                float d = Vector2.Distance(p, a + seg * ab);
                if (d < best) { best = d; bestT = (i + seg) / Mathf.Max(1, n - 1); }
            }
            return bestT;
        }

        private float DistanceToCenterline(float x, float z)
        {
            // Min distance (in XZ) from the point to the sampled centerline segments.
            float best = float.MaxValue;
            var p = new Vector2(x, z);
            for (int i = 0; i < _centerline.Count - 1; i++)
            {
                var a = new Vector2(_centerline[i].x, _centerline[i].z);
                var b = new Vector2(_centerline[i + 1].x, _centerline[i + 1].z);
                best = Mathf.Min(best, DistToSegment(p, a, b));
            }
            return best;
        }

        private static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            return Vector2.Distance(p, a + t * ab);
        }

        private static float HorizDist(float x1, float z1, float x2, float z2)
            => Mathf.Sqrt((x1 - x2) * (x1 - x2) + (z1 - z2) * (z1 - z2));

        // ---- materials + markers ------------------------------------------------

        private static Material MakeMaterial(Color c)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", c);
            mat.SetFloat("_Smoothness", 0.05f);
            mat.SetFloat("_Cull", 0f); // double-sided: winding can't hide the terrain
            return mat;
        }

        private static Material TerrainMaterial()
        {
            // Custom vertex-colour flat-shading shader; fall back to URP/Lit if it
            // failed to compile (terrain still renders, just without the blend).
            Shader shader = Shader.Find("Greenside/TerrainVertexColor");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            return new Material(shader);
        }

        private void BuildMarkers()
        {
            if (_markers != null) SafeDestroy(_markers.gameObject);
            _markers = new GameObject("Markers").transform;
            _markers.SetParent(transform, false);

            // Pin: a thin pole with a small flag near the top (no collider).
            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.name = "Pin";
            pole.GetComponent<Collider>().enabled = false;
            pole.transform.SetParent(_markers, false);
            pole.transform.position = _pinPos + Vector3.up * 1.5f;
            pole.transform.localScale = new Vector3(0.12f, 1.5f, 0.12f);
            pole.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(0.92f, 0.92f, 0.92f));

            var flag = GameObject.CreatePrimitive(PrimitiveType.Cube);
            flag.name = "Flag";
            flag.GetComponent<Collider>().enabled = false;
            flag.transform.SetParent(pole.transform, false);
            flag.transform.localScale = new Vector3(6f, 0.5f, 0.05f);
            flag.transform.localPosition = new Vector3(3f, 0.7f, 0f);
            flag.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(0.95f, 0.85f, 0.1f));

            // Cup: a dark disk at the pin marking the hole (collider off — cosmetic).
            var cup = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cup.name = "Cup";
            cup.GetComponent<Collider>().enabled = false;
            cup.transform.SetParent(_markers, false);
            cup.transform.position = _pinPos + Vector3.up * 0.02f;
            cup.transform.localScale = new Vector3(cupRadius * 2f, 0.02f, cupRadius * 2f);
            cup.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(0.04f, 0.04f, 0.04f));

            // Water hazards: a flat blue disk at each pond's surface level.
            for (int i = 0; i < _ponds.Count; i++)
            {
                var water = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                water.name = "Water";
                water.GetComponent<Collider>().enabled = false;
                water.transform.SetParent(_markers, false);
                water.transform.position = new Vector3(_ponds[i].center.x, _ponds[i].waterY, _ponds[i].center.y);
                float d = _ponds[i].radius * 0.9f * 2f;
                water.transform.localScale = new Vector3(d, 0.05f, d);
                water.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(waterColor);
            }

            // Tee peg: a small box with a flat top the ball sits on. Its collider is a
            // primitive (live immediately, no mesh cooking), so the ball rests on it
            // from the first frame — sidestepping the terrain collider's cook timing.
            var peg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            peg.name = "Tee";
            peg.GetComponent<Collider>().enabled = false;
            peg.transform.SetParent(_markers, false);
            const float pegWidth = 0.3f;
            peg.transform.position = new Vector3(_teePos.x, _teeboxHeight + teePegHeight * 0.5f, _teePos.z);
            peg.transform.localScale = new Vector3(pegWidth, teePegHeight, pegWidth);
            peg.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(0.8f, 0.12f, 0.12f));
            _teePegTop = new Vector3(_teePos.x, _teeboxHeight + teePegHeight, _teePos.z);
        }

        private void ClearGenerated()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                SafeDestroy(transform.GetChild(i).gameObject);
            _markers = null;
            _bunkers.Clear();
            _ponds.Clear();
        }

        private static void SafeDestroy(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }
    }
}
