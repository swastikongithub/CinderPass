using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CinderPass.EditorTools
{
    /// <summary>
    /// Authors every particle effect as a prefab with explicit, bounded settings (max particles, lifetimes,
    /// soft particles, shared materials). Effects are spatially concentrated: nothing floods the screen.
    /// </summary>
    public static class VfxFactory
    {
        public static Material Dust, Smoke, Plume, Steam, Ember, Spark, Ash, Glow, Haze;

        public static void BuildMaterials()
        {
            var smokeTex = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureBaker.SmokePath);
            var emberTex = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureBaker.EmberPath);
            var sparkTex = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureBaker.SparkPath);
            var ashTex = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureBaker.AshPath);
            Dust = MaterialLibrary.Particle("M_FX_Dust", smokeTex, Color.white, MaterialLibrary.Blend.Alpha, 1f, 0.9f, 1.5f);
            Smoke = MaterialLibrary.Particle("M_FX_Smoke", smokeTex, Color.white, MaterialLibrary.Blend.Alpha, 1f, 1f, 4f);
            Plume = MaterialLibrary.Particle("M_FX_Plume", smokeTex, Color.white, MaterialLibrary.Blend.Alpha, 1f, 1f, 25f);
            Steam = MaterialLibrary.Particle("M_FX_Steam", smokeTex, Color.white, MaterialLibrary.Blend.Alpha, 1.1f, 0.75f, 2f);
            Ember = MaterialLibrary.Particle("M_FX_Ember", emberTex, Color.white, MaterialLibrary.Blend.Additive, 9f, 0f, 0.3f);
            Spark = MaterialLibrary.Particle("M_FX_Spark", sparkTex, Color.white, MaterialLibrary.Blend.Additive, 7f, 0f, 0.3f);
            Ash = MaterialLibrary.Particle("M_FX_Ash", ashTex, Color.white, MaterialLibrary.Blend.Alpha, 1f, 0.6f, 0.3f);
            Glow = MaterialLibrary.Particle("M_FX_Glow", smokeTex, new Color(1f, 0.45f, 0.15f, 1f), MaterialLibrary.Blend.Additive, 1.6f, 0f, 20f);
            Haze = AssetUtil.Material(MaterialLibrary.MatPath("VFX", "M_FX_HeatHaze"), AssetUtil.FindShader("CinderPass/HeatHaze"));
            Haze.SetTexture("_NoiseTex", AssetDatabase.LoadAssetAtPath<Texture2D>(TextureBaker.LavaNoisePath));
            EditorUtility.SetDirty(Haze);
        }

        // ---------------------------------------------------------------- helpers

        static ParticleSystem NewSystem(string name, Transform parent, out ParticleSystemRenderer renderer)
        {
            // Material references are statics: rebuild them after a domain reload.
            if (Dust == null || Ember == null || Haze == null) BuildMaterials();
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.maxParticleSize = 3f;
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.playOnAwake = true;
            var shape = ps.shape;
            shape.enabled = false;
            return ps;
        }

        static void SheetAnimation(ParticleSystem ps)
        {
            var tsa = ps.textureSheetAnimation;
            tsa.enabled = true;
            tsa.numTilesX = 2;
            tsa.numTilesY = 2;
            tsa.animation = ParticleSystemAnimationType.WholeSheet;
            tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
            tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, 3.99f);
        }

        static Gradient Fade(Color a, Color b, float inT = 0.1f, float outT = 0.7f, float peak = 1f)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(a, 0f), new GradientColorKey(b, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(peak, inT), new GradientAlphaKey(peak * 0.8f, outT), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        static void ColorOverLife(ParticleSystem ps, Gradient g)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(g);
        }

        static void SizeOverLife(ParticleSystem ps, float from, float to)
        {
            var s = ps.sizeOverLifetime;
            s.enabled = true;
            s.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, from, 1f, to));
        }

        static void Noise(ParticleSystem ps, float strength, float frequency, float scrollSpeed = 0.2f)
        {
            var n = ps.noise;
            n.enabled = true;
            n.strength = strength;
            n.frequency = frequency;
            n.scrollSpeed = scrollSpeed;
            n.quality = ParticleSystemNoiseQuality.Medium;
            n.octaveCount = 2;
        }

        public static GameObject Save(GameObject go, string name)
        {
            return VegetationBuilder.SavePrefab(go, $"{CinderPaths.VfxPrefabs}/{name}.prefab");
        }

        // ---------------------------------------------------------------- effects

        /// <summary>Script-driven dust (emitted by WheelDust at the tyre contacts). Lives inside the vehicle prefab.</summary>
        public static ParticleSystem WheelDust(Transform parent)
        {
            var ps = NewSystem("FX_WheelDust", parent, out var r);
            r.sharedMaterial = Dust;
            var main = ps.main;
            main.loop = true;
            main.maxParticles = 800;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.8f);
            main.startSpeed = 0f;
            main.gravityModifier = -0.01f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var em = ps.emission;
            em.enabled = false;
            SheetAnimation(ps);
            SizeOverLife(ps, 0.6f, 2.6f);
            ColorOverLife(ps, Fade(Color.white, Color.white, 0.06f, 0.45f, 0.9f));
            var lim = ps.limitVelocityOverLifetime;
            lim.enabled = true;
            lim.drag = 1.8f;
            lim.dampen = 0.2f;
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            Noise(ps, 0.35f, 0.4f);
            return ps;
        }

        public static GameObject VolcanoPlume()
        {
            var root = new GameObject("FX_VolcanoPlume");
            // Main ash column.
            var ps = NewSystem("Column", root.transform, out var r);
            r.sharedMaterial = Plume;
            r.maxParticleSize = 6f;
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.maxParticles = 220;
            main.startLifetime = new ParticleSystem.MinMaxCurve(45f, 70f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(6f, 11f);
            main.startSize = new ParticleSystem.MinMaxCurve(35f, 70f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.34f, 0.32f, 0.31f), new Color(0.5f, 0.48f, 0.46f));
            var em = ps.emission;
            em.rateOverTime = 3.2f;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = 28f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            SheetAnimation(ps);
            SizeOverLife(ps, 0.7f, 3.4f);
            ColorOverLife(ps, Fade(new Color(0.5f, 0.46f, 0.44f), new Color(0.85f, 0.84f, 0.84f), 0.05f, 0.55f, 0.85f));
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(1.5f, 4f);
            vel.y = new ParticleSystem.MinMaxCurve(0f, 1f);
            vel.z = new ParticleSystem.MinMaxCurve(0.5f, 2f);
            var lim = ps.limitVelocityOverLifetime;
            lim.enabled = true;
            lim.drag = 0.08f;
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);

            // Internal glow lighting the column from the crater below.
            var g = NewSystem("CraterGlow", root.transform, out var gr);
            gr.sharedMaterial = Glow;
            gr.maxParticleSize = 6f;
            var gm = g.main;
            gm.loop = true;
            gm.prewarm = true;
            gm.maxParticles = 20;
            gm.startLifetime = new ParticleSystem.MinMaxCurve(5f, 8f);
            gm.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5f);
            gm.startSize = new ParticleSystem.MinMaxCurve(40f, 70f);
            gm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var gem = g.emission;
            gem.rateOverTime = 2f;
            var gs = g.shape;
            gs.enabled = true;
            gs.shapeType = ParticleSystemShapeType.Sphere;
            gs.radius = 15f;
            SheetAnimation(g);
            ColorOverLife(g, Fade(new Color(1f, 0.5f, 0.2f), new Color(0.8f, 0.25f, 0.08f), 0.2f, 0.5f, 0.22f));
            return Save(root, "FX_VolcanoPlume");
        }

        public static GameObject Eruption()
        {
            var root = new GameObject("FX_Eruption");
            // Lava bombs with glowing trails.
            var bombs = NewSystem("LavaBombs", root.transform, out var br);
            br.sharedMaterial = Ember;
            br.trailMaterial = Spark;
            var bm = bombs.main;
            bm.loop = false;
            bm.playOnAwake = false;
            bm.duration = 2.5f;
            bm.maxParticles = 60;
            bm.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
            bm.startSpeed = new ParticleSystem.MinMaxCurve(35f, 75f);
            bm.startSize = new ParticleSystem.MinMaxCurve(2f, 4.5f);
            bm.gravityModifier = 1f;
            bm.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.75f, 0.35f), new Color(1f, 0.45f, 0.1f));
            var bem = bombs.emission;
            bem.rateOverTime = 0f;
            bem.SetBursts(new[] { new ParticleSystem.Burst(0f, 25, 40), new ParticleSystem.Burst(0.6f, 10, 18) });
            var bs = bombs.shape;
            bs.enabled = true;
            bs.shapeType = ParticleSystemShapeType.Cone;
            bs.angle = 28f;
            bs.radius = 12f;
            bs.rotation = new Vector3(-90f, 0f, 0f);
            var trails = bombs.trails;
            trails.enabled = true;
            trails.lifetime = new ParticleSystem.MinMaxCurve(0.25f);
            trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
            trails.dieWithParticles = true;
            trails.minVertexDistance = 3f;
            var bcol = bombs.colorOverLifetime;
            bcol.enabled = true;
            var bg = new Gradient();
            bg.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.9f, 0.6f), 0f), new GradientColorKey(new Color(1f, 0.3f, 0.05f), 0.6f), new GradientColorKey(new Color(0.3f, 0.05f, 0.02f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            bcol.color = bg;

            // Ash burst.
            var ash = NewSystem("AshBurst", root.transform, out var ar);
            ar.sharedMaterial = Plume;
            ar.maxParticleSize = 6f;
            var am = ash.main;
            am.loop = false;
            am.playOnAwake = false;
            am.duration = 3f;
            am.maxParticles = 40;
            am.startLifetime = new ParticleSystem.MinMaxCurve(18f, 26f);
            am.startSpeed = new ParticleSystem.MinMaxCurve(14f, 26f);
            am.startSize = new ParticleSystem.MinMaxCurve(30f, 55f);
            am.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            am.startColor = new ParticleSystem.MinMaxGradient(new Color(0.22f, 0.2f, 0.2f), new Color(0.35f, 0.33f, 0.32f));
            var aem = ash.emission;
            aem.rateOverTime = 0f;
            aem.SetBursts(new[] { new ParticleSystem.Burst(0f, 16, 24) });
            var ashShape = ash.shape;
            ashShape.enabled = true;
            ashShape.shapeType = ParticleSystemShapeType.Cone;
            ashShape.angle = 20f;
            ashShape.radius = 18f;
            ashShape.rotation = new Vector3(-90f, 0f, 0f);
            SheetAnimation(ash);
            SizeOverLife(ash, 0.6f, 2.6f);
            ColorOverLife(ash, Fade(Color.white, new Color(0.8f, 0.8f, 0.8f), 0.03f, 0.5f, 0.95f));
            var alim = ash.limitVelocityOverLifetime;
            alim.enabled = true;
            alim.drag = 0.35f;
            return Save(root, "FX_Eruption");
        }

        /// <summary>Embers rising off a lava surface. Rate scales with area.</summary>
        public static GameObject LavaEmbers(string name, float radius, float rate, bool line = false, float lineLength = 0f)
        {
            var ps = NewSystem(name, null, out var r);
            r.sharedMaterial = Ember;
            r.maxParticleSize = 0.05f;
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.maxParticles = Mathf.Clamp(Mathf.RoundToInt(rate * 6f), 20, 160);
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 5.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 2.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
            main.gravityModifier = -0.06f;
            var em = ps.emission;
            em.rateOverTime = rate;
            var shape = ps.shape;
            shape.enabled = true;
            if (line)
            {
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(radius * 1.6f, 0.2f, lineLength);
            }
            else
            {
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = radius * 0.85f;
                shape.rotation = new Vector3(-90f, 0f, 0f);
            }
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.85f, 0.5f), 0f), new GradientColorKey(new Color(1f, 0.4f, 0.08f), 0.4f), new GradientColorKey(new Color(0.6f, 0.1f, 0.03f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            SizeOverLife(ps, 1f, 0.3f);
            Noise(ps, 0.9f, 0.6f, 0.4f);
            return Save(ps.gameObject, name);
        }

        public static GameObject LavaSmoke(string name, float radius, float rate, bool line = false, float lineLength = 0f)
        {
            var ps = NewSystem(name, null, out var r);
            r.sharedMaterial = Smoke;
            r.maxParticleSize = 2.5f;
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.maxParticles = Mathf.Clamp(Mathf.RoundToInt(rate * 14f), 10, 90);
            main.startLifetime = new ParticleSystem.MinMaxCurve(9f, 14f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(radius * 0.5f + 2f, radius * 0.9f + 4f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.52f, 0.5f, 0.49f), new Color(0.7f, 0.68f, 0.66f));
            var em = ps.emission;
            em.rateOverTime = rate;
            var shape = ps.shape;
            shape.enabled = true;
            if (line)
            {
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(radius * 1.4f, 0.5f, lineLength);
            }
            else
            {
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = radius * 0.7f;
                shape.rotation = new Vector3(-90f, 0f, 0f);
            }
            SheetAnimation(ps);
            SizeOverLife(ps, 0.6f, 2.4f);
            ColorOverLife(ps, Fade(new Color(0.9f, 0.6f, 0.45f), Color.white, 0.12f, 0.5f, 0.42f));
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);
            vel.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.z = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
            Noise(ps, 0.4f, 0.2f);
            return Save(ps.gameObject, name);
        }

        public static GameObject HeatHaze(string name, float radius, float rate)
        {
            var ps = NewSystem(name, null, out var r);
            r.sharedMaterial = Haze;
            r.sortMode = ParticleSystemSortMode.None;
            r.maxParticleSize = 1.5f;
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.maxParticles = 24;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(radius * 0.6f + 2f, radius + 4f);
            var em = ps.emission;
            em.rateOverTime = rate;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius * 0.6f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            ColorOverLife(ps, Fade(Color.white, Color.white, 0.25f, 0.6f, 1f));
            return Save(ps.gameObject, name);
        }

        public static GameObject Fumarole()
        {
            var ps = NewSystem("FX_Fumarole", null, out var r);
            r.sharedMaterial = Steam;
            r.maxParticleSize = 2f;
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.maxParticles = 70;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 5.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.92f, 0.92f, 0.9f), Color.white);
            var em = ps.emission;
            em.rateOverTime = 7f;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 9f;
            shape.radius = 0.5f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            SheetAnimation(ps);
            SizeOverLife(ps, 0.5f, 5f);
            ColorOverLife(ps, Fade(Color.white, Color.white, 0.06f, 0.35f, 0.55f));
            var lim = ps.limitVelocityOverLifetime;
            lim.enabled = true;
            lim.drag = 0.5f;
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
            vel.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.z = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
            Noise(ps, 0.6f, 0.35f);
            return Save(ps.gameObject, "FX_Fumarole");
        }

        public static GameObject AshFall()
        {
            var ps = NewSystem("FX_AshFall", null, out var r);
            r.sharedMaterial = Ash;
            r.sortMode = ParticleSystemSortMode.None;
            r.maxParticleSize = 0.02f;
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.maxParticles = 900;
            main.startLifetime = new ParticleSystem.MinMaxCurve(9f, 13f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0.025f;
            var em = ps.emission;
            em.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(70f, 16f, 70f);
            ColorOverLife(ps, Fade(Color.white, Color.white, 0.1f, 0.85f, 0.9f));
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-2f, 2f);
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.3f, 1.0f);
            vel.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.z = new ParticleSystem.MinMaxCurve(0.1f, 0.4f);
            Noise(ps, 0.5f, 0.3f);
            ps.gameObject.AddComponent<CinderPass.Environment.RegionalEmission>();
            return Save(ps.gameObject, "FX_AshFall");
        }

        public static GameObject HazardBurst()
        {
            var root = new GameObject("FX_HazardBurst");
            var steam = NewSystem("Steam", root.transform, out var sr);
            sr.sharedMaterial = Steam;
            var sm = steam.main;
            sm.loop = false;
            sm.playOnAwake = false;
            sm.duration = 1.2f;
            sm.maxParticles = 40;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
            sm.startSpeed = new ParticleSystem.MinMaxCurve(3f, 8f);
            sm.startSize = new ParticleSystem.MinMaxCurve(2f, 4.5f);
            sm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var sem = steam.emission;
            sem.rateOverTime = 0f;
            sem.SetBursts(new[] { new ParticleSystem.Burst(0f, 24, 32), new ParticleSystem.Burst(0.4f, 8, 12) });
            var ss = steam.shape;
            ss.enabled = true;
            ss.shapeType = ParticleSystemShapeType.Hemisphere;
            ss.radius = 2f;
            ss.rotation = new Vector3(-90f, 0f, 0f);
            SheetAnimation(steam);
            SizeOverLife(steam, 0.6f, 2.8f);
            ColorOverLife(steam, Fade(new Color(1f, 0.75f, 0.55f), Color.white, 0.03f, 0.4f, 0.8f));
            var sl = steam.limitVelocityOverLifetime;
            sl.enabled = true;
            sl.drag = 1.2f;

            var fire = NewSystem("Embers", root.transform, out var fr);
            fr.sharedMaterial = Ember;
            fr.trailMaterial = Spark;
            var fm = fire.main;
            fm.loop = false;
            fm.playOnAwake = false;
            fm.duration = 1f;
            fm.maxParticles = 90;
            fm.startLifetime = new ParticleSystem.MinMaxCurve(1f, 2.4f);
            fm.startSpeed = new ParticleSystem.MinMaxCurve(4f, 12f);
            fm.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
            fm.gravityModifier = 0.6f;
            var fem = fire.emission;
            fem.rateOverTime = 0f;
            fem.SetBursts(new[] { new ParticleSystem.Burst(0f, 60, 80) });
            var fs = fire.shape;
            fs.enabled = true;
            fs.shapeType = ParticleSystemShapeType.Hemisphere;
            fs.radius = 1.5f;
            fs.rotation = new Vector3(-90f, 0f, 0f);
            var ft = fire.trails;
            ft.enabled = true;
            ft.lifetime = new ParticleSystem.MinMaxCurve(0.15f);
            ft.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
            ft.dieWithParticles = true;
            var fcol = fire.colorOverLifetime;
            fcol.enabled = true;
            var fg = new Gradient();
            fg.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.9f, 0.6f), 0f), new GradientColorKey(new Color(1f, 0.35f, 0.06f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            fcol.color = fg;

            var lightGo = new GameObject("Flash");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = Vector3.up * 1.5f;
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.5f, 0.2f);
            l.range = 18f;
            l.intensity = 0f;
            l.shadows = LightShadows.None;
            l.enabled = false;

            var fx = root.AddComponent<CinderPass.Environment.HazardEffect>();
            Wiring.SetArray(fx, "bursts", new[] { steam, fire });
            Wiring.Set(fx, "flash", l);
            return Save(root, "FX_HazardBurst");
        }
    }
}
