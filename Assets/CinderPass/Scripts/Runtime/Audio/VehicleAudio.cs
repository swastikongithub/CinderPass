using CinderPass.Vehicle;
using UnityEngine;

namespace CinderPass.Audio
{
    /// <summary>Engine loop pitched by RPM, tyre-on-surface loop scaled by speed, and impact one-shots.</summary>
    [DisallowMultipleComponent]
    public sealed class VehicleAudio : MonoBehaviour
    {
        [SerializeField] VehicleController vehicle;
        [SerializeField] AudioSource engine;
        [SerializeField] AudioSource tyres;
        [SerializeField] AudioSource impacts;
        [SerializeField] AudioClip impactClip;

        [Header("Engine")]
        [SerializeField] Vector2 pitchRange = new Vector2(0.55f, 1.85f);
        [SerializeField] Vector2 volumeRange = new Vector2(0.28f, 0.7f);

        [Header("Tyres")]
        [SerializeField, Min(0.1f)] float fullVolumeSpeed = 18f;
        [SerializeField, Range(0f, 1f)] float maxTyreVolume = 0.55f;

        float throttleSmoothed;

        void OnEnable()
        {
            if (vehicle != null) vehicle.Impact += OnImpact;
        }

        void OnDisable()
        {
            if (vehicle != null) vehicle.Impact -= OnImpact;
        }

        void Update()
        {
            if (vehicle == null) return;
            float dt = Time.deltaTime;
            var input = vehicle.CurrentInput;
            float demand = vehicle.Drivetrain.InReverse ? input.Brake : input.Throttle;
            throttleSmoothed = Mathf.Lerp(throttleSmoothed, vehicle.IsFrozen ? 0f : demand, 1f - Mathf.Exp(-6f * dt));

            if (engine != null)
            {
                float rpm = vehicle.Drivetrain.RpmNormalized;
                engine.pitch = Mathf.Lerp(pitchRange.x, pitchRange.y, rpm);
                engine.volume = Mathf.Lerp(volumeRange.x, volumeRange.y, Mathf.Max(throttleSmoothed, rpm * 0.5f));
            }

            if (tyres != null)
            {
                float speedFactor = Mathf.Clamp01(vehicle.Body.linearVelocity.magnitude / fullVolumeSpeed);
                float surfaceNoise = 0f;
                int n = 0;
                foreach (var w in vehicle.Wheels)
                {
                    if (!w.grounded || w.surface == null) continue;
                    surfaceNoise += w.surface.rollNoise;
                    n++;
                }
                surfaceNoise = n > 0 ? surfaceNoise / vehicle.Wheels.Count : 0f;
                float target = maxTyreVolume * speedFactor * surfaceNoise + vehicle.TyreSlip * 0.25f;
                tyres.volume = Mathf.Lerp(tyres.volume, target, 1f - Mathf.Exp(-8f * dt));
                tyres.pitch = 0.8f + speedFactor * 0.45f;
            }
        }

        void OnImpact(Collision c)
        {
            if (impacts == null || impactClip == null) return;
            float strength = Mathf.Clamp01(c.impulse.magnitude / (vehicle.Config.mass * 10f));
            impacts.pitch = Random.Range(0.85f, 1.1f);
            impacts.PlayOneShot(impactClip, 0.3f + strength * 0.7f);
        }
    }
}
