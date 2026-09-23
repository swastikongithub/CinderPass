using CinderPass.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CinderPass.EditorTools
{
    /// <summary>
    /// Sky, sun, fog and post-processing. The sun direction is derived from the HDRI itself (rendered and
    /// measured), then the sky is rotated so the sun sits where the level composition wants it.
    /// </summary>
    public static class LightingSetup
    {
        public const string VolumeProfilePath = CinderPaths.Settings + "/VP_CinderPass.asset";
        public const string BasinProfilePath = CinderPaths.Settings + "/VP_EmberBasin.asset";
        /// <summary>Desired sun azimuth (degrees clockwise from +Z / north). South-west keeps north-east views front-lit.</summary>
        public const float SunAzimuth = 218f;

        public static Light Build(Transform parent)
        {
            var sky = MaterialLibrary.Sky(0f, 1f);
            RenderSettings.skybox = sky;
            Vector3 sun0 = MeasureSunDirection(sky);
            float az0 = Mathf.Atan2(sun0.x, sun0.z) * Mathf.Rad2Deg;
            // Skybox _Rotation rotates the sky; find the sign empirically (robust to import conventions).
            float rotation = Mathf.Repeat(SunAzimuth - az0, 360f);
            sky.SetFloat("_Rotation", rotation);
            Vector3 sun = MeasureSunDirection(sky);
            float az = Mathf.Atan2(sun.x, sun.z) * Mathf.Rad2Deg;
            if (Mathf.Abs(Mathf.DeltaAngle(az, SunAzimuth)) > 10f)
            {
                rotation = Mathf.Repeat(az0 - SunAzimuth, 360f);
                sky.SetFloat("_Rotation", rotation);
                sun = MeasureSunDirection(sky);
            }
            Debug.Log($"[Lighting] HDRI sun at azimuth {az0:0.0}; sky rotated {rotation:0.0}; final sun dir {sun}");

            var go = AssetUtil.Child(parent, "Sun");
            var light = AssetUtil.GetOrAdd<Light>(go);
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.96f, 0.9f);
            light.intensity = 2.6f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.92f;
            light.shadowBias = 0.05f;
            light.shadowNormalBias = 0.4f;
            go.transform.rotation = Quaternion.LookRotation(-sun, Vector3.up);
            var data = AssetUtil.GetOrAdd<UniversalAdditionalLightData>(go);
            data.softShadowQuality = SoftShadowQuality.Medium;
            RenderSettings.sun = light;

            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1.05f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 0.9f;
            // Atmospheric perspective is rendered by the AtmosphereFeature (height fog + sun scattering, set up
            // in the volume profile below). Unity's distance fog stays off so nothing is fogged twice.
            RenderSettings.fog = false;
            RenderSettings.fogColor = new Color(0.62f, 0.71f, 0.8f);

            BuildVolume(parent);
            return light;
        }

        /// <summary>Renders the sky into six 90-degree views and returns the direction of the brightest texel.</summary>
        static Vector3 MeasureSunDirection(Material sky)
        {
            RenderSettings.skybox = sky;
            var camGo = new GameObject("SunProbeCamera") { hideFlags = HideFlags.HideAndDontSave };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.cullingMask = 0;
            cam.fieldOfView = 90f;
            cam.aspect = 1f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 10f;
            var extra = camGo.AddComponent<UniversalAdditionalCameraData>();
            extra.renderPostProcessing = false;
            const int size = 128;
            var rt = new RenderTexture(size, size, 16, RenderTextureFormat.ARGBHalf) { name = "SunProbe" };
            cam.targetTexture = rt;
            var read = new Texture2D(size, size, TextureFormat.RGBAHalf, false, true);
            Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right, Vector3.up };
            float best = -1f;
            Vector3 bestDir = Vector3.up;
            foreach (var d in dirs)
            {
                camGo.transform.rotation = Quaternion.LookRotation(d, Mathf.Abs(d.y) > 0.9f ? Vector3.forward : Vector3.up);
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                read.Apply();
                RenderTexture.active = prev;
                var px = read.GetPixels();
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var c = px[y * size + x];
                    float lum = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
                    if (lum <= best) continue;
                    best = lum;
                    Vector2 ndc = new Vector2((x + 0.5f) / size * 2f - 1f, (y + 0.5f) / size * 2f - 1f);
                    bestDir = camGo.transform.TransformDirection(new Vector3(ndc.x, ndc.y, 1f)).normalized;
                }
            }
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(read);
            Object.DestroyImmediate(camGo);
            return bestDir;
        }

        /// <summary>
        /// Local volume over the volcanic basin: denser, low-lying ash haze in warm grey that the camera blends
        /// into as it crosses the ridge (the global profile covers everything else).
        /// </summary>
        public static void BuildBasinAtmosphere(Transform parent, Vector3 centre, Vector3 size)
        {
            var profile = AssetUtil.LoadOrCreate<VolumeProfile>(BasinProfilePath);
            if (!profile.TryGet<AtmosphereSettings>(out var a))
            {
                a = profile.Add<AtmosphereSettings>(true);
                a.name = nameof(AtmosphereSettings);
                AssetDatabase.AddObjectToAsset(a, profile);
            }
            a.active = true;
            a.intensity.Override(1f);
            a.density.Override(0.0021f);
            a.heightFalloff.Override(0.016f);
            a.baseHeight.Override(42f);
            a.startDistance.Override(18f);
            a.fogColor.Override(new Color(0.6f, 0.58f, 0.57f));
            a.sunScatterColor.Override(new Color(1f, 0.66f, 0.38f));
            a.sunScatterStrength.Override(0.42f);
            a.maxOpacity.Override(0.94f);
            EditorUtility.SetDirty(profile);

            var go = AssetUtil.Child(parent, "BasinAtmosphere");
            go.transform.position = centre;
            var box = AssetUtil.GetOrAdd<BoxCollider>(go);
            box.isTrigger = true;
            box.size = size;
            go.layer = 2; // Ignore Raycast: never hit by camera/respawn probes
            var vol = AssetUtil.GetOrAdd<Volume>(go);
            vol.isGlobal = false;
            vol.blendDistance = 140f;
            vol.priority = 1;
            vol.sharedProfile = profile;
        }

        static void BuildVolume(Transform parent)
        {
            var profile = AssetUtil.LoadOrCreate<VolumeProfile>(VolumeProfilePath);
            T Get<T>() where T : VolumeComponent
            {
                if (!profile.TryGet<T>(out var c))
                {
                    c = profile.Add<T>(true);
                    c.name = typeof(T).Name;
                    AssetDatabase.AddObjectToAsset(c, profile);
                }
                c.active = true;
                return c;
            }
            var tone = Get<Tonemapping>();
            tone.mode.Override(TonemappingMode.ACES);
            var color = Get<ColorAdjustments>();
            color.postExposure.Override(0.15f);
            color.contrast.Override(10f);
            color.saturation.Override(2f);
            color.colorFilter.Override(Color.white);
            var wb = Get<WhiteBalance>();
            wb.temperature.Override(-2f);
            wb.tint.Override(1f);
            var lgg = Get<LiftGammaGain>();
            lgg.lift.Override(new Vector4(0.98f, 1f, 1.03f, 0f));
            lgg.gain.Override(new Vector4(1.02f, 1f, 0.97f, 0f));
            var bloom = Get<Bloom>();
            bloom.threshold.Override(1.05f);
            bloom.intensity.Override(0.55f);
            bloom.scatter.Override(0.62f);
            bloom.highQualityFiltering.Override(true);
            var vignette = Get<Vignette>();
            vignette.intensity.Override(0.2f);
            vignette.smoothness.Override(0.45f);
            // Valley haze that thins with altitude, warm forward scattering toward the late-afternoon sun.
            var atmosphere = Get<AtmosphereSettings>();
            atmosphere.intensity.Override(1f);
            atmosphere.density.Override(0.0009f);
            atmosphere.heightFalloff.Override(0.009f);
            atmosphere.baseHeight.Override(30f);
            atmosphere.startDistance.Override(30f);
            atmosphere.fogColor.Override(new Color(0.64f, 0.72f, 0.82f));
            atmosphere.sunScatterColor.Override(new Color(1f, 0.8f, 0.55f));
            atmosphere.sunScatterPower.Override(7f);
            atmosphere.sunScatterStrength.Override(0.3f);
            atmosphere.maxOpacity.Override(0.9f);
            atmosphere.skyDistance.Override(4500f);
            EditorUtility.SetDirty(profile);

            var go = AssetUtil.Child(parent, "PostProcess");
            var vol = AssetUtil.GetOrAdd<Volume>(go);
            vol.isGlobal = true;
            vol.priority = 0;
            vol.sharedProfile = profile;
        }
    }
}
