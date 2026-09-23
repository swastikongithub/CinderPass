using CinderPass.Hazards;
using UnityEngine;

namespace CinderPass.Environment
{
    /// <summary>One-shot "vehicle hits lava" effect: steam/fire burst and a hot light flash at the contact point.</summary>
    [DisallowMultipleComponent]
    public sealed class HazardEffect : MonoBehaviour
    {
        [SerializeField] ParticleSystem[] bursts = System.Array.Empty<ParticleSystem>();
        [SerializeField] Light flash;
        [SerializeField] AudioSource sizzle;
        [SerializeField, Min(0f)] float flashIntensity = 40f;
        [SerializeField, Min(0.1f)] float flashDecay = 1.6f;

        float flashLevel;

        public void Play(Vector3 position, HazardZone zone)
        {
            float y = zone != null && zone.SurfaceHeight != 0f ? Mathf.Max(zone.SurfaceHeight, position.y - 1.5f) : position.y;
            transform.position = new Vector3(position.x, y, position.z);
            foreach (var ps in bursts) if (ps != null) { ps.Clear(true); ps.Play(true); }
            if (sizzle != null) sizzle.Play();
            flashLevel = 1f;
            if (flash != null) flash.enabled = true;
        }

        public void Stop()
        {
            foreach (var ps in bursts) if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        void Update()
        {
            if (flash == null || flashLevel <= 0f) return;
            flashLevel = Mathf.Max(0f, flashLevel - Time.deltaTime / flashDecay);
            flash.intensity = flashIntensity * flashLevel * (0.8f + 0.2f * Mathf.PerlinNoise(Time.time * 12f, 0f));
            if (flashLevel <= 0f) flash.enabled = false;
        }
    }
}
