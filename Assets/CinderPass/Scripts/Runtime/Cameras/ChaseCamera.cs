using CinderPass.Core;
using UnityEngine;

namespace CinderPass.Cameras
{
    /// <summary>
    /// Third-person vehicle camera: follows the direction of travel, leans into slopes, widens with speed,
    /// can be orbited by the player, and never clips into terrain or rocks.
    /// </summary>
    public sealed class ChaseCamera : CameraMode
    {
        [SerializeField] Transform target;
        [SerializeField] Rigidbody targetBody;
        [SerializeField] InputHub input;

        [Header("Framing")]
        [SerializeField, Min(1f)] float distance = 7.4f;
        [SerializeField, Min(0f)] float height = 2.3f;
        [SerializeField, Min(0f)] float lookHeight = 1.25f;
        [SerializeField, Min(0f)] float lookAhead = 3f;
        [Tooltip("Extra distance added at high speed.")]
        [SerializeField, Min(0f)] float speedDistance = 2.2f;
        [SerializeField, Min(1f)] float speedForMaxEffect = 30f;
        [SerializeField] float baseFov = 58f;
        [SerializeField] float speedFov = 10f;
        [Tooltip("How much of the vehicle's pitch the camera adopts (look up hills, down descents).")]
        [SerializeField, Range(0f, 1f)] float pitchFollow = 0.4f;

        [Header("Smoothing")]
        [SerializeField, Min(0.01f)] float yawSmoothTime = 0.28f;
        [SerializeField, Min(0.01f)] float pitchSmoothTime = 0.45f;
        [SerializeField, Min(0.01f)] float positionSmoothTime = 0.06f;

        [Header("Orbit")]
        [SerializeField, Min(0f)] float orbitSpeed = 140f;
        [SerializeField, Min(0f)] float orbitRecenterDelay = 1.6f;
        [SerializeField, Min(0.1f)] float orbitRecenterTime = 0.8f;

        [Header("Collision")]
        [SerializeField] LayerMask collisionMask = ~0;
        [SerializeField, Min(0.05f)] float collisionRadius = 0.35f;
        [SerializeField, Min(0f)] float groundClearance = 0.9f;

        [Header("Presets (C key)")]
        [Tooltip("Alternative (distance, height) framings cycled by the player. Index 0 is the default above.")]
        [SerializeField] Vector2[] presets = { new Vector2(10.5f, 3.4f), new Vector2(5.4f, 1.8f) };

        float yaw, yawVel, pitch, pitchVel;
        int presetIndex = -1;
        float defaultDistance = -1f, defaultHeight;
        float orbitYaw, orbitPitch, orbitIdle;
        float speedT;
        Vector3 smoothedPos, posVel;
        bool initialised;

        /// <summary>Cycles default framing -> preset 0 -> preset 1 -> ... -> default.</summary>
        public void CyclePreset()
        {
            if (defaultDistance < 0f) { defaultDistance = distance; defaultHeight = height; }
            presetIndex++;
            if (presetIndex >= presets.Length) presetIndex = -1;
            distance = presetIndex < 0 ? defaultDistance : presets[presetIndex].x;
            height = presetIndex < 0 ? defaultHeight : presets[presetIndex].y;
        }

        public void SetTarget(Transform t, Rigidbody body)
        {
            target = t;
            targetBody = body;
        }

        public override void Activate(in CameraPose current)
        {
            if (target == null) return;
            Vector3 f = target.forward;
            yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            pitch = 0f;
            yawVel = pitchVel = 0f;
            smoothedPos = current.position;
            posVel = Vector3.zero;
            initialised = true;
        }

        public override CameraPose Evaluate(float dt)
        {
            if (target == null) return new CameraPose(transform.position, transform.rotation, baseFov);
            if (!initialised) Activate(new CameraPose(transform.position, transform.rotation, baseFov));

            Vector3 velocity = targetBody != null ? targetBody.linearVelocity : Vector3.zero;
            Vector3 flatVel = new Vector3(velocity.x, 0f, velocity.z);
            Vector3 fwd = target.forward;
            Vector3 flatFwd = new Vector3(fwd.x, 0f, fwd.z);
            float forwardSpeed = Vector3.Dot(velocity, fwd);

            // Heading: follow velocity when driving forward quickly (shows where the car is going),
            // otherwise the chassis forward (so reversing does not spin the camera round).
            Vector3 headingDir = flatFwd;
            if (flatVel.magnitude > 4f && forwardSpeed > 2f) headingDir = Vector3.Slerp(flatFwd, flatVel.normalized, 0.6f);
            float targetYaw = Mathf.Atan2(headingDir.x, headingDir.z) * Mathf.Rad2Deg;
            yaw = Mathf.SmoothDampAngle(yaw, targetYaw, ref yawVel, yawSmoothTime, Mathf.Infinity, dt);

            float vehiclePitch = -Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg;
            pitch = Mathf.SmoothDamp(pitch, vehiclePitch * pitchFollow, ref pitchVel, pitchSmoothTime, Mathf.Infinity, dt);

            // Player orbit with automatic recentre.
            Vector2 look = input != null ? input.Look : Vector2.zero;
            if (look.sqrMagnitude > 0.0004f)
            {
                orbitYaw += look.x * orbitSpeed * dt;
                orbitPitch = Mathf.Clamp(orbitPitch - look.y * orbitSpeed * 0.5f * dt, -10f, 35f);
                orbitIdle = 0f;
            }
            else
            {
                orbitIdle += dt;
                if (orbitIdle > orbitRecenterDelay)
                {
                    float k = 1f - Mathf.Exp(-dt / orbitRecenterTime);
                    orbitYaw = Mathf.LerpAngle(orbitYaw, 0f, k);
                    orbitPitch = Mathf.Lerp(orbitPitch, 0f, k);
                }
            }

            speedT = Mathf.Lerp(speedT, Mathf.Clamp01(flatVel.magnitude / speedForMaxEffect), 1f - Mathf.Exp(-2f * dt));
            float dist = distance + speedDistance * speedT;
            Quaternion orbit = Quaternion.Euler(pitch + orbitPitch, yaw + orbitYaw, 0f);

            Vector3 pivot = target.position + Vector3.up * lookHeight;
            Vector3 desired = pivot + orbit * new Vector3(0f, height - lookHeight + 0.6f, -dist);

            // Collision: pull in toward the pivot if geometry is in the way.
            Vector3 toCam = desired - pivot;
            float len = toCam.magnitude;
            if (len > 0.01f && Physics.SphereCast(pivot, collisionRadius, toCam / len, out var hit, len, collisionMask, QueryTriggerInteraction.Ignore))
                desired = pivot + toCam / len * Mathf.Max(1.2f, hit.distance - 0.05f);

            // Terrain clearance.
            if (Physics.Raycast(desired + Vector3.up * 30f, Vector3.down, out var ground, 60f, collisionMask, QueryTriggerInteraction.Ignore))
                desired.y = Mathf.Max(desired.y, ground.point.y + groundClearance);

            smoothedPos = Vector3.SmoothDamp(smoothedPos, desired, ref posVel, positionSmoothTime, Mathf.Infinity, dt);
            if ((smoothedPos - desired).sqrMagnitude > 25f) smoothedPos = desired;

            Vector3 lookPoint = pivot + headingDir.normalized * (lookAhead * (0.4f + 0.6f * speedT));
            Quaternion rot = Quaternion.LookRotation(lookPoint - smoothedPos, Vector3.up);
            return new CameraPose(smoothedPos, rot, baseFov + speedFov * speedT);
        }
    }
}
