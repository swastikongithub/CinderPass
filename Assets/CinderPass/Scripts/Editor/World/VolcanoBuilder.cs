using System.Collections.Generic;
using CinderPass.Environment;
using CinderPass.Hazards;
using UnityEditor;
using UnityEngine;

namespace CinderPass.EditorTools
{
    /// <summary>
    /// Builds the volcanic region on top of the sculpted terrain: lava surfaces (pools, river, summit crater)
    /// with meshes that match the carved shorelines, hazard triggers, lava lights, ambient VFX and audio,
    /// fumaroles and the eruption rig.
    /// </summary>
    public static class VolcanoBuilder
    {
        public sealed class Result
        {
            public VolcanicActivity activity;
            public RegionalEmission ashFall;
            public HazardEffect hazardEffect;
            public int hazardZones;
        }

        public static Result Build(Transform envRoot, Transform vfxRoot, TerrainGrid g, WorldField f, LavaLayout lava)
        {
            var d = f.D;
            var result = new Result();
            var root = AssetUtil.Child(envRoot, "Volcanic").transform;
            var matPool = MaterialLibrary.Lava("M_Lava_Pool", false, 0.72f, 1f, 0.2f, 7f, 16f);
            var matActive = MaterialLibrary.Lava("M_Lava_Active", false, 0.64f, 1.15f, 0.3f, 6f, 13f);
            var matRiver = MaterialLibrary.Lava("M_Lava_River", true, 0.58f, 1.05f, 1.1f, 6f, 14f);
            matRiver.SetFloat("_ChannelHalfWidth", lava.riverHalfWidth);
            matRiver.SetFloat("_FlowCycle", 6f);
            var matCrater = MaterialLibrary.Lava("M_Lava_Crater", false, 0.4f, 1.5f, 0.35f, 8f, 20f);

            var embers = new Dictionary<string, GameObject>();
            var rumbleClip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioFactory.ClipPath("AMB_LavaRumble_Loop"));

            // --- Pools.
            var poolsRoot = AssetUtil.Child(root, "LavaPools").transform;
            foreach (var pool in lava.pools)
            {
                var go = new GameObject(pool.label.Replace(' ', '_'));
                go.transform.SetParent(poolsRoot, false);
                go.transform.position = new Vector3(pool.centre.x, pool.level, pool.centre.y);
                var mesh = AssetUtil.SaveAsset(PoolMesh(pool), $"{CinderPaths.GenMeshes}/Lava/{go.name}.asset");
                AddSurface(go, mesh, pool.activity > 1.15f ? matActive : matPool);
                result.hazardZones += AddPoolHazard(go, pool);
                AddLavaLight(go.transform, pool.radius, pool.activity);
                AddPoolFx(go.transform, pool.radius, pool.activity);
                if (pool.radius > 8f) AddAudio(go.transform, rumbleClip, pool.radius * 2.5f + 20f, 0.8f);
            }

            // --- River.
            var riverGo = AssetUtil.Child(root, "LavaRiver");
            riverGo.transform.position = Vector3.zero;
            var riverMesh = AssetUtil.SaveAsset(RiverMesh(lava), $"{CinderPaths.GenMeshes}/Lava/LavaRiver.asset");
            AddSurface(riverGo, riverMesh, matRiver);
            result.hazardZones += AddRiverHazards(riverGo.transform, lava);
            AddRiverFx(riverGo.transform, lava);
            // Steam vent where the flow emerges from the flank.
            var vent = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{CinderPaths.VfxPrefabs}/FX_Fumarole.prefab"), riverGo.transform);
            vent.name = "FX_RiverSourceVent";
            vent.transform.position = lava.river[0] + Vector3.up * 0.3f;
            var mid = lava.river[lava.river.Length / 2];
            var riverAudio = AssetUtil.Child(riverGo.transform, "Audio_Rumble");
            riverAudio.transform.position = mid + Vector3.up;
            AddAudio(riverAudio.transform, rumbleClip, 70f, 0.7f, attach: true);

            // --- Summit crater.
            var craterGo = AssetUtil.Child(root, "SummitCrater");
            var crater = lava.crater;
            craterGo.transform.position = new Vector3(crater.centre.x, crater.level, crater.centre.y);
            var craterMesh = AssetUtil.SaveAsset(PoolMesh(crater), $"{CinderPaths.GenMeshes}/Lava/SummitCrater.asset");
            AddSurface(craterGo, craterMesh, matCrater);
            var craterLight = AddLavaLight(craterGo.transform, 60f, 2f);
            craterLight.range = 160f;
            craterLight.intensity = 60f;
            craterLight.GetComponent<LavaLight>().Configure(60f, 0.3f, 0.35f);

            // Plume + eruption.
            var plume = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{CinderPaths.VfxPrefabs}/FX_VolcanoPlume.prefab"), vfxRoot);
            plume.transform.position = craterGo.transform.position + Vector3.up * 8f;
            var eruption = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{CinderPaths.VfxPrefabs}/FX_Eruption.prefab"), vfxRoot);
            eruption.transform.position = craterGo.transform.position + Vector3.up * 4f;
            var boomSrc = eruption.AddComponent<AudioSource>();
            boomSrc.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioFactory.ClipPath("SFX_EruptionBoom"));
            boomSrc.playOnAwake = false;
            boomSrc.spatialBlend = 0.6f;
            boomSrc.maxDistance = 1500f;
            boomSrc.minDistance = 200f;
            boomSrc.volume = 0.9f;
            var activity = eruption.AddComponent<VolcanicActivity>();
            Wiring.SetArray(activity, "eruptionBursts", eruption.GetComponentsInChildren<ParticleSystem>());
            Wiring.Set(activity, "craterLight", craterLight);
            Wiring.Set(activity, "rumble", boomSrc);
            result.activity = activity;

            // --- Fumaroles (steam vents on the ash plains and flanks).
            var fumRoot = AssetUtil.Child(root, "Fumaroles").transform;
            var fumPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{CinderPaths.VfxPrefabs}/FX_Fumarole.prefab");
            for (int i = 0; i < d.fumaroles.Length; i++)
            {
                var p = d.fumaroles[i];
                var fx = (GameObject)PrefabUtility.InstantiatePrefab(fumPrefab, fumRoot);
                fx.name = $"Fumarole_{i}";
                fx.transform.position = new Vector3(p.x, g.Height(p.x, p.y) + 0.2f, p.y);
            }

            // --- Ash fall around the viewer while inside the basin.
            var ash = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{CinderPaths.VfxPrefabs}/FX_AshFall.prefab"), vfxRoot);
            result.ashFall = ash.GetComponent<RegionalEmission>();
            result.ashFall.Configure(null, new Vector3(d.basin.x, 0f, d.basin.y), 200f, 330f, 75f);

            // --- Hazard response effect (repositioned at the contact point at runtime).
            var burst = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{CinderPaths.VfxPrefabs}/FX_HazardBurst.prefab"), vfxRoot);
            var sizzle = burst.AddComponent<AudioSource>();
            sizzle.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioFactory.ClipPath("SFX_LavaSizzle"));
            sizzle.playOnAwake = false;
            sizzle.spatialBlend = 0.3f;
            result.hazardEffect = burst.GetComponent<HazardEffect>();
            Wiring.Set(result.hazardEffect, "sizzle", sizzle);
            return result;
        }

        // ---------------------------------------------------------------- meshes

        /// <summary>Lava meshes carry the distance to the bank in vertex colour R (0..1 over 4 m) for shoreline cooling.</summary>
        const float ShoreRange = 4f;
        /// <summary>Where the carved bank rises through the lava surface, beyond the pool outline / channel edge.</summary>
        const float BankOffset = 0.6f;

        static Color Shore(float metres) => new Color(Mathf.Clamp01(metres / ShoreRange), 0f, 0f, 1f);

        static Mesh PoolMesh(LavaLayout.Pool p)
        {
            var mb = new MeshBuilder(1);
            const int seg = 48;
            int rings = Mathf.Max(3, Mathf.CeilToInt(p.radius / 1.3f));
            int centre = mb.Vertex(Vector3.zero, Vector3.up, new Vector2(p.centre.x, p.centre.y), Shore(ShoreRange));
            for (int r = 1; r <= rings; r++)
            {
                float t = (float)r / rings;
                for (int s = 0; s < seg; s++)
                {
                    float a = s / (float)seg * Mathf.PI * 2f;
                    // Extend 0.8 m past the shoreline so the edge tucks under the carved rim.
                    float outline = p.radius * LavaLayout.OutlineAt(p, a);
                    float rad = (outline + 0.8f) * t;
                    var pos = new Vector3(Mathf.Cos(a) * rad, 0f, Mathf.Sin(a) * rad);
                    mb.Vertex(pos, Vector3.up, new Vector2(p.centre.x + pos.x, p.centre.y + pos.z), Shore(outline + BankOffset - rad));
                }
            }
            for (int s = 0; s < seg; s++) mb.Triangle(0, centre, 1 + (s + 1) % seg, 1 + s);
            for (int r = 1; r < rings; r++)
            for (int s = 0; s < seg; s++)
            {
                int a = 1 + (r - 1) * seg + s, b = 1 + (r - 1) * seg + (s + 1) % seg;
                int c = 1 + r * seg + (s + 1) % seg, e = 1 + r * seg + s;
                mb.Quad(0, a, b, c, e);
            }
            return mb.ToMesh("LavaPool");
        }

        static Mesh RiverMesh(LavaLayout lava)
        {
            var mb = new MeshBuilder(1);
            var pts = lava.river;
            const float overlap = 0.9f; // ribbon reaches under the levee so no gap shows at the bank
            float uhw = lava.riverHalfWidth + overlap;
            const int across = 7;
            float along = 0f;
            for (int i = 0; i < pts.Length; i++)
            {
                if (i > 0) along += Vector3.Distance(pts[i], pts[i - 1]);
                Vector3 fwd = (pts[Mathf.Min(i + 1, pts.Length - 1)] - pts[Mathf.Max(i - 1, 0)]);
                fwd.y = 0f;
                fwd.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, fwd);
                // Geometry narrows toward the source; UVs keep full-width units so the shader's bank cooling
                // stays on the edges.
                float hw = lava.RiverHalfWidthAt(i) + overlap;
                float bank = lava.RiverHalfWidthAt(i) + BankOffset;
                for (int k = 0; k < across; k++)
                {
                    float t = k / (float)(across - 1);
                    float offset = Mathf.Lerp(-hw, hw, t);
                    mb.Vertex(pts[i] + right * offset, Vector3.up, new Vector2(Mathf.Lerp(-uhw, uhw, t), along), Shore(bank - Mathf.Abs(offset)));
                }
            }
            for (int i = 0; i < pts.Length - 1; i++)
            for (int k = 0; k < across - 1; k++)
            {
                int a = i * across + k, b = a + 1, c = a + across + 1, e = a + across;
                mb.Quad(0, a, e, c, b);
            }
            return mb.ToMesh("LavaRiver");
        }

        static void AddSurface(GameObject go, Mesh mesh, Material mat)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = true;
        }

        // ---------------------------------------------------------------- hazards

        static int AddPoolHazard(GameObject poolGo, LavaLayout.Pool p)
        {
            var zone = AssetUtil.Child(poolGo.transform, "Hazard");
            zone.layer = 2; // Ignore Raycast: never hit by camera/respawn probes
            // Convex prism over the lava outline: from well below the surface to 0.9 m above it.
            var verts = new List<Vector3>();
            const int seg = 24;
            for (int s = 0; s < seg; s++)
            {
                float a = s / (float)seg * Mathf.PI * 2f;
                float rad = p.radius * LavaLayout.OutlineAt(p, a) - 0.2f;
                verts.Add(new Vector3(Mathf.Cos(a) * rad, -3f, Mathf.Sin(a) * rad));
                verts.Add(new Vector3(Mathf.Cos(a) * rad, 0.9f, Mathf.Sin(a) * rad));
            }
            var tris = new List<int>();
            for (int s = 0; s < seg; s++)
            {
                int a = s * 2, b = ((s + 1) % seg) * 2;
                tris.AddRange(new[] { a, a + 1, b + 1, a, b + 1, b });
            }
            var mesh = new Mesh { name = $"{poolGo.name}_HazardHull" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh = AssetUtil.SaveAsset(mesh, $"{CinderPaths.GenMeshes}/Lava/{poolGo.name}_Hazard.asset");
            var mc = zone.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            mc.convex = true;
            mc.isTrigger = true;
            var hz = zone.AddComponent<HazardZone>();
            hz.Configure(HazardKind.Lava, "Lava", p.level);
            return 1;
        }

        static int AddRiverHazards(Transform river, LavaLayout lava)
        {
            var root = AssetUtil.Child(river, "Hazards").transform;
            int n = 0;
            const int step = 8;
            for (int i = 0; i < lava.river.Length - 1; i += step)
            {
                Vector3 a = lava.river[i], b = lava.river[Mathf.Min(i + step, lava.river.Length - 1)];
                var go = new GameObject($"Hazard_{n}");
                go.layer = 2;
                go.transform.SetParent(root, false);
                Vector3 mid = (a + b) * 0.5f;
                Vector3 dir = b - a;
                dir.y = 0f;
                go.transform.position = mid;
                go.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
                var bc = go.AddComponent<BoxCollider>();
                bc.isTrigger = true;
                bc.size = new Vector3(lava.RiverHalfWidthAt(i + step / 2) * 2f - 0.4f, 4f, dir.magnitude + 1f);
                bc.center = new Vector3(0f, -1.1f, 0f);
                var hz = go.AddComponent<HazardZone>();
                hz.Configure(HazardKind.Lava, "Lava river", mid.y);
                n++;
            }
            return n;
        }

        // ---------------------------------------------------------------- lights, FX, audio

        static Light AddLavaLight(Transform parent, float radius, float activity)
        {
            var go = AssetUtil.Child(parent, "LavaLight");
            go.transform.localPosition = Vector3.up * Mathf.Clamp(radius * 0.35f, 1.5f, 8f);
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.42f, 0.12f);
            l.range = radius * 2.2f + 10f;
            l.intensity = Mathf.Clamp(radius * 0.9f, 3f, 25f) * activity;
            l.shadows = LightShadows.None;
            var ll = go.AddComponent<LavaLight>();
            ll.Configure(l.intensity, 0.22f, 0.5f);
            return l;
        }

        static void AddPoolFx(Transform parent, float radius, float activity)
        {
            float area = radius * radius;
            Instantiate(VfxFactory.LavaEmbers($"FX_LavaEmbers_R{Mathf.RoundToInt(radius)}", radius, Mathf.Clamp(area * 0.12f * activity, 4f, 26f)), parent, Vector3.up * 0.3f);
            Instantiate(VfxFactory.LavaSmoke($"FX_LavaSmoke_R{Mathf.RoundToInt(radius)}", radius, Mathf.Clamp(radius * 0.18f * activity, 0.5f, 3f)), parent, Vector3.up * 0.5f);
            if (radius > 5f)
                Instantiate(VfxFactory.HeatHaze($"FX_HeatHaze_R{Mathf.RoundToInt(radius)}", radius, Mathf.Clamp(radius * 0.35f, 2f, 7f)), parent, Vector3.up * 0.8f);
        }

        static void AddRiverFx(Transform parent, LavaLayout lava)
        {
            var emberPrefab = VfxFactory.LavaEmbers("FX_LavaEmbers_River", lava.riverHalfWidth, 10f, true, 22f);
            var smokePrefab = VfxFactory.LavaSmoke("FX_LavaSmoke_River", lava.riverHalfWidth, 1.2f, true, 22f);
            var hazePrefab = VfxFactory.HeatHaze("FX_HeatHaze_River", lava.riverHalfWidth, 3f);
            for (int i = 10; i < lava.river.Length - 5; i += 24)
            {
                Vector3 a = lava.river[i], b = lava.river[Mathf.Min(i + 6, lava.river.Length - 1)];
                var rot = Quaternion.LookRotation(new Vector3(b.x - a.x, 0f, b.z - a.z).normalized, Vector3.up);
                Instantiate(emberPrefab, parent, a + Vector3.up * 0.3f, rot, world: true);
                Instantiate(smokePrefab, parent, a + Vector3.up * 0.5f, rot, world: true);
                if ((i / 24) % 2 == 0) Instantiate(hazePrefab, parent, a + Vector3.up * 0.8f, Quaternion.identity, world: true);
            }
        }

        static GameObject Instantiate(GameObject prefab, Transform parent, Vector3 localPos) => Instantiate(prefab, parent, localPos, Quaternion.identity, world: false);

        static GameObject Instantiate(GameObject prefab, Transform parent, Vector3 pos, Quaternion rot, bool world)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            if (world) go.transform.SetPositionAndRotation(pos, rot);
            else { go.transform.localPosition = pos; go.transform.localRotation = rot; }
            return go;
        }

        static void AddAudio(Transform t, AudioClip clip, float maxDistance, float volume, bool attach = false)
        {
            var go = attach ? t.gameObject : AssetUtil.Child(t, "Audio_Rumble");
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.playOnAwake = true;
            src.spatialBlend = 1f;
            src.volume = volume;
            src.minDistance = 6f;
            src.maxDistance = maxDistance;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.dopplerLevel = 0f;
        }
    }
}
