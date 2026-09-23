using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CinderPass.Rendering
{
    /// <summary>
    /// Volume-driven atmosphere: exponential height fog with sun scattering, rendered by
    /// <see cref="AtmosphereFeature"/>. Because it is a volume component, regions can override it with local
    /// volumes (the volcanic basin has a denser, ash-grey haze) and the camera blends between them.
    /// </summary>
    [Serializable]
    [VolumeComponentMenu("Cinder Pass/Atmosphere")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public sealed class AtmosphereSettings : VolumeComponent
    {
        [Tooltip("Overall strength (0 disables the effect).")]
        public ClampedFloatParameter intensity = new ClampedFloatParameter(0f, 0f, 1f);
        [Tooltip("Fog density at the base height, per metre.")]
        public MinFloatParameter density = new MinFloatParameter(0.0012f, 0f);
        [Tooltip("How quickly the fog thins with altitude (1/m). Higher keeps it in the valleys.")]
        public MinFloatParameter heightFalloff = new MinFloatParameter(0.012f, 0f);
        [Tooltip("World height where the density applies.")]
        public FloatParameter baseHeight = new FloatParameter(25f);
        [Tooltip("No fog closer than this (keeps the car and road crisp).")]
        public MinFloatParameter startDistance = new MinFloatParameter(25f, 0f);
        [Tooltip("Ambient colour of the fog (the sky light it scatters).")]
        public ColorParameter fogColor = new ColorParameter(new Color(0.62f, 0.71f, 0.8f), true, false, true);
        [Tooltip("Tint of sunlight scattered toward the camera when looking toward the sun.")]
        public ColorParameter sunScatterColor = new ColorParameter(new Color(1f, 0.82f, 0.6f), true, false, true);
        [Tooltip("Sharpness of the forward-scattering glow around the sun.")]
        public MinFloatParameter sunScatterPower = new MinFloatParameter(6f, 1f);
        [Tooltip("Strength of the sun glow, relative to the sun's intensity.")]
        public MinFloatParameter sunScatterStrength = new MinFloatParameter(0.35f, 0f);
        [Tooltip("Upper limit of fog opacity (distant peaks stay faintly visible).")]
        public ClampedFloatParameter maxOpacity = new ClampedFloatParameter(0.92f, 0f, 1f);
        [Tooltip("Distance used for sky pixels (sets how much haze the horizon receives).")]
        public MinFloatParameter skyDistance = new MinFloatParameter(5000f, 100f);

        public bool IsActive() => intensity.value > 0f && density.value > 0f;
    }
}
