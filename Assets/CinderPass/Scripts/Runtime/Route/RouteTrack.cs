using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace CinderPass.Route
{
    /// <summary>
    /// The authoritative driving route. The Unity <see cref="SplineContainer"/> on this object is the single
    /// source of truth (terrain carving, autopilot, respawn and cameras all derive from it). At runtime the
    /// spline is baked once into evenly spaced samples so per-frame queries are cheap and allocation free.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SplineContainer))]
    public sealed class RouteTrack : MonoBehaviour
    {
        [SerializeField, Min(0.25f)] float sampleSpacing = 1f;
        [Tooltip("Half width of the drivable track surface in metres (used by respawn safety and validation).")]
        [SerializeField, Min(1f)] float roadHalfWidth = 5f;
        [SerializeField] bool drawGizmos;

        SplineContainer container;
        Vector3[] positions;
        Vector3[] tangents;
        float length;
        float spacing;
        bool closed;

        public SplineContainer Container => container != null ? container : container = GetComponent<SplineContainer>();
        public float RoadHalfWidth => roadHalfWidth;
        public bool IsClosed { get { EnsureBaked(); return closed; } }
        public float Length { get { EnsureBaked(); return length; } }
        public int SampleCount { get { EnsureBaked(); return positions.Length; } }
        public float SampleSpacing { get { EnsureBaked(); return spacing; } }

        void Awake() => Bake();

        void OnValidate() => positions = null;

        void EnsureBaked()
        {
            if (positions == null || positions.Length < 2) Bake();
        }

        /// <summary>Re-samples the spline. Call after editing knots at runtime.</summary>
        public void Bake()
        {
            var spline = Container.Spline;
            closed = spline.Closed;
            using var native = new NativeSpline(spline, Container.transform.localToWorldMatrix, Allocator.Temp);
            length = native.GetLength();
            int count = Mathf.Max(2, Mathf.CeilToInt(length / sampleSpacing) + 1);
            spacing = length / (count - 1);
            positions = new Vector3[count];
            tangents = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                float t = native.ConvertIndexUnit(i * spacing, PathIndexUnit.Distance, PathIndexUnit.Normalized);
                native.Evaluate(math.clamp(t, 0f, 1f), out float3 p, out float3 tan, out _);
                positions[i] = p;
                tangents[i] = math.lengthsq(tan) > 1e-6f ? (Vector3)math.normalize(tan) : Vector3.forward;
            }
        }

        /// <summary>Wraps (closed) or clamps (open) a distance to the valid range.</summary>
        public float Wrap(float distance)
        {
            EnsureBaked();
            if (closed) return Mathf.Repeat(distance, length);
            return Mathf.Clamp(distance, 0f, length);
        }

        public Vector3 PositionAt(float distance)
        {
            Locate(distance, out int i, out float f);
            return Vector3.LerpUnclamped(positions[i], positions[i + 1], f);
        }

        public Vector3 TangentAt(float distance)
        {
            Locate(distance, out int i, out float f);
            return Vector3.Slerp(tangents[i], tangents[i + 1], f).normalized;
        }

        /// <summary>Position plus a horizontal forward/right frame at the given distance.</summary>
        public void SampleFrame(float distance, out Vector3 position, out Vector3 forward, out Vector3 right)
        {
            position = PositionAt(distance);
            forward = TangentAt(distance);
            var flat = new Vector3(forward.x, 0f, forward.z);
            right = flat.sqrMagnitude > 1e-6f ? Vector3.Cross(Vector3.up, flat.normalized) : Vector3.right;
        }

        /// <summary>
        /// Finds the distance along the route closest to <paramref name="point"/>.
        /// Pass a sample index hint (or -1) to restrict the search to a window around the previous result.
        /// </summary>
        public float FindNearest(Vector3 point, ref int hint, int window = 40)
        {
            EnsureBaked();
            int n = positions.Length;
            int best = 0;
            float bestSq = float.MaxValue;
            if (hint < 0 || hint >= n)
            {
                for (int i = 0; i < n; i++)
                {
                    float d = (positions[i] - point).sqrMagnitude;
                    if (d < bestSq) { bestSq = d; best = i; }
                }
            }
            else
            {
                for (int k = -window; k <= window; k++)
                {
                    int i = hint + k;
                    if (closed) i = (i % n + n) % n;
                    else if (i < 0 || i >= n) continue;
                    float d = (positions[i] - point).sqrMagnitude;
                    if (d < bestSq) { bestSq = d; best = i; }
                }
            }
            hint = best;

            // Refine by projecting onto the neighbouring segments.
            float bestDistance = best * spacing;
            float refinedSq = bestSq;
            for (int side = -1; side <= 0; side++)
            {
                int a = best + side;
                int b = a + 1;
                if (closed) { a = (a % n + n) % n; b = (b % n + n) % n; }
                else if (a < 0 || b >= n) continue;
                Vector3 seg = positions[b] - positions[a];
                float t = Mathf.Clamp01(Vector3.Dot(point - positions[a], seg) / Mathf.Max(seg.sqrMagnitude, 1e-6f));
                float d = (positions[a] + seg * t - point).sqrMagnitude;
                if (d <= refinedSq)
                {
                    refinedSq = d;
                    bestDistance = (best + side + t) * spacing;
                }
            }
            return Wrap(bestDistance);
        }

        /// <summary>Signed horizontal offset of <paramref name="point"/> from the centre line (positive = right).</summary>
        public float LateralOffset(Vector3 point, float distance)
        {
            SampleFrame(distance, out var p, out _, out var right);
            return Vector3.Dot(point - p, right);
        }

        /// <summary>Signed along-route delta from a to b, respecting loop wrap.</summary>
        public float Delta(float from, float to)
        {
            float d = to - from;
            if (closed && length > 0f)
            {
                if (d > length * 0.5f) d -= length;
                else if (d < -length * 0.5f) d += length;
            }
            return d;
        }

        void Locate(float distance, out int index, out float fraction)
        {
            EnsureBaked();
            float s = Wrap(distance) / spacing;
            index = Mathf.Clamp(Mathf.FloorToInt(s), 0, positions.Length - 2);
            fraction = Mathf.Clamp01(s - index);
        }

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            if (!drawGizmos) return;
            EnsureBaked();
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.9f);
            for (int i = 0; i < positions.Length - 1; i++) Gizmos.DrawLine(positions[i] + Vector3.up * 0.3f, positions[i + 1] + Vector3.up * 0.3f);
            Gizmos.color = new Color(1f, 1f, 1f, 0.35f);
            for (int i = 0; i < positions.Length; i += 25)
            {
                SampleFrame(i * spacing, out var p, out _, out var r);
                Gizmos.DrawLine(p - r * roadHalfWidth, p + r * roadHalfWidth);
            }
        }
#endif
    }
}
