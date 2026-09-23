using CinderPass.Route;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace CinderPass.EditorTools
{
    /// <summary>Dense, smoothed samples of the route used for carving, painting and placement.</summary>
    public sealed class RouteSamples
    {
        public Vector3[] points;     // x, road surface height, z
        public float[] canyon;       // 0..1 canyon weight
        public float spacing;
        public float length;
        public float[] knotDistances; // route distance of each design knot
    }

    public static class RouteBuilder
    {
        /// <summary>Creates (or rebuilds) the authoritative route spline from the design knots.</summary>
        public static SplineContainer BuildSpline(WorldDesign d, Transform parent)
        {
            var go = AssetUtil.Child(parent, "Route");
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var container = AssetUtil.GetOrAdd<SplineContainer>(go);
            var spline = container.Spline;
            spline.Clear();
            foreach (var k in d.route) spline.Add(new BezierKnot((float3)k.position), TangentMode.AutoSmooth, 0.4f);
            spline.Closed = true;
            var track = AssetUtil.GetOrAdd<RouteTrack>(go);
            Wiring.Set(track, "roadHalfWidth", d.roadHalfWidth);
            track.Bake();
            return container;
        }

        public static RouteSamples Sample(SplineContainer container, WorldDesign d, float spacing = 0.5f)
        {
            var spline = container.Spline;
            using var native = new NativeSpline(spline, container.transform.localToWorldMatrix, Allocator.Temp);
            float length = native.GetLength();
            int count = Mathf.CeilToInt(length / spacing);
            spacing = length / count;
            var pts = new Vector3[count];
            var canyon = new float[count];
            int knots = d.route.Length;
            for (int i = 0; i < count; i++)
            {
                float t = native.ConvertIndexUnit(i * spacing, PathIndexUnit.Distance, PathIndexUnit.Normalized);
                pts[i] = (Vector3)native.EvaluatePosition(t);
                float k = native.ConvertIndexUnit(t, PathIndexUnit.Normalized, PathIndexUnit.Knot);
                int k0 = Mathf.FloorToInt(k) % knots, k1 = (k0 + 1) % knots;
                float f = k - Mathf.Floor(k);
                float c0 = d.route[k0].canyon ? 1f : 0f, c1 = d.route[k1].canyon ? 1f : 0f;
                canyon[i] = Mathf.Lerp(c0, c1, Mathf.SmoothStep(0f, 1f, f));
            }

            // Smooth the road profile (limits abrupt grade changes) with a wrapped moving average.
            int window = Mathf.RoundToInt(14f / spacing);
            var ys = new float[count];
            for (int i = 0; i < count; i++)
            {
                float sum = 0f;
                for (int w = -window; w <= window; w++) sum += pts[((i + w) % count + count) % count].y;
                ys[i] = sum / (2 * window + 1);
            }
            for (int i = 0; i < count; i++) pts[i].y = ys[i];

            var knotDist = new float[knots];
            for (int k = 0; k < knots; k++)
            {
                float t = native.ConvertIndexUnit(k, PathIndexUnit.Knot, PathIndexUnit.Normalized);
                knotDist[k] = native.ConvertIndexUnit(t, PathIndexUnit.Normalized, PathIndexUnit.Distance);
            }
            return new RouteSamples { points = pts, canyon = canyon, spacing = spacing, length = length, knotDistances = knotDist };
        }

        /// <summary>After carving, snap the spline knots to the finished road surface.</summary>
        public static void ConformKnotsToGround(SplineContainer container, Terrain terrain)
        {
            var spline = container.Spline;
            for (int i = 0; i < spline.Count; i++)
            {
                var k = spline[i];
                Vector3 p = k.Position;
                p.y = terrain.SampleHeight(p) + terrain.GetPosition().y;
                k.Position = p;
                spline.SetKnot(i, k);
            }
            container.GetComponent<RouteTrack>().Bake();
        }
    }
}
