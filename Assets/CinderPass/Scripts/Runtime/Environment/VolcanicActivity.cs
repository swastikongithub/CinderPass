using System.Collections;
using CinderPass.Cameras;
using UnityEngine;

namespace CinderPass.Environment
{
    /// <summary>
    /// Occasional eruptions from the summit crater: ejecta and ash bursts, a light flare, a distant rumble,
    /// and camera shake scaled by the viewer's distance. Everything else in the volcanic region is ambient.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VolcanicActivity : MonoBehaviour
    {
        [SerializeField] ParticleSystem[] eruptionBursts = System.Array.Empty<ParticleSystem>();
        [SerializeField] Light craterLight;
        [SerializeField] AudioSource rumble;
        [SerializeField] CameraRig cameraRig;
        [SerializeField] Transform viewer;
        [SerializeField] Vector2 intervalRange = new Vector2(16f, 30f);
        [SerializeField, Min(0f)] float firstEruptionDelay = 9f;
        [SerializeField, Min(0f)] float flareIntensity = 60f;
        [SerializeField, Min(0.1f)] float flareDuration = 3.5f;
        [SerializeField, Min(1f)] float shakeRadius = 650f;
        [SerializeField, Range(0f, 1f)] float maxShake = 0.35f;

        float baseLightIntensity;

        public void Configure(ParticleSystem[] bursts, Light light, AudioSource audio, CameraRig rig, Transform viewerTransform)
        {
            eruptionBursts = bursts;
            craterLight = light;
            rumble = audio;
            cameraRig = rig;
            viewer = viewerTransform;
        }

        void OnEnable()
        {
            if (craterLight != null) baseLightIntensity = craterLight.intensity;
            StartCoroutine(Loop());
        }

        IEnumerator Loop()
        {
            yield return new WaitForSeconds(firstEruptionDelay);
            while (enabled)
            {
                Erupt();
                yield return new WaitForSeconds(Random.Range(intervalRange.x, intervalRange.y));
            }
        }

        /// <summary>Triggers an eruption immediately (also useful for demonstrations).</summary>
        [ContextMenu("Erupt Now")]
        public void Erupt()
        {
            foreach (var ps in eruptionBursts) if (ps != null) ps.Play(true);
            if (rumble != null) rumble.Play();
            if (craterLight != null) StartCoroutine(Flare());
            if (cameraRig != null && viewer != null)
            {
                float d = Vector3.Distance(viewer.position, transform.position);
                float k = 1f - Mathf.Clamp01(d / shakeRadius);
                if (k > 0f) cameraRig.AddShake(maxShake * k);
            }
        }

        IEnumerator Flare()
        {
            float t = 0f;
            while (t < flareDuration)
            {
                t += Time.deltaTime;
                float k = t / flareDuration;
                float env = Mathf.Sin(Mathf.Clamp01(k * 4f) * Mathf.PI * 0.5f) * (1f - k);
                craterLight.intensity = baseLightIntensity + flareIntensity * env;
                yield return null;
            }
            craterLight.intensity = baseLightIntensity;
        }
    }
}
