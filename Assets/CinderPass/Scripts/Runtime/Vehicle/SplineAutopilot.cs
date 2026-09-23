using System;
using CinderPass.Route;
using UnityEngine;

namespace CinderPass.Vehicle
{
    /// <summary>
    /// Drives the vehicle along the route spline during the introduction.
    /// The car stays a fully dynamic rigidbody on its WheelColliders: the autopilot produces steering
    /// (pure-pursuit on the spline) and throttle/brake (PI speed control against a speed profile), so wheels,
    /// suspension and terrain response behave exactly as in gameplay. A gentle guidance force keeps the
    /// car within centimetres of the spline and its heading on the tangent. Handover to the player is
    /// therefore just swapping the input source - no teleport, no snapping, velocity preserved.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-20)]
    public sealed class SplineAutopilot : MonoBehaviour, IVehicleInputSource
    {
        [Serializable]
        public struct SpeedZone
        {
            public string label;
            [Min(0f)] public float fromDistance;
            [Min(0f)] public float toDistance;
            [Min(0f)] public float speed;
        }

        [SerializeField] VehicleController vehicle;
        [SerializeField] RouteTrack route;

        [Header("Segment")]
        [SerializeField, Min(0f)] float startDistance;
        [Tooltip("Distance along the route where control is handed to the player.")]
        [SerializeField, Min(1f)] float handoverDistance = 600f;

        [Header("Speed profile (m/s)")]
        [SerializeField, Min(0f)] float cruiseSpeed = 14f;
        [SerializeField] SpeedZone[] speedZones = Array.Empty<SpeedZone>();
        [Tooltip("Metres over which speed changes between zones are blended.")]
        [SerializeField, Min(1f)] float zoneBlend = 25f;

        [Header("Steering (pure pursuit)")]
        [SerializeField, Min(1f)] float lookAheadMin = 5.5f;
        [SerializeField, Min(0f)] float lookAheadPerMetrePerSecond = 0.55f;
        [SerializeField, Min(1f)] float lookAheadMax = 18f;

        [Header("Speed controller")]
        [SerializeField, Min(0f)] float throttleGain = 0.45f;
        [SerializeField, Min(0f)] float throttleIntegral = 0.12f;
        [SerializeField, Min(0f)] float brakeGain = 0.3f;

        [Header("Guidance assist")]
        [SerializeField] bool guidanceAssist = true;
        [SerializeField, Min(0f)] float lateralStiffness = 2.2f;
        [SerializeField, Min(0f)] float lateralDamping = 2.6f;
        [SerializeField, Min(0f)] float yawStiffness = 2.5f;
        [SerializeField, Min(0f)] float yawDamping = 1.2f;
        [SerializeField, Min(0f)] float maxAssistAcceleration = 5f;

        int hint = -1;
        float integral;
        float travelled;
        float lastDistance;
        bool completed;

        public bool Active { get; private set; }
        /// <summary>Distance driven since the intro began (monotonic, loop-safe).</summary>
        public float Travelled => travelled;
        public float StartDistance => startDistance;
        public float HandoverDistance => handoverDistance;
        public float CurrentRouteDistance => lastDistance;
        public float CrossTrackError { get; private set; }
        public float TargetSpeed { get; private set; }
        public event Action Completed;

        /// <summary>Places the vehicle at rest on the start of the intro segment, aligned to the tangent.</summary>
        public void PlaceAtStart()
        {
            route.SampleFrame(startDistance, out var p, out var fwd, out _);
            vehicle.Teleport(p + Vector3.up * 0.25f, Quaternion.LookRotation(fwd, Vector3.up));
        }

        public void Begin()
        {
            Active = true;
            completed = false;
            integral = 0f;
            travelled = 0f;
            hint = -1;
            lastDistance = route.FindNearest(vehicle.Body.position, ref hint, 40);
            vehicle.InputSource = this;
        }

        public void Stop() => Active = false;

        public VehicleInputState ReadInput(float dt)
        {
            if (!Active || route == null) return VehicleInputState.Idle;
            Vector3 p = vehicle.Body.position;
            float s = route.FindNearest(p, ref hint, 40);
            travelled += Mathf.Max(0f, route.Delta(lastDistance, s));
            lastDistance = s;
            CrossTrackError = route.LateralOffset(p, s);

            float v = vehicle.ForwardSpeed;
            float remaining = handoverDistance - (startDistance + travelled);
            TargetSpeed = SpeedAt(startDistance + travelled);

            // Pure pursuit toward a look-ahead point on the spline.
            float lookAhead = Mathf.Clamp(lookAheadMin + lookAheadPerMetrePerSecond * Mathf.Max(v, 0f), lookAheadMin, lookAheadMax);
            Vector3 aim = route.PositionAt(s + lookAhead);
            Vector3 local = vehicle.transform.InverseTransformPoint(aim);
            float curvature = 2f * local.x / Mathf.Max(local.x * local.x + local.z * local.z, 0.01f);
            float steerDeg = Mathf.Atan(curvature * vehicle.Wheelbase) * Mathf.Rad2Deg;
            float steer = Mathf.Clamp(steerDeg / Mathf.Max(1f, vehicle.CurrentMaxSteerAngle), -1f, 1f);

            // PI speed control.
            float err = TargetSpeed - v;
            integral = Mathf.Clamp(integral + err * dt, -4f, 6f);
            float throttle = Mathf.Clamp01(throttleGain * err + throttleIntegral * integral);
            float brake = err < -1.2f ? Mathf.Clamp01(-brakeGain * (err + 1.2f)) : 0f;
            if (brake > 0f) { throttle = 0f; integral = Mathf.Min(integral, 0f); }

            if (!completed && remaining <= 0f)
            {
                completed = true;
                Completed?.Invoke();
            }
            return new VehicleInputState { Throttle = throttle, Brake = brake, Steer = steer };
        }

        void FixedUpdate()
        {
            if (!Active || !guidanceAssist || vehicle.IsFrozen || vehicle.GroundedWheelCount < 2) return;
            var body = vehicle.Body;
            route.SampleFrame(lastDistance, out var p, out _, out var right);
            Vector3 tangentAhead = route.TangentAt(lastDistance + 3f);

            float lateral = Vector3.Dot(body.position - p, right);
            float lateralVel = Vector3.Dot(body.linearVelocity, right);
            float acc = Mathf.Clamp(-lateralStiffness * lateral - lateralDamping * lateralVel, -maxAssistAcceleration, maxAssistAcceleration);
            body.AddForce(right * acc, ForceMode.Acceleration);

            Vector3 fwdFlat = Vector3.ProjectOnPlane(vehicle.transform.forward, Vector3.up);
            Vector3 tanFlat = Vector3.ProjectOnPlane(tangentAhead, Vector3.up);
            if (fwdFlat.sqrMagnitude < 1e-4f || tanFlat.sqrMagnitude < 1e-4f) return;
            float yawError = Vector3.SignedAngle(fwdFlat, tanFlat, Vector3.up) * Mathf.Deg2Rad;
            float yawRate = Vector3.Dot(body.angularVelocity, Vector3.up);
            float yawAcc = Mathf.Clamp(yawStiffness * yawError - yawDamping * yawRate, -maxAssistAcceleration, maxAssistAcceleration);
            body.AddTorque(Vector3.up * yawAcc, ForceMode.Acceleration);
        }

        /// <summary>Target speed at an absolute route distance, blended smoothly between zones.</summary>
        public float SpeedAt(float distance)
        {
            float speed = cruiseSpeed;
            foreach (var z in speedZones)
            {
                float fadeIn = Mathf.Clamp01((distance - (z.fromDistance - zoneBlend)) / zoneBlend);
                float fadeOut = Mathf.Clamp01(((z.toDistance + zoneBlend) - distance) / zoneBlend);
                float w = Mathf.SmoothStep(0f, 1f, Mathf.Min(fadeIn, fadeOut));
                speed = Mathf.Lerp(speed, z.speed, w);
            }
            // Start gently from rest.
            float fromStart = distance - startDistance;
            return Mathf.Min(speed, 4f + fromStart * 0.6f);
        }

        public void Configure(VehicleController v, RouteTrack r, float start, float handover, float cruise, SpeedZone[] zones)
        {
            vehicle = v;
            route = r;
            startDistance = start;
            handoverDistance = handover;
            cruiseSpeed = cruise;
            speedZones = zones ?? Array.Empty<SpeedZone>();
        }
    }
}
