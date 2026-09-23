using System;
using CinderPass.Vehicle;
using UnityEngine;

namespace CinderPass.Cameras
{
    /// <summary>
    /// Cinematic camera for the introduction. Shots are keyed to the autopilot's progress along the
    /// route, so the edit always matches what the vehicle is doing regardless of frame rate.
    /// </summary>
    public sealed class IntroCameraDirector : CameraMode
    {
        public enum ShotType
        {
            /// <summary>Follows the vehicle at a heading-relative offset.</summary>
            Tracking,
            /// <summary>Fixed crane position (with slow drift) that pans to keep the vehicle framed.</summary>
            Anchored,
            /// <summary>Slowly orbits the vehicle.</summary>
            Orbit
        }

        [Serializable]
        public sealed class Shot
        {
            public string label;
            [Tooltip("Route distance travelled by the autopilot at which this shot starts.")]
            [Min(0f)] public float startTravelled;
            public ShotType type;
            [Tooltip("Tracking/Orbit: offset in the vehicle's heading frame (x right, y up, z forward).")]
            public Vector3 offset = new Vector3(0f, 2.5f, -8f);
            [Tooltip("Anchored: world-space crane position.")]
            public Vector3 anchorPosition;
            [Tooltip("Anchored: drift velocity of the crane (m/s) for a subtle dolly.")]
            public Vector3 anchorDrift;
            [Tooltip("Orbit: degrees per second.")]
            public float orbitSpeed = 12f;
            public float lookHeight = 1.2f;
            [Range(10f, 90f)] public float fieldOfView = 55f;
            [Tooltip("Seconds to blend into this shot (0 = hard cut).")]
            [Min(0f)] public float blendIn = 1.2f;
        }

        [SerializeField] SplineAutopilot autopilot;
        [SerializeField] Transform target;
        [SerializeField] Rigidbody targetBody;
        [SerializeField] Shot[] shots = Array.Empty<Shot>();
        [SerializeField, Min(0.01f)] float lookSmoothTime = 0.25f;
        [SerializeField, Min(0.01f)] float headingSmoothTime = 0.6f;
        [SerializeField] LayerMask groundMask = ~0;

        int shotIndex = -1;
        float shotTime;
        float blendTimer, blendDuration;
        CameraPose blendFrom, lastPose;
        Vector3 lookPoint, lookVel;
        float heading, headingVel;

        public Shot[] Shots => shots;
        public void SetShots(Shot[] value) => shots = value;

        public override void Activate(in CameraPose current)
        {
            shotIndex = -1;
            lastPose = current;
            if (target != null)
            {
                lookPoint = target.position + Vector3.up;
                heading = Mathf.Atan2(target.forward.x, target.forward.z) * Mathf.Rad2Deg;
            }
        }

        public override CameraPose Evaluate(float dt)
        {
            if (target == null || shots.Length == 0) return lastPose;
            float travelled = autopilot != null ? autopilot.Travelled : 0f;
            int index = 0;
            for (int i = 0; i < shots.Length; i++) if (travelled >= shots[i].startTravelled) index = i;
            if (index != shotIndex)
            {
                blendFrom = lastPose;
                blendDuration = shots[index].blendIn;
                blendTimer = 0f;
                shotTime = 0f;
                shotIndex = index;
            }
            shotTime += dt;
            var shot = shots[shotIndex];

            Vector3 vel = targetBody != null ? targetBody.linearVelocity : target.forward;
            Vector3 flat = new Vector3(vel.x, 0f, vel.z);
            if (flat.sqrMagnitude < 1f) flat = new Vector3(target.forward.x, 0f, target.forward.z);
            heading = Mathf.SmoothDampAngle(heading, Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg, ref headingVel, headingSmoothTime, Mathf.Infinity, dt);
            Quaternion frame = Quaternion.Euler(0f, heading, 0f);

            Vector3 desiredLook = target.position + Vector3.up * shot.lookHeight;
            lookPoint = Vector3.SmoothDamp(lookPoint, desiredLook, ref lookVel, lookSmoothTime, Mathf.Infinity, dt);
            if ((lookPoint - desiredLook).sqrMagnitude > 100f) lookPoint = desiredLook;

            Vector3 pos;
            switch (shot.type)
            {
                case ShotType.Anchored:
                    pos = shot.anchorPosition + shot.anchorDrift * shotTime;
                    break;
                case ShotType.Orbit:
                    pos = target.position + Quaternion.Euler(0f, heading + shot.orbitSpeed * shotTime, 0f) * shot.offset;
                    break;
                default:
                    pos = target.position + frame * shot.offset;
                    break;
            }
            if (Physics.Raycast(pos + Vector3.up * 40f, Vector3.down, out var hit, 80f, groundMask, QueryTriggerInteraction.Ignore))
                pos.y = Mathf.Max(pos.y, hit.point.y + 1f);

            var pose = new CameraPose(pos, Quaternion.LookRotation(lookPoint - pos, Vector3.up), shot.fieldOfView);
            if (blendTimer < blendDuration)
            {
                blendTimer += dt;
                pose = CameraPose.Lerp(blendFrom, pose, Mathf.SmoothStep(0f, 1f, blendTimer / blendDuration));
            }
            lastPose = pose;
            return pose;
        }
    }
}
