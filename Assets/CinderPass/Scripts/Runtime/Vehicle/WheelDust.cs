using UnityEngine;

namespace CinderPass.Vehicle
{
    /// <summary>
    /// Emits surface-coloured dust from every grounded wheel through one shared world-space particle system
    /// (a single draw call for all four wheels). Emission scales with speed, slip and the surface's dust amount.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WheelDust : MonoBehaviour
    {
        [SerializeField] VehicleController vehicle;
        [SerializeField] ParticleSystem dust;
        [SerializeField, Min(0f)] float particlesPerMetre = 2f;
        [SerializeField, Min(0f)] float particlesPerSlip = 30f;
        [SerializeField, Min(0f)] float minSpeed = 2.5f;
        [SerializeField, Min(1)] int maxPerFrame = 8;

        float[] accumulators;

        void Start() => accumulators = new float[vehicle.Wheels.Count];

        void Update()
        {
            if (vehicle == null || dust == null || vehicle.IsFrozen) return;
            Vector3 velocity = vehicle.Body.linearVelocity;
            float speed = velocity.magnitude;
            float dt = Time.deltaTime;
            var wheels = vehicle.Wheels;
            for (int i = 0; i < wheels.Count; i++)
            {
                var w = wheels[i];
                if (!w.grounded || w.surface == null) continue;
                float slip = Mathf.Abs(w.hit.sidewaysSlip) + Mathf.Abs(w.hit.forwardSlip);
                float moving = Mathf.Max(0f, speed - minSpeed);
                float rate = (moving * particlesPerMetre + slip * particlesPerSlip) * w.surface.dustAmount;
                accumulators[i] += rate * dt;
                int count = Mathf.Min(maxPerFrame, (int)accumulators[i]);
                if (count <= 0) continue;
                accumulators[i] -= count;

                var ep = new ParticleSystem.EmitParams
                {
                    position = w.hit.point + Vector3.up * 0.15f - velocity.normalized * 0.3f,
                    velocity = -velocity * 0.12f + Vector3.up * (0.8f + slip * 1.5f) + Random.insideUnitSphere * 0.6f,
                    startColor = w.surface.dustColor,
                    startSize = Random.Range(1.2f, 2.3f) * (0.8f + Mathf.Min(speed, 25f) * 0.03f),
                    rotation = Random.Range(0f, 360f),
                    applyShapeToPosition = false
                };
                dust.Emit(ep, count);
            }
        }
    }
}
