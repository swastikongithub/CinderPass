using CinderPass.Environment;
using UnityEditor;
using UnityEngine;

namespace CinderPass.EditorTools
{
    /// <summary>
    /// Environmental storytelling: the expedition basecamp at Pine Hollow (supplies, fuel, spare tyres,
    /// a fire ring) and a few abandoned supplies along the route that hint at earlier survey teams.
    /// </summary>
    public static class PropDresser
    {
        public static int Dress(Transform parent, TerrainGrid g, WorldField f, RouteSamples route)
        {
            var d = f.D;
            GameObject P(string name) => AssetDatabase.LoadAssetAtPath<GameObject>($"{CinderPaths.EnvPrefabs}/Props/{name}.prefab");
            GameObject G(string name) => AssetDatabase.LoadAssetAtPath<GameObject>($"{CinderPaths.EnvPrefabs}/Geology/{name}.prefab");
            var crate = P("Prop_Crate");
            var mil = P("Prop_MilitaryCrate");
            var can = P("Prop_Jerrycan");
            var tank = P("Prop_PropaneTank");
            var tyre = P("Prop_Tyre");
            var log = P("Log_Fallen");
            var stone = G("Basalt_B");
            var rng = new System.Random(d.seed + 5);
            int count = 0;

            // Basecamp sits beside the start of the trail, facing the route.
            var camp = AssetUtil.Child(parent, "Basecamp").transform;
            Vector3 start = route.points[0];
            Vector3 fwd = (route.points[20] - route.points[0]);
            fwd.y = 0f;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 c = start - right * 16f - fwd * 6f; // camp centre, left of the start
            Quaternion face = Quaternion.LookRotation(right, Vector3.up);

            (GameObject prefab, Vector3 local, float yaw, float scale)[] layout =
            {
                (mil, new Vector3(-3.5f, 0f, 2.2f), 8f, 1f),
                (mil, new Vector3(-3.4f, 0.98f, 2.1f), 14f, 1f),
                (crate, new Vector3(-1.6f, 0f, 3.1f), -20f, 1f),
                (crate, new Vector3(-0.8f, 0f, 3.6f), 35f, 0.9f),
                (can, new Vector3(-2.2f, 0f, 1.2f), 80f, 1f),
                (can, new Vector3(-2.6f, 0f, 1.0f), 95f, 1f),
                (can, new Vector3(-2.4f, 0f, 0.6f), 60f, 1f),
                (tank, new Vector3(-4.6f, 0f, 0.4f), 0f, 1f),
                (tank, new Vector3(-4.9f, 0f, 1.0f), 40f, 1f),
                (tyre, new Vector3(2.8f, 0f, 3.5f), 0f, 1.35f),
                (tyre, new Vector3(2.8f, 0.2f, 3.5f), 20f, 1.35f),
                (tyre, new Vector3(3.9f, 0f, 2.6f), 70f, 1.35f),
                (log, new Vector3(1.8f, 0f, -2.6f), 90f, 1.3f),
                (log, new Vector3(-1.9f, 0f, -3.4f), 10f, 1.2f),
            };
            foreach (var (prefab, local, yaw, scale) in layout)
            {
                if (prefab == null) continue;
                Vector3 wp = c + face * new Vector3(local.x, 0f, local.z);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, camp);
                go.transform.position = new Vector3(wp.x, g.Height(wp.x, wp.z) + local.y, wp.z);
                go.transform.rotation = face * Quaternion.Euler(0f, yaw, 0f);
                go.transform.localScale = Vector3.one * scale;
                count++;
            }

            // Fire ring with embers and a warm light (the camp is still in use).
            var fire = AssetUtil.Child(camp, "FireRing");
            Vector3 fc = c + face * new Vector3(0f, 0f, -0.5f);
            fire.transform.position = new Vector3(fc.x, g.Height(fc.x, fc.z), fc.z);
            for (int i = 0; i < 9; i++)
            {
                float a = i / 9f * Mathf.PI * 2f;
                Vector3 sp = fc + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.85f;
                var s = (GameObject)PrefabUtility.InstantiatePrefab(stone, fire.transform);
                s.transform.position = new Vector3(sp.x, g.Height(sp.x, sp.z) - 0.05f, sp.z);
                s.transform.rotation = Quaternion.Euler(0f, rng.Next(360), 0f);
                s.transform.localScale = Vector3.one * (0.18f + (float)rng.NextDouble() * 0.08f);
                count++;
            }
            var emberFx = (GameObject)PrefabUtility.InstantiatePrefab(VfxFactory.LavaEmbers("FX_CampfireEmbers", 0.4f, 6f), fire.transform);
            emberFx.transform.localPosition = Vector3.up * 0.2f;
            var smokeFx = (GameObject)PrefabUtility.InstantiatePrefab(VfxFactory.LavaSmoke("FX_CampfireSmoke", 0.4f, 0.8f), fire.transform);
            smokeFx.transform.localPosition = Vector3.up * 0.5f;
            var lightGo = AssetUtil.Child(fire.transform, "FireLight");
            lightGo.transform.localPosition = Vector3.up * 0.6f;
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.55f, 0.25f);
            l.range = 9f;
            l.intensity = 3.5f;
            l.shadows = LightShadows.None;
            lightGo.AddComponent<LavaLight>().Configure(3.5f, 0.35f, 2.2f);

            // Abandoned supplies along the route (earlier survey teams).
            var cache = AssetUtil.Child(parent, "SupplyCaches").transform;
            float[] fractions = { 0.17f, 0.3f, 0.62f, 0.8f };
            foreach (float fr in fractions)
            {
                int i = Mathf.RoundToInt(fr * (route.points.Length - 1));
                Vector3 p = route.points[i];
                Vector3 fw = (route.points[(i + 10) % route.points.Length] - p).normalized;
                Vector3 rt = Vector3.Cross(Vector3.up, new Vector3(fw.x, 0f, fw.z)).normalized;
                Vector3 bp = p + rt * (d.roadHalfWidth + 3.5f);
                if (g.LavaDistance(bp.x, bp.z) < 10f) continue;
                var pieces = new[] { mil, can, tyre };
                for (int k = 0; k < pieces.Length; k++)
                {
                    Vector3 o = bp + fw * (k * 1.3f) + rt * ((float)rng.NextDouble() * 0.8f);
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(pieces[k], cache);
                    go.transform.position = new Vector3(o.x, g.Height(o.x, o.z), o.z);
                    go.transform.rotation = Quaternion.Euler(0f, rng.Next(360), 0f);
                    count++;
                }
            }
            return count;
        }
    }
}
