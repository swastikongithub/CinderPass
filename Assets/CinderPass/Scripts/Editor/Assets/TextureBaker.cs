using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CinderPass.EditorTools
{
    /// <summary>
    /// Generates every derived texture in the project: packed mask maps, tileable noise, particle sprites,
    /// and foliage cards composed from the Poly Haven twig/grass atlases.
    /// </summary>
    public static class TextureBaker
    {
        public static string GroundMaskPath(string id) => $"{CinderPaths.GenTextures}/Ground/{id}_mask.png";
        public static string ModelMaskPath(string id) => $"{CinderPaths.GenTextures}/Models/{id}_mask.png";
        public const string LavaNoisePath = CinderPaths.GenTextures + "/FX/T_Noise_RGBA.png";
        public const string SmokePath = CinderPaths.GenTextures + "/FX/T_Smoke_2x2.png";
        public const string EmberPath = CinderPaths.GenTextures + "/FX/T_Ember.png";
        public const string SparkPath = CinderPaths.GenTextures + "/FX/T_Spark.png";
        public const string AshPath = CinderPaths.GenTextures + "/FX/T_AshFlake.png";
        public const string BranchAlbedoPath = CinderPaths.GenTextures + "/Foliage/T_FirBranch_Albedo.png";
        public const string BranchNormalPath = CinderPaths.GenTextures + "/Foliage/T_FirBranch_Normal.png";
        public const string GrassAlbedoPath = CinderPaths.GenTextures + "/Foliage/T_Grass_Albedo.png";
        public const string GrassDryAlbedoPath = CinderPaths.GenTextures + "/Foliage/T_GrassDry_Albedo.png";
        public const string GrassNormalPath = CinderPaths.GenTextures + "/Foliage/T_Grass_Normal.png";
        public const string CharredBarkPath = CinderPaths.GenTextures + "/Foliage/T_CharredBark.png";
        public const string FernAlbedoPath = CinderPaths.GenTextures + "/Foliage/T_Fern_Albedo.png";

        public static readonly string[] GroundSets =
        {
            "sparse_grass", "brown_mud_leaves_01", "rocky_trail", "forest_ground_04", "aerial_rocks_02",
            "burned_ground_01", "ground_grey", "dark_rock_02", "dark_rock"
        };

        // ------------------------------------------------------------------ mask maps

        /// <summary>Packs Poly Haven ARM (AO, Rough, Metal) + displacement into URP mask layout (R metal, G AO, B height, A smooth).</summary>
        public static void BakeGroundMasks()
        {
            foreach (var id in GroundSets)
            {
                string dir = $"{CinderPaths.GroundTextures}/{id}";
                var arm = AssetUtil.LoadRaw($"{dir}/{id}_arm.jpg");
                string dispPath = File.Exists($"{dir}/{id}_disp.png") ? $"{dir}/{id}_disp.png" : $"{dir}/{id}_disp.jpg";
                var disp = AssetUtil.LoadRaw(dispPath);
                int w = arm.width, h = arm.height;
                var a = arm.GetPixels32();
                var d = ResizePixels(disp, w, h);
                var outPx = new Color32[a.Length];
                for (int i = 0; i < a.Length; i++)
                    outPx[i] = new Color32(a[i].b, a[i].r, d[i].r, (byte)(255 - a[i].g));
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, true, true);
                tex.SetPixels32(outPx);
                tex.Apply();
                AssetUtil.WritePng(tex, GroundMaskPath(id), linear: true, maxSize: 2048);
                Object.DestroyImmediate(tex);
                Object.DestroyImmediate(arm);
                Object.DestroyImmediate(disp);
            }
        }

        /// <summary>Mask map for a Poly Haven model from its roughness (and optional metal) EXR maps.</summary>
        public static Texture2D BakeModelMask(string id, string texturesDir)
        {
            string rough = FindFile(texturesDir, "_rough");
            if (rough == null) return null;
            string metal = FindFile(texturesDir, "_metal");
            string ao = FindFile(texturesDir, "_ao");
            int size = 1024;
            var r = AssetUtil.ReadPixels(rough, size, size);
            var m = metal != null ? AssetUtil.ReadPixels(metal, size, size) : null;
            var o = ao != null ? AssetUtil.ReadPixels(ao, size, size) : null;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, true);
            var px = new Color[size * size];
            for (int i = 0; i < px.Length; i++)
                px[i] = new Color(m != null ? m[i].r : 0f, o != null ? o[i].r : 1f, 0.5f, 1f - Mathf.Clamp01(r[i].r));
            tex.SetPixels(px);
            tex.Apply();
            var result = AssetUtil.WritePng(tex, ModelMaskPath(id), linear: true, maxSize: 1024);
            Object.DestroyImmediate(tex);
            return result;
        }

        static string FindFile(string dir, string token)
        {
            if (!Directory.Exists(dir)) return null;
            foreach (var f in Directory.GetFiles(dir))
            {
                if (f.EndsWith(".meta")) continue;
                if (Path.GetFileName(f).Contains(token)) return f.Replace('\\', '/');
            }
            return null;
        }

        static Color32[] ResizePixels(Texture2D src, int w, int h)
        {
            if (src.width == w && src.height == h) return src.GetPixels32();
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            t.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            t.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = t.GetPixels32();
            Object.DestroyImmediate(t);
            return px;
        }

        // ------------------------------------------------------------------ noise & FX sprites

        /// <summary>Tileable RGBA noise: R fbm, G Worley F1, B Worley edge (F2-F1), A ridged.</summary>
        public static void BakeNoise()
        {
            const int size = 512;
            var noise = new Noise(1337);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size, v = (float)y / size;
                float fbm = noise.Fbm(u * 4f, v * 4f, 5, 2f, 0.5f, 4) * 0.5f + 0.5f;
                // Domain-warped cells give organic plate shapes.
                float wx = noise.Fbm(u * 3f + 5.2f, v * 3f + 1.3f, 3, 2f, 0.5f, 3) * 0.35f;
                float wy = noise.Fbm(u * 3f + 9.7f, v * 3f + 4.1f, 3, 2f, 0.5f, 3) * 0.35f;
                noise.Worley((u + wx) * 7f, (v + wy) * 7f, 7, out float f1, out float f2);
                float ridged = noise.Ridged(u * 4f + 0.37f, v * 4f + 0.71f, 5, 2f, 0.5f, 4);
                px[y * size + x] = new Color(Mathf.Clamp01(fbm), Mathf.Clamp01(1f - f1 * 1.2f), Mathf.Clamp01((f2 - f1) * 1.8f), Mathf.Clamp01(ridged));
            }
            WriteLinear(px, size, size, LavaNoisePath, 512);
        }

        public static void BakeParticleSprites()
        {
            var noise = new Noise(99);
            // Smoke: 2x2 atlas of distinct billowy puffs, shaded top-light to read as volume.
            {
                const int cell = 256, size = cell * 2;
                var px = new Color[size * size];
                for (int c = 0; c < 4; c++)
                {
                    int ox = (c % 2) * cell, oy = (c / 2) * cell;
                    for (int y = 0; y < cell; y++)
                    for (int x = 0; x < cell; x++)
                    {
                        float u = (x + 0.5f) / cell * 2f - 1f, v = (y + 0.5f) / cell * 2f - 1f;
                        float r = Mathf.Sqrt(u * u + v * v);
                        float n = noise.Fbm(u * 2.2f + c * 7.1f, v * 2.2f + c * 3.3f, 5) * 0.5f + 0.5f;
                        float billow = noise.Fbm(u * 5f + c * 2f, v * 5f - c, 3) * 0.5f + 0.5f;
                        float shape = Mathf.Clamp01(1f - r * (0.85f + 0.5f * (1f - n)));
                        float a = Mathf.Pow(shape, 1.6f) * Mathf.Lerp(0.55f, 1f, billow);
                        float shade = Mathf.Lerp(0.62f, 1f, Mathf.Clamp01(0.5f + v * 0.45f + (billow - 0.5f) * 0.5f));
                        px[(oy + y) * size + ox + x] = new Color(shade, shade, shade, Mathf.Clamp01(a));
                    }
                }
                WriteSRGB(px, size, size, SmokePath, 512);
            }
            // Ember: hot core with soft falloff.
            {
                const int size = 64;
                var px = new Color[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float core = Mathf.Clamp01(1f - r * 2.2f);
                    float glow = Mathf.Pow(Mathf.Clamp01(1f - r), 3f);
                    float a = Mathf.Clamp01(core + glow * 0.6f);
                    px[y * size + x] = new Color(1f, Mathf.Lerp(0.55f, 1f, core), Mathf.Lerp(0.2f, 0.9f, core * core), a);
                }
                WriteSRGB(px, size, size, EmberPath, 64);
            }
            // Spark streak (stretched billboards).
            {
                const int w = 64, h = 64;
                var px = new Color[w * h];
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w * 2f - 1f, v = (y + 0.5f) / h * 2f - 1f;
                    float a = Mathf.Clamp01(1f - Mathf.Abs(u) * 2.5f) * Mathf.Clamp01(1f - Mathf.Abs(v));
                    px[y * w + x] = new Color(1f, 0.85f, 0.6f, a * a);
                }
                WriteSRGB(px, w, h, SparkPath, 64);
            }
            // Ash flake: irregular dark flake.
            {
                const int size = 32;
                var px = new Color[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(u * u * 1.4f + v * v) + noise.Perlin(u * 3f, v * 3f) * 0.3f;
                    float a = Mathf.Clamp01((0.8f - r) * 5f);
                    px[y * size + x] = new Color(0.3f, 0.29f, 0.28f, a);
                }
                WriteSRGB(px, size, size, AshPath, 32);
            }
        }

        static void WriteLinear(Color[] px, int w, int h, string path, int max)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true, true);
            t.SetPixels(px);
            t.Apply();
            AssetUtil.WritePng(t, path, linear: true, maxSize: max);
            Object.DestroyImmediate(t);
        }

        static void WriteSRGB(Color[] px, int w, int h, string path, int max)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true, false);
            t.SetPixels(px);
            t.Apply();
            AssetUtil.WritePng(t, path, linear: false, maxSize: max, alphaIsTransparency: true, wrap: TextureWrapMode.Clamp);
            Object.DestroyImmediate(t);
        }

        // ------------------------------------------------------------------ foliage cards

        sealed class Sprite
        {
            public RectInt rect;
            public int area;
            public int id;
        }

        /// <summary>Finds opaque islands in an alpha mask (connected components) at a working resolution.</summary>
        static List<Sprite> FindIslands(Color[] alpha, int w, int h, float threshold, int minArea) => FindIslands(alpha, w, h, threshold, minArea, out _);

        static List<Sprite> FindIslands(Color[] alpha, int w, int h, float threshold, int minArea, out int[] labels)
        {
            labels = new int[w * h];
            int nextId = 0;
            var result = new List<Sprite>();
            var stack = new Stack<int>();
            for (int i = 0; i < w * h; i++)
            {
                if (labels[i] != 0 || alpha[i].r < threshold) continue;
                int xmin = w, ymin = h, xmax = 0, ymax = 0, area = 0;
                int id = ++nextId;
                stack.Push(i);
                labels[i] = id;
                while (stack.Count > 0)
                {
                    int p = stack.Pop();
                    int x = p % w, y = p / w;
                    area++;
                    if (x < xmin) xmin = x; if (x > xmax) xmax = x;
                    if (y < ymin) ymin = y; if (y > ymax) ymax = y;
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int q = ny * w + nx;
                        if (labels[q] != 0 || alpha[q].r < threshold) continue;
                        labels[q] = id;
                        stack.Push(q);
                    }
                }
                if (area >= minArea) result.Add(new Sprite { rect = new RectInt(xmin, ymin, xmax - xmin + 1, ymax - ymin + 1), area = area, id = id });
            }
            return result;
        }

        sealed class Canvas
        {
            public readonly int w, h;
            public readonly Color[] albedo, normal;
            public Canvas(int w, int h)
            {
                this.w = w; this.h = h;
                albedo = new Color[w * h];
                normal = new Color[w * h];
                for (int i = 0; i < normal.Length; i++) normal[i] = new Color(0.5f, 0.5f, 1f, 1f);
            }
        }

        struct Source
        {
            public Color[] albedo, alpha, normal;
            public int w, h;
            public Vector4 Sample(Color[] arr, float x, float y)
            {
                x = Mathf.Clamp(x, 0, w - 1.001f); y = Mathf.Clamp(y, 0, h - 1.001f);
                int x0 = (int)x, y0 = (int)y;
                float fx = x - x0, fy = y - y0;
                Color c = Color.Lerp(Color.Lerp(arr[y0 * w + x0], arr[y0 * w + x0 + 1], fx), Color.Lerp(arr[(y0 + 1) * w + x0], arr[(y0 + 1) * w + x0 + 1], fx), fy);
                return c;
            }
        }

        /// <summary>
        /// Stamps a source sprite onto the canvas. The sprite's base (bottom-centre of its rect) is placed at
        /// <paramref name="basePos"/> and its up axis points along <paramref name="angle"/> (radians from +x).
        /// Normals are rotated with the sprite. Tint darkens/colours albedo (used for depth layering).
        /// </summary>
        static void Stamp(Canvas c, in Source s, RectInt r, Vector2 basePos, float angle, float lengthPx, Color tint)
        {
            float scale = lengthPx / r.height;
            float halfW = r.width * 0.5f * scale;
            Vector2 up = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 right = new Vector2(up.y, -up.x);
            Vector2[] corners = { basePos - right * halfW, basePos + right * halfW, basePos + right * halfW + up * lengthPx, basePos - right * halfW + up * lengthPx };
            int x0 = c.w, y0 = c.h, x1 = 0, y1 = 0;
            foreach (var p in corners)
            {
                x0 = Mathf.Min(x0, Mathf.FloorToInt(p.x)); x1 = Mathf.Max(x1, Mathf.CeilToInt(p.x));
                y0 = Mathf.Min(y0, Mathf.FloorToInt(p.y)); y1 = Mathf.Max(y1, Mathf.CeilToInt(p.y));
            }
            x0 = Mathf.Max(0, x0); y0 = Mathf.Max(0, y0); x1 = Mathf.Min(c.w - 1, x1); y1 = Mathf.Min(c.h - 1, y1);
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                Vector2 d = new Vector2(x + 0.5f, y + 0.5f) - basePos;
                float lu = Vector2.Dot(d, right) / scale;   // source px from centre line
                float lv = Vector2.Dot(d, up) / scale;      // source px from base
                if (lv < 0 || lv > r.height || Mathf.Abs(lu) > r.width * 0.5f) continue;
                float sx = r.x + r.width * 0.5f + lu, sy = r.y + lv;
                float a = s.Sample(s.alpha, sx, sy).x;
                if (a <= 0.02f) continue;
                int i = y * c.w + x;
                Vector4 al = s.Sample(s.albedo, sx, sy);
                Vector4 nm = s.Sample(s.normal, sx, sy);
                // Rotate tangent-space normal by the sprite's rotation (source up -> 'up').
                float nx = nm.x * 2f - 1f, ny = nm.y * 2f - 1f;
                Vector2 rn = right * nx + up * ny;
                Color src = new Color(al.x * tint.r, al.y * tint.g, al.z * tint.b, a);
                Color dst = c.albedo[i];
                float outA = a + dst.a * (1f - a);
                c.albedo[i] = new Color(
                    (src.r * a + dst.r * dst.a * (1f - a)) / Mathf.Max(outA, 1e-4f),
                    (src.g * a + dst.g * dst.a * (1f - a)) / Mathf.Max(outA, 1e-4f),
                    (src.b * a + dst.b * dst.a * (1f - a)) / Mathf.Max(outA, 1e-4f), outA);
                Color nOld = c.normal[i];
                c.normal[i] = Color.Lerp(nOld, new Color(rn.x * 0.5f + 0.5f, rn.y * 0.5f + 0.5f, nm.z, 1f), a);
            }
        }

        static Source LoadSource(string albedoPath, string alphaPath, string normalPath, int workSize)
        {
            var s = new Source { w = workSize, h = workSize };
            s.albedo = ToColors(AssetUtil.LoadRaw(albedoPath), workSize);
            s.alpha = ToColors(AssetUtil.LoadRaw(alphaPath), workSize);
            s.normal = normalPath != null ? ToColors(AssetUtil.LoadRaw(normalPath), workSize) : null;
            if (s.normal == null)
            {
                s.normal = new Color[workSize * workSize];
                for (int i = 0; i < s.normal.Length; i++) s.normal[i] = new Color(0.5f, 0.5f, 1f);
            }
            return s;
        }

        static Color[] ToColors(Texture2D t, int size)
        {
            var px32 = ResizePixels(t, size, size);
            Object.DestroyImmediate(t);
            var c = new Color[px32.Length];
            for (int i = 0; i < c.Length; i++) c[i] = px32[i];
            return c;
        }

        /// <summary>
        /// Composes a dense fir branch card (1024x512, base at left, tip at right) from the individual
        /// twig sprays in the Poly Haven fir atlas.
        /// </summary>
        public static void BakeBranchCard()
        {
            const int work = 1024;
            var src = LoadSource($"{CinderPaths.FoliageTextures}/fir_tree_01_twig_diff.jpg", $"{CinderPaths.FoliageTextures}/fir_tree_01_twig_alpha.png",
                $"{CinderPaths.FoliageTextures}/fir_tree_01_twig_nor_gl.jpg", work);
            var islands = FindIslands(src.alpha, work, work, 0.5f, 900);
            // Keep compact, spray-shaped islands (reject stems/strokes and tiny fragments).
            var sprays = new List<Sprite>();
            foreach (var isl in islands)
            {
                float fill = isl.area / (float)(isl.rect.width * isl.rect.height);
                float aspect = isl.rect.height / (float)isl.rect.width;
                if (fill > 0.12f && aspect > 0.6f && aspect < 3.5f && isl.rect.height > work * 0.06f) sprays.Add(isl);
            }
            sprays.Sort((a, b) => b.area.CompareTo(a.area));
            if (sprays.Count > 6) sprays.RemoveRange(6, sprays.Count - 6);
            Debug.Log($"[TextureBaker] Fir atlas: {islands.Count} islands, using {sprays.Count} sprays.");
            if (sprays.Count == 0) throw new System.InvalidOperationException("No twig sprays found in the fir atlas.");

            var canvas = new Canvas(1024, 512);
            var rng = new System.Random(7);
            float cy = 256f;
            // Stem.
            for (int x = 0; x < 900; x++)
            {
                float t = x / 900f;
                float thick = Mathf.Lerp(9f, 2.5f, t);
                for (int y = (int)(cy - thick); y <= (int)(cy + thick); y++)
                {
                    int i = y * canvas.w + x;
                    float k = 1f - Mathf.Abs(y - cy) / (thick + 0.5f);
                    canvas.albedo[i] = new Color(0.22f * (0.7f + 0.3f * k), 0.16f * (0.7f + 0.3f * k), 0.11f, 1f);
                    canvas.normal[i] = new Color(0.5f, 0.5f + (cy - y) / thick * 0.35f, 0.85f, 1f);
                }
            }
            // Back layer (darker, inner shade), then front layer.
            for (int layer = 0; layer < 2; layer++)
            {
                int count = layer == 0 ? 9 : 11;
                for (int i = 0; i < count; i++)
                {
                    float s = 0.06f + 0.84f * i / (count - 1f) + (float)rng.NextDouble() * 0.03f;
                    float side = (i % 2 == 0 ? 1f : -1f);
                    float angle = side * Mathf.Deg2Rad * (layer == 0 ? 55f : 38f + (float)rng.NextDouble() * 14f);
                    float len = Mathf.Lerp(250f, 140f, s) * (layer == 0 ? 1.05f : 0.95f);
                    var spr = sprays[rng.Next(sprays.Count)];
                    float shade = layer == 0 ? 0.62f : 0.9f + (float)rng.NextDouble() * 0.1f;
                    Stamp(canvas, src, spr.rect, new Vector2(s * 930f, cy), angle, len, new Color(shade, shade, shade * 0.95f));
                }
            }
            Stamp(canvas, src, sprays[0].rect, new Vector2(860f, cy), 0f, 170f, Color.white); // leader at the tip

            Dilate(canvas.albedo, canvas.w, canvas.h);
            SaveCard(canvas, BranchAlbedoPath, BranchNormalPath);
        }

        /// <summary>Grass atlas: four clumps (256x512 cells) from the Poly Haven grass atlas, in lush and dry variants.</summary>
        public static void BakeGrassCards()
        {
            const int work = 2048;
            var lush = LoadSource($"{CinderPaths.FoliageTextures}/grass_medium_01_diffuse.jpg", $"{CinderPaths.FoliageTextures}/grass_medium_01_alpha.png",
                $"{CinderPaths.FoliageTextures}/grass_medium_01_nor_gl.jpg", work);
            var dryAlbedo = ToColors(AssetUtil.LoadRaw($"{CinderPaths.FoliageTextures}/grass_medium_01_dry_diff.jpg"), work);

            // The clump cards occupy the bottom ~30% of the atlas (texture v up = bottom rows first).
            var islands = FindIslands(lush.alpha, work, work, 0.35f, 4000, out var labels);
            var clumps = new List<Sprite>();
            foreach (var isl in islands)
                if (isl.rect.yMax < work * 0.33f && isl.rect.width > work * 0.08f && isl.rect.height > work * 0.07f) clumps.Add(isl);
            clumps.Sort((a, b) => b.area.CompareTo(a.area));
            if (clumps.Count > 4) clumps.RemoveRange(4, clumps.Count - 4);
            clumps.Sort((a, b) => a.rect.x.CompareTo(b.rect.x));
            Debug.Log($"[TextureBaker] Grass atlas: {islands.Count} islands, {clumps.Count} clumps.");
            if (clumps.Count == 0) throw new System.InvalidOperationException("No grass clumps found.");

            for (int variant = 0; variant < 2; variant++)
            {
                var s = lush;
                if (variant == 1) s.albedo = dryAlbedo;
                var canvas = new Canvas(1024, 512);
                // 2x2 grid of 512x256 cells (clumps are roughly 2:1, so cards waste little fill).
                for (int c = 0; c < 4; c++)
                {
                    var clump = clumps[c % clumps.Count];
                    var r = clump.rect;
                    float fit = Mathf.Min(500f / r.width, 250f / r.height);
                    StampAxisAligned(canvas, s, r, (c % 2) * 512 + 256, (c / 2) * 256 + 2, fit, labels, clump.id, work);
                }
                Dilate(canvas.albedo, canvas.w, canvas.h);
                if (variant == 0) SaveCard(canvas, GrassAlbedoPath, GrassNormalPath);
                else SaveCard(canvas, GrassDryAlbedoPath, null);
            }
        }

        static void StampAxisAligned(Canvas c, in Source s, RectInt r, int centreX, int baseY, float scale, int[] labels, int id, int labelWidth)
        {
            int w = Mathf.RoundToInt(r.width * scale), h = Mathf.RoundToInt(r.height * scale);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int dx = centreX - w / 2 + x, dy = baseY + y;
                if (dx < 0 || dy < 0 || dx >= c.w || dy >= c.h) continue;
                float sx = r.x + x / scale, sy = r.y + y / scale;
                // Only this island's pixels (neighbouring fragments inside the rect are excluded).
                int lx = Mathf.Clamp(Mathf.RoundToInt(sx), 0, labelWidth - 1), ly = Mathf.Clamp(Mathf.RoundToInt(sy), 0, labelWidth - 1);
                bool mine = false;
                for (int oy = -2; oy <= 2 && !mine; oy++)
                for (int ox = -2; ox <= 2 && !mine; ox++)
                {
                    int qx = Mathf.Clamp(lx + ox, 0, labelWidth - 1), qy = Mathf.Clamp(ly + oy, 0, labelWidth - 1);
                    mine = labels[qy * labelWidth + qx] == id;
                }
                if (!mine) continue;
                float a = s.Sample(s.alpha, sx, sy).x;
                int i = dy * c.w + dx;
                Vector4 al = s.Sample(s.albedo, sx, sy);
                Vector4 nm = s.Sample(s.normal, sx, sy);
                c.albedo[i] = new Color(al.x, al.y, al.z, a);
                c.normal[i] = new Color(nm.x, nm.y, nm.z, 1f);
            }
        }

        /// <summary>Bleeds opaque colours into transparent texels so mipmaps don't produce dark halos.</summary>
        static void Dilate(Color[] px, int w, int h)
        {
            for (int pass = 0; pass < 6; pass++)
            {
                var copy = (Color[])px.Clone();
                for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                {
                    int i = y * w + x;
                    if (copy[i].a > 0.05f) continue;
                    Color sum = Color.clear;
                    int n = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        int j = k == 0 ? i - 1 : k == 1 ? i + 1 : k == 2 ? i - w : i + w;
                        if (copy[j].a > 0.05f || (copy[j].r + copy[j].g + copy[j].b) > 0.01f) { sum += copy[j]; n++; }
                    }
                    if (n > 0) px[i] = new Color(sum.r / n, sum.g / n, sum.b / n, 0f);
                }
            }
        }

        static void SaveCard(Canvas c, string albedoPath, string normalPath)
        {
            var t = new Texture2D(c.w, c.h, TextureFormat.RGBA32, true, false);
            t.SetPixels(c.albedo);
            t.Apply();
            AssetUtil.WritePng(t, albedoPath, linear: false, maxSize: 1024, alphaIsTransparency: false, wrap: TextureWrapMode.Clamp);
            Object.DestroyImmediate(t);
            if (normalPath == null) return;
            var n = new Texture2D(c.w, c.h, TextureFormat.RGBA32, true, true);
            n.SetPixels(c.normal);
            n.Apply();
            AssetUtil.WritePng(n, normalPath, linear: true, normalMap: true, maxSize: 1024, wrap: TextureWrapMode.Clamp);
            Object.DestroyImmediate(n);
        }

        /// <summary>Fern albedo with its separate alpha mask packed into A (the foliage shader reads coverage from A).</summary>
        public static void BakeFernAlbedo()
        {
            string dir = $"{CinderPaths.Models}/fern_02/textures";
            var diff = AssetUtil.LoadRaw($"{dir}/fern_02_diff_1k.jpg");
            var alpha = AssetUtil.LoadRaw($"{dir}/fern_02_alpha_1k.png");
            int w = diff.width, h = diff.height;
            var c = diff.GetPixels32();
            var a = ResizePixels(alpha, w, h);
            for (int i = 0; i < c.Length; i++) c[i].a = a[i].r;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true, false);
            t.SetPixels32(c);
            t.Apply();
            var px = t.GetPixels();
            Dilate(px, w, h);
            t.SetPixels(px);
            t.Apply();
            AssetUtil.WritePng(t, FernAlbedoPath, linear: false, maxSize: 1024, wrap: TextureWrapMode.Clamp);
            Object.DestroyImmediate(t);
            Object.DestroyImmediate(diff);
            Object.DestroyImmediate(alpha);
        }

        /// <summary>Charred bark: the pine bark darkened with glowing-free soot variation (for burnt snags).</summary>
        public static void BakeCharredBark()
        {
            var bark = AssetUtil.LoadRaw($"{CinderPaths.FoliageTextures}/pine_tree_01_bark_diff.jpg");
            var px = ResizePixels(bark, 1024, 1024);
            Object.DestroyImmediate(bark);
            var noise = new Noise(3);
            var outPx = new Color[px.Length];
            for (int y = 0; y < 1024; y++)
            for (int x = 0; x < 1024; x++)
            {
                int i = y * 1024 + x;
                Color c = px[i];
                float soot = Mathf.Clamp01(noise.Fbm(x / 256f, y / 256f, 4, 2f, 0.5f, 4) * 0.5f + 0.55f);
                float lum = (c.r + c.g + c.b) / 3f;
                Color charred = new Color(lum * 0.28f + 0.02f, lum * 0.26f + 0.018f, lum * 0.25f + 0.017f, 1f);
                outPx[i] = Color.Lerp(c * 0.5f, charred, soot);
            }
            var t = new Texture2D(1024, 1024, TextureFormat.RGBA32, true, false);
            t.SetPixels(outPx);
            t.Apply();
            AssetUtil.WritePng(t, CharredBarkPath, linear: false, maxSize: 1024);
            Object.DestroyImmediate(t);
        }
    }
}
