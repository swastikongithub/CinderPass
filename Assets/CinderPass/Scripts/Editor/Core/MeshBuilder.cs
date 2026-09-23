using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CinderPass.EditorTools
{
    /// <summary>
    /// Procedural mesh construction toolkit (hard-surface primitives, lathes, tubes, extrusions, cards)
    /// with multiple submeshes, vertex colours and box-projected UVs.
    /// </summary>
    public sealed class MeshBuilder
    {
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Vector2> UVs = new List<Vector2>();
        public readonly List<Color> Colors = new List<Color>();
        readonly List<int>[] tris;

        public Color DefaultColor = Color.white;

        public MeshBuilder(int submeshCount = 1)
        {
            tris = new List<int>[submeshCount];
            for (int i = 0; i < submeshCount; i++) tris[i] = new List<int>();
        }

        public int SubmeshCount => tris.Length;
        public int TriangleCount { get { int n = 0; foreach (var t in tris) n += t.Count / 3; return n; } }

        public int Vertex(Vector3 p, Vector3 n, Vector2 uv) => Vertex(p, n, uv, DefaultColor);

        public int Vertex(Vector3 p, Vector3 n, Vector2 uv, Color c)
        {
            Vertices.Add(p);
            Normals.Add(n);
            UVs.Add(uv);
            Colors.Add(c);
            return Vertices.Count - 1;
        }

        public void Triangle(int sub, int a, int b, int c)
        {
            var t = tris[sub];
            t.Add(a); t.Add(b); t.Add(c);
        }

        public void Quad(int sub, int a, int b, int c, int d)
        {
            Triangle(sub, a, b, c);
            Triangle(sub, a, c, d);
        }

        /// <summary>Flat-shaded planar polygon (convex, counter-clockwise when viewed from the normal side).</summary>
        public void Polygon(int sub, IList<Vector3> pts, float uvScale = 1f)
        {
            if (pts.Count < 3) return;
            Vector3 n = Vector3.Cross(pts[1] - pts[0], pts[2] - pts[0]).normalized;
            int start = Vertices.Count;
            foreach (var p in pts) Vertex(p, n, BoxUV(p, n) * uvScale);
            for (int i = 1; i < pts.Count - 1; i++) Triangle(sub, start, start + i, start + i + 1);
        }

        /// <summary>Flat quad face from 4 corners (a-b-c-d counter-clockwise seen from outside).</summary>
        public void Face(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, float uvScale = 1f)
        {
            Vector3 n = Vector3.Cross(b - a, c - a).normalized;
            int i0 = Vertex(a, n, BoxUV(a, n) * uvScale);
            int i1 = Vertex(b, n, BoxUV(b, n) * uvScale);
            int i2 = Vertex(c, n, BoxUV(c, n) * uvScale);
            int i3 = Vertex(d, n, BoxUV(d, n) * uvScale);
            Quad(sub, i0, i1, i2, i3);
        }

        public static Vector2 BoxUV(Vector3 p, Vector3 n)
        {
            Vector3 a = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
            if (a.x >= a.y && a.x >= a.z) return new Vector2(p.z, p.y);
            if (a.y >= a.z) return new Vector2(p.x, p.z);
            return new Vector2(p.x, p.y);
        }

        /// <summary>Oriented box with optional chamfer on all edges (chamfer 0 = sharp box).</summary>
        public void Box(int sub, Matrix4x4 m, Vector3 size, float chamfer = 0f)
        {
            Vector3 h = size * 0.5f;
            if (chamfer <= 0f)
            {
                Vector3 P(float x, float y, float z) => m.MultiplyPoint3x4(new Vector3(x * h.x, y * h.y, z * h.z));
                Face(sub, P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1));    // +z
                Face(sub, P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1)); // -z
                Face(sub, P(1, -1, 1), P(1, -1, -1), P(1, 1, -1), P(1, 1, 1));     // +x
                Face(sub, P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1)); // -x
                Face(sub, P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), P(-1, 1, -1));     // +y
                Face(sub, P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1)); // -y
                return;
            }
            // Chamfered box: an extruded octagon profile across X, capped.
            float c = Mathf.Min(chamfer, Mathf.Min(h.y, h.z) * 0.9f);
            var profile = new List<Vector2>
            {
                new Vector2(-h.z + c, -h.y), new Vector2(h.z - c, -h.y), new Vector2(h.z, -h.y + c), new Vector2(h.z, h.y - c),
                new Vector2(h.z - c, h.y), new Vector2(-h.z + c, h.y), new Vector2(-h.z, h.y - c), new Vector2(-h.z, -h.y + c)
            };
            Extrude(sub, profile, -h.x, h.x, m, true);
        }

        /// <summary>
        /// Extrudes a closed 2D profile (x = local Z forward, y = local Y up) along local X from x0 to x1.
        /// Profile must be counter-clockwise when viewed from +X. Flat shaded, optionally capped (convex caps).
        /// </summary>
        public void Extrude(int sub, IList<Vector2> profile, float x0, float x1, Matrix4x4 m, bool caps, float capInset = 0f)
        {
            int n = profile.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = profile[i], b = profile[(i + 1) % n];
                Vector3 p0 = m.MultiplyPoint3x4(new Vector3(x0, a.y, a.x));
                Vector3 p1 = m.MultiplyPoint3x4(new Vector3(x0, b.y, b.x));
                Vector3 p2 = m.MultiplyPoint3x4(new Vector3(x1, b.y, b.x));
                Vector3 p3 = m.MultiplyPoint3x4(new Vector3(x1, a.y, a.x));
                Face(sub, p0, p3, p2, p1);
            }
            if (!caps) return;
            Vector2 centroid = Vector2.zero;
            foreach (var p in profile) centroid += p;
            centroid /= n;
            var capA = new List<Vector3>();
            var capB = new List<Vector3>();
            for (int i = 0; i < n; i++)
            {
                Vector2 q = Vector2.Lerp(profile[n - 1 - i], centroid, capInset);
                capA.Add(m.MultiplyPoint3x4(new Vector3(x1, q.y, q.x)));
                capB.Add(m.MultiplyPoint3x4(new Vector3(x0, profile[i].y, profile[i].x)));
            }
            Polygon(sub, capA);
            Polygon(sub, capB);
        }

        /// <summary>Tapered cylinder between two points (smooth sides).</summary>
        public void Cylinder(int sub, Vector3 a, Vector3 b, float ra, float rb, int segments, bool caps, Color? color = null)
        {
            Vector3 axis = b - a;
            float len = axis.magnitude;
            if (len < 1e-5f) return;
            Vector3 dir = axis / len;
            Vector3 side = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
            Vector3 up = Vector3.Cross(side, dir);
            Color col = color ?? DefaultColor;
            int start = Vertices.Count;
            float slope = (ra - rb) / len;
            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments * Mathf.PI * 2f;
                Vector3 radial = side * Mathf.Cos(t) + up * Mathf.Sin(t);
                Vector3 n = (radial + dir * slope).normalized;
                Vertex(a + radial * ra, n, new Vector2((float)i / segments, 0f), col);
                Vertex(b + radial * rb, n, new Vector2((float)i / segments, len), col);
            }
            for (int i = 0; i < segments; i++)
            {
                int k = start + i * 2;
                Quad(sub, k, k + 1, k + 3, k + 2);
            }
            if (!caps) return;
            for (int end = 0; end < 2; end++)
            {
                Vector3 c = end == 0 ? a : b;
                float r = end == 0 ? ra : rb;
                Vector3 n = end == 0 ? -dir : dir;
                int ci = Vertex(c, n, new Vector2(0.5f, 0.5f), col);
                int ring = Vertices.Count;
                for (int i = 0; i < segments; i++)
                {
                    float t = (float)i / segments * Mathf.PI * 2f;
                    Vector3 radial = side * Mathf.Cos(t) + up * Mathf.Sin(t);
                    Vertex(c + radial * r, n, new Vector2(0.5f + Mathf.Cos(t) * 0.5f, 0.5f + Mathf.Sin(t) * 0.5f), col);
                }
                for (int i = 0; i < segments; i++)
                {
                    int i0 = ring + i, i1 = ring + (i + 1) % segments;
                    if (end == 0) Triangle(sub, ci, i0, i1); else Triangle(sub, ci, i1, i0);
                }
            }
        }

        /// <summary>Smooth tube along a polyline with parallel-transport frames (roll cages, pipes, branches).</summary>
        public void Tube(int sub, IList<Vector3> path, float radius, int segments, bool caps = true, float radiusEnd = -1f)
        {
            if (path.Count < 2) return;
            if (radiusEnd < 0f) radiusEnd = radius;
            Vector3 t0 = (path[1] - path[0]).normalized;
            Vector3 normal = Vector3.Cross(t0, Mathf.Abs(t0.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
            int start = Vertices.Count;
            float along = 0f;
            for (int i = 0; i < path.Count; i++)
            {
                Vector3 tan = i == 0 ? t0 : i == path.Count - 1 ? (path[i] - path[i - 1]).normalized : (path[i + 1] - path[i - 1]).normalized;
                normal = Vector3.ProjectOnPlane(normal, tan).normalized;
                Vector3 bin = Vector3.Cross(tan, normal);
                if (i > 0) along += Vector3.Distance(path[i], path[i - 1]);
                float r = Mathf.Lerp(radius, radiusEnd, (float)i / (path.Count - 1));
                for (int s = 0; s <= segments; s++)
                {
                    float a = (float)s / segments * Mathf.PI * 2f;
                    Vector3 radial = normal * Mathf.Cos(a) + bin * Mathf.Sin(a);
                    Vertex(path[i] + radial * r, radial, new Vector2((float)s / segments, along));
                }
            }
            int ringSize = segments + 1;
            for (int i = 0; i < path.Count - 1; i++)
            for (int s = 0; s < segments; s++)
            {
                int a = start + i * ringSize + s;
                int b = a + ringSize;
                Quad(sub, a, a + 1, b + 1, b);
            }
            if (!caps) return;
            Cap(sub, path[0], -(path[1] - path[0]).normalized, radius, segments, normal);
            Cap(sub, path[path.Count - 1], (path[path.Count - 1] - path[path.Count - 2]).normalized, radiusEnd, segments, normal);
        }

        void Cap(int sub, Vector3 c, Vector3 n, float r, int segments, Vector3 refNormal)
        {
            Vector3 x = Vector3.ProjectOnPlane(refNormal, n).normalized;
            Vector3 y = Vector3.Cross(n, x);
            int ci = Vertex(c, n, new Vector2(0.5f, 0.5f));
            int ring = Vertices.Count;
            for (int i = 0; i < segments; i++)
            {
                float a = (float)i / segments * Mathf.PI * 2f;
                Vertex(c + (x * Mathf.Cos(a) + y * Mathf.Sin(a)) * r, n, new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f));
            }
            for (int i = 0; i < segments; i++) Triangle(sub, ci, ring + i, ring + (i + 1) % segments);
        }

        /// <summary>
        /// Surface of revolution around local X (wheels/tyres). Profile points are (radius, x).
        /// hardEdges duplicates vertices per profile segment for crisp creases.
        /// </summary>
        public void Lathe(int sub, IList<Vector2> profile, int segments, Matrix4x4 m, bool hardEdges, float uvV = 1f)
        {
            int start = Vertices.Count;
            if (!hardEdges)
            {
                for (int s = 0; s <= segments; s++)
                {
                    float a = (float)s / segments * Mathf.PI * 2f;
                    float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                    for (int i = 0; i < profile.Count; i++)
                    {
                        Vector2 p = profile[i];
                        Vector2 prev = profile[Mathf.Max(0, i - 1)], next = profile[Mathf.Min(profile.Count - 1, i + 1)];
                        Vector2 d = (next - prev).normalized;              // (dr, dx)
                        Vector2 n2 = new Vector2(d.y, -d.x);                  // outward in (r, x)
                        Vector3 pos = new Vector3(p.y, p.x * ca, p.x * sa);
                        Vector3 nrm = new Vector3(n2.y, n2.x * ca, n2.x * sa).normalized;
                        Vertex(m.MultiplyPoint3x4(pos), m.MultiplyVector(nrm).normalized, new Vector2((float)s / segments * 4f, i * uvV));
                    }
                }
                int rows = profile.Count;
                for (int s = 0; s < segments; s++)
                for (int i = 0; i < rows - 1; i++)
                {
                    int a = start + s * rows + i, b = start + (s + 1) * rows + i;
                    Quad(sub, a, b, b + 1, a + 1);
                }
                return;
            }
            for (int i = 0; i < profile.Count - 1; i++)
            {
                Vector2 p0 = profile[i], p1 = profile[i + 1];
                Vector2 d = (p1 - p0).normalized;
                Vector2 n2 = new Vector2(d.y, -d.x);
                int rowStart = Vertices.Count;
                for (int s = 0; s <= segments; s++)
                {
                    float a = (float)s / segments * Mathf.PI * 2f;
                    float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                    Vector3 nrm = m.MultiplyVector(new Vector3(n2.y, n2.x * ca, n2.x * sa)).normalized;
                    Vertex(m.MultiplyPoint3x4(new Vector3(p0.y, p0.x * ca, p0.x * sa)), nrm, new Vector2((float)s / segments * 4f, i * uvV));
                    Vertex(m.MultiplyPoint3x4(new Vector3(p1.y, p1.x * ca, p1.x * sa)), nrm, new Vector2((float)s / segments * 4f, (i + 1) * uvV));
                }
                for (int s = 0; s < segments; s++)
                {
                    int k = rowStart + s * 2;
                    Quad(sub, k, k + 2, k + 3, k + 1);
                }
            }
        }

        /// <summary>A double-sided-friendly card (single quad) with per-corner colours and UV rect.</summary>
        public void Card(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Rect uv, Color ca, Color cb, Color cc, Color cd, Vector3? normalOverride = null)
        {
            Vector3 n = normalOverride ?? Vector3.Cross(b - a, d - a).normalized;
            int i0 = Vertex(a, n, new Vector2(uv.xMin, uv.yMin), ca);
            int i1 = Vertex(b, n, new Vector2(uv.xMax, uv.yMin), cb);
            int i2 = Vertex(c, n, new Vector2(uv.xMax, uv.yMax), cc);
            int i3 = Vertex(d, n, new Vector2(uv.xMin, uv.yMax), cd);
            Quad(sub, i0, i1, i2, i3);
        }

        public void Append(MeshBuilder other, Matrix4x4 m, int targetSubmeshOffset = 0)
        {
            int start = Vertices.Count;
            for (int i = 0; i < other.Vertices.Count; i++)
            {
                Vertices.Add(m.MultiplyPoint3x4(other.Vertices[i]));
                Normals.Add(m.MultiplyVector(other.Normals[i]).normalized);
                UVs.Add(other.UVs[i]);
                Colors.Add(other.Colors[i]);
            }
            for (int s = 0; s < other.tris.Length; s++)
                foreach (int idx in other.tris[s]) tris[Mathf.Min(s + targetSubmeshOffset, tris.Length - 1)].Add(idx + start);
        }

        public Mesh ToMesh(string name, bool recalculateNormals = false)
        {
            var mesh = new Mesh { name = name };
            if (Vertices.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(Vertices);
            mesh.SetNormals(Normals);
            mesh.SetUVs(0, UVs);
            mesh.SetColors(Colors);
            mesh.subMeshCount = tris.Length;
            for (int i = 0; i < tris.Length; i++) mesh.SetTriangles(tris[i], i, false);
            if (recalculateNormals) mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }
    }
}
