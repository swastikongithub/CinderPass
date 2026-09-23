using CinderPass.Route;
using CinderPass.Vehicle;
using UnityEngine;

namespace CinderPass.Hazards
{
    /// <summary>
    /// Continuously records the last point on the route where the vehicle was demonstrably safe
    /// (upright, on its wheels, close to the track) and restores the vehicle to the route behind it.
    /// Because recovery points come from the route spline, they are always on the drivable track and
    /// never inside a hazard (the route is validated to keep clear of lava).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RespawnSystem : MonoBehaviour
    {
        [SerializeField] VehicleController vehicle;
        [SerializeField] RouteTrack route;
        [SerializeField, Min(0.05f)] float sampleInterval = 0.25f;
        [Tooltip("Maximum distance from the centre line for a position to count as safe.")]
        [SerializeField, Min(1f)] float maxSafeOffset = 9f;
        [Tooltip("How far back along the route (metres) from the last safe point to respawn.")]
        [SerializeField, Min(0f)] float backoff = 12f;
        [SerializeField, Min(0f)] float spawnHeight = 0.6f;
        [Tooltip("Layers considered ground when settling the respawn point (exclude the vehicle).")]
        [SerializeField] LayerMask groundMask = ~0;

        int hint = -1;
        float timer;
        bool hasSafe;

        public float LastSafeDistance { get; private set; }
        public bool Tracking { get; set; } = true;

        public void ResetTracking(float routeDistance)
        {
            LastSafeDistance = routeDistance;
            hasSafe = true;
            hint = -1;
        }

        void FixedUpdate()
        {
            if (!Tracking || vehicle == null || route == null || vehicle.IsFrozen) return;
            timer -= Time.fixedDeltaTime;
            if (timer > 0f) return;
            timer = sampleInterval;

            Vector3 p = vehicle.Body.position;
            float s = route.FindNearest(p, ref hint, 60);
            bool upright = Vector3.Dot(vehicle.transform.up, Vector3.up) > 0.8f;
            bool onWheels = vehicle.GroundedWheelCount >= 3;
            bool nearTrack = Mathf.Abs(route.LateralOffset(p, s)) < maxSafeOffset;
            if (upright && onWheels && nearTrack)
            {
                LastSafeDistance = s;
                hasSafe = true;
            }
        }

        /// <summary>Route distance the vehicle will be restored to.</summary>
        public float ResolveRespawnDistance()
        {
            if (!hasSafe)
            {
                int h = -1;
                LastSafeDistance = route.FindNearest(vehicle.Body.position, ref h, 0);
            }
            return route.Wrap(LastSafeDistance - backoff);
        }

        /// <summary>Places the vehicle on the route, aligned to the direction of travel, at rest.</summary>
        public void RespawnNow()
        {
            float s = ResolveRespawnDistance();
            route.SampleFrame(s, out var p, out var fwd, out _);
            Vector3 ground = p;
            if (Physics.Raycast(p + Vector3.up * 20f, Vector3.down, out var hit, 60f, groundMask, QueryTriggerInteraction.Ignore))
                ground = hit.point;
            vehicle.Teleport(ground + Vector3.up * spawnHeight, Quaternion.LookRotation(fwd, Vector3.up));
            ResetTracking(s);
        }
    }
}
