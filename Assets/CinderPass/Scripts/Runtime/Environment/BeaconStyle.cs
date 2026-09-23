using System;
using UnityEngine;

namespace CinderPass.Environment
{
    /// <summary>
    /// Central appearance definition for every route beacon in the game.
    /// All beacon instances share one glow material; this asset writes its values into that material,
    /// so editing the asset (in Edit or Play mode) updates every beacon at once. The pulse itself is
    /// computed in the shader from each instance's world position, so there is no per-beacon script cost.
    /// </summary>
    [CreateAssetMenu(menuName = "Cinder Pass/Beacon Style", fileName = "BeaconStyle")]
    public sealed class BeaconStyle : ScriptableObject
    {
        static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");
        static readonly int GlowIntensityId = Shader.PropertyToID("_GlowIntensity");
        static readonly int PulseAmountId = Shader.PropertyToID("_PulseAmount");
        static readonly int PulseSpeedId = Shader.PropertyToID("_PulseSpeed");
        static readonly int FresnelPowerId = Shader.PropertyToID("_FresnelPower");
        static readonly int CoreTintId = Shader.PropertyToID("_CoreTint");

        [Header("Glow")]
        [ColorUsage(false, false)] public Color glowColor = new Color(0.42f, 0.95f, 0.86f);
        [ColorUsage(false, false)] public Color coreTint = new Color(0.9f, 1f, 0.97f);
        [Min(0f)] public float glowIntensity = 5.5f;
        [Range(0f, 1f)] public float pulseAmount = 0.35f;
        [Tooltip("Pulses per second.")]
        [Min(0f)] public float pulseSpeed = 0.55f;
        [Min(0.1f)] public float fresnelPower = 2.2f;

        [Header("Shared materials driven by this style")]
        public Material glowMaterial;
        public Material haloMaterial;

        [Header("Real-time light (pooled, nearest beacons only)")]
        public Color lightColor = new Color(0.5f, 0.95f, 0.88f);
        [Min(0f)] public float lightIntensity = 3f;
        [Min(0.5f)] public float lightRange = 10f;

        public event Action Changed;

        void OnEnable() => Apply();
        void OnValidate() => Apply();

        /// <summary>Pushes this style into the shared materials.</summary>
        public void Apply()
        {
            Write(glowMaterial);
            Write(haloMaterial);
            Changed?.Invoke();
        }

        void Write(Material m)
        {
            if (m == null) return;
            m.SetColor(GlowColorId, glowColor);
            m.SetColor(CoreTintId, coreTint);
            m.SetFloat(GlowIntensityId, glowIntensity);
            m.SetFloat(PulseAmountId, pulseAmount);
            m.SetFloat(PulseSpeedId, pulseSpeed);
            m.SetFloat(FresnelPowerId, fresnelPower);
        }

        /// <summary>CPU mirror of the shader pulse (0..1) so pooled lights breathe in sync with the glass.</summary>
        public float EvaluatePulse(Vector3 instancePosition, float time)
        {
            // Must match BeaconPhase() in CinderPass/BeaconGlow.shader.
            float h = Mathf.Sin(instancePosition.x * 0.1291f + instancePosition.z * 0.7823f) * 437.585f;
            float phase = (h - Mathf.Floor(h)) * Mathf.PI * 2f;
            float wave = 0.5f + 0.5f * Mathf.Sin(time * pulseSpeed * Mathf.PI * 2f + phase);
            return 1f - pulseAmount * wave;
        }
    }
}
