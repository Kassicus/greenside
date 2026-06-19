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
        public enum Surface { Fairway = 0, Green = 1, Rough = 2 }

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

        [Header("Mesh")]
        [Tooltip("Grid cell size — larger = chunkier low-poly facets.")]
        public float cellSize = 4f;

        [Header("Undulation")]
        public float noiseAmplitude = 2.5f;
        public float noiseScale = 0.018f;
        [Range(0f, 1f)] public float fairwayFlatten = 0.25f;
        [Range(0f, 1f)] public float greenFlatten = 0.05f;
        [Tooltip("Width of the fairway->rough height blend.")]
        public float transitionBand = 8f;

        [Header("Surface colors (placeholder until Phase 8 art pass)")]
        public Color fairwayColor = new Color(0.30f, 0.55f, 0.20f);
        public Color greenColor = new Color(0.40f, 0.70f, 0.27f);
        public Color roughColor = new Color(0.20f, 0.38f, 0.14f);

        [Header("References")]
        public BallController ball;

        [Header("Hole preview")]
        [Tooltip("A Cinemachine camera (priority higher than the follow cam) that reveals " +
                 "the hole at the start, then blends to the tee follow cam.")]
        public Transform previewCamera;
        [Tooltip("Seconds to show the hole overview before blending to the follow camera.")]
        public float previewDuration = 3f;

        // --- generated state ---
        private readonly List<Vector3> _centerline = new List<Vector3>();
        private Vector3 _teePos;
        private Vector3 _pinPos;
        private float _teeboxHeight;
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
            if (ball != null) ball.SetHole(_pinPos, cupRadius);
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
            Mesh mesh = BuildMesh();

            GetComponent<MeshFilter>().sharedMesh = mesh;
            var meshCollider = GetComponent<MeshCollider>();
            meshCollider.sharedMesh = null;            // force the collision shape to rebuild
            meshCollider.convex = false;
            meshCollider.sharedMesh = _colliderMesh;
            GetComponent<MeshRenderer>().sharedMaterials = new[]
            {
                MakeMaterial(fairwayColor),
                MakeMaterial(greenColor),
                MakeMaterial(roughColor),
            };

            BuildMarkers();
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

            _teePos.y = _teeboxHeight;
            _pinPos.y = HeightAt(_pinPos.x, _pinPos.z);
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

            int cols = Mathf.Max(1, Mathf.CeilToInt((maxX - minX) / cellSize));
            int rows = Mathf.Max(1, Mathf.CeilToInt((maxZ - minZ) / cellSize));

            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var subTris = new List<int>[3] { new List<int>(), new List<int>(), new List<int>() };
            var colliderTris = new List<int>();

            void AddTri(Vector3 a, Vector3 b, Vector3 c, int surf)
            {
                int i0 = verts.Count;
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                if (n.y < 0f) n = -n; // terrain faces up; keep lighting correct
                verts.Add(a); verts.Add(b); verts.Add(c);
                normals.Add(n); normals.Add(n); normals.Add(n);
                subTris[surf].Add(i0); subTris[surf].Add(i0 + 1); subTris[surf].Add(i0 + 2);
                // Collider gets BOTH windings so the ball can't fall through a back face.
                colliderTris.Add(i0); colliderTris.Add(i0 + 1); colliderTris.Add(i0 + 2);
                colliderTris.Add(i0); colliderTris.Add(i0 + 2); colliderTris.Add(i0 + 1);
            }

            Vector3 Corner(int i, int j)
            {
                float x = minX + i * cellSize;
                float z = minZ + j * cellSize;
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

                    Vector3 center = (c00 + c10 + c11 + c01) * 0.25f;
                    int surf = (int)SurfaceAt(center.x, center.z);

                    AddTri(c00, c01, c11, surf);
                    AddTri(c00, c11, c10, surf);
                }
            }

            var indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;

            var mesh = new Mesh { name = "Hole", indexFormat = indexFormat };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.subMeshCount = 3;
            for (int s = 0; s < 3; s++)
                mesh.SetTriangles(subTris[s], s);
            mesh.RecalculateBounds();

            // Dedicated double-sided collision mesh (single submesh).
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
            if (DistanceToCenterline(x, z) <= fairwayWidth * 0.5f) return Surface.Fairway;
            return Surface.Rough;
        }

        /// <summary>The surface under a world position (XZ).</summary>
        public Surface SurfaceAtWorld(Vector3 worldPos) => SurfaceAt(worldPos.x, worldPos.z);

        /// <summary>Top-end power multiplier for the lie at a world position.</summary>
        public float PowerMultiplierAt(Vector3 worldPos)
            => SurfaceAt(worldPos.x, worldPos.z) == Surface.Rough ? roughPowerMultiplier : 1f;

        private float HeightAt(float x, float z)
        {
            // Flat, raised tee box at the start of the hole.
            if (HorizDist(x, z, _teePos.x, _teePos.z) <= teeboxRadius) return _teeboxHeight;

            // Perlin hills, with a seeded offset so different seeds undulate differently.
            float off = seed * 0.001f;
            float n = Mathf.PerlinNoise(x * noiseScale + off, z * noiseScale + off) * 2f - 1f;
            float baseH = n * noiseAmplitude;

            float dPin = HorizDist(x, z, _pinPos.x, _pinPos.z);
            if (dPin <= greenRadius) return baseH * greenFlatten;

            // Blend fairway flatten -> full rough amplitude across the transition band.
            float d2c = DistanceToCenterline(x, z);
            float edge = fairwayWidth * 0.5f;
            float t = Mathf.Clamp01((d2c - edge) / Mathf.Max(0.01f, transitionBand));
            float amp = Mathf.Lerp(fairwayFlatten, 1f, t);
            return baseH * amp;
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
        }

        private static void SafeDestroy(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }
    }
}
