using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace CinderPass.Rendering
{
    /// <summary>
    /// Renders <see cref="AtmosphereSettings"/>: one full-screen pass after opaques and the sky (before
    /// transparents) that applies height fog and sun scattering using the depth buffer. The same parameters are
    /// published as shader globals for transparent shaders (CinderAtmosphere.hlsl), which fog themselves.
    /// </summary>
    public sealed class AtmosphereFeature : ScriptableRendererFeature
    {
        [SerializeField] Shader shader;
        [SerializeField] RenderPassEvent passEvent = RenderPassEvent.BeforeRenderingTransparents;

        Material material;
        AtmospherePass pass;

        public override void Create()
        {
            if (shader == null) shader = Shader.Find("Hidden/CinderPass/Atmosphere");
            if (shader == null) return;
            material = CoreUtils.CreateEngineMaterial(shader);
            pass = new AtmospherePass(material) { renderPassEvent = passEvent };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (pass == null) return;
            var type = renderingData.cameraData.cameraType;
            if (type == CameraType.Preview || type == CameraType.Reflection) return;
            pass.ConfigureInput(ScriptableRenderPassInput.Depth);
            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing) => CoreUtils.Destroy(material);

        sealed class AtmospherePass : ScriptableRenderPass
        {
            static readonly int Params0 = Shader.PropertyToID("_CP_AtmosParams0");
            static readonly int Params1 = Shader.PropertyToID("_CP_AtmosParams1");
            static readonly int FogColor = Shader.PropertyToID("_CP_AtmosFogColor");
            static readonly int SunColor = Shader.PropertyToID("_CP_AtmosSunColor");
            static readonly int SunDir = Shader.PropertyToID("_CP_AtmosSunDir");

            readonly Material material;

            sealed class PassData
            {
                public TextureHandle source;
                public Material material;
            }

            public AtmospherePass(Material material) => this.material = material;

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var settings = VolumeManager.instance.stack.GetComponent<AtmosphereSettings>();
                bool active = settings != null && settings.IsActive();
                PublishGlobals(settings, active);
                if (!active) return;

                var resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer) return;
                var source = resources.activeColorTexture;
                var desc = renderGraph.GetTextureDesc(source);
                desc.name = "_CP_AtmosphereColor";
                desc.clearBuffer = false;
                var target = renderGraph.CreateTexture(desc);

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("CinderPass Atmosphere", out var data))
                {
                    data.source = source;
                    data.material = material;
                    builder.UseTexture(source, AccessFlags.Read);
                    if (resources.cameraDepthTexture.IsValid()) builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
                    builder.SetRenderAttachment(target, 0, AccessFlags.Write);
                    builder.SetRenderFunc((PassData d, RasterGraphContext ctx) =>
                        Blitter.BlitTexture(ctx.cmd, d.source, new Vector4(1f, 1f, 0f, 0f), d.material, 0));
                }
                resources.cameraColor = target;
            }

            // Globals are set immediately: each camera's graph executes right after it is recorded, and the
            // transparent shaders of the same camera read them. Inactive = zero density = no fog anywhere.
            static void PublishGlobals(AtmosphereSettings s, bool active)
            {
                if (!active)
                {
                    Shader.SetGlobalVector(Params0, Vector4.zero);
                    Shader.SetGlobalVector(Params1, new Vector4(1f, 0f, 0f, 1000f));
                    return;
                }
                float k = s.intensity.value;
                Shader.SetGlobalVector(Params0, new Vector4(s.density.value * k, s.heightFalloff.value, s.baseHeight.value, s.startDistance.value));
                Shader.SetGlobalVector(Params1, new Vector4(s.sunScatterPower.value, s.sunScatterStrength.value, s.maxOpacity.value, s.skyDistance.value));
                Shader.SetGlobalVector(FogColor, (Vector4)s.fogColor.value);

                var sun = RenderSettings.sun;
                Vector3 toSun = sun != null ? -sun.transform.forward : Vector3.up;
                Color sunColor = sun != null && sun.isActiveAndEnabled ? sun.color * sun.intensity : Color.black;
                Shader.SetGlobalVector(SunColor, (Vector4)(sunColor * s.sunScatterColor.value));
                Shader.SetGlobalVector(SunDir, toSun);
            }
        }
    }
}
