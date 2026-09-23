using System;
using UnityEngine;

namespace CinderPass.Hazards
{
    /// <summary>
    /// Lives on the vehicle's rigidbody object. Unity forwards trigger messages from every child collider
    /// of a rigidbody to this object, so any part of the car touching a <see cref="HazardZone"/> is reported.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class VehicleHazardSensor : MonoBehaviour
    {
        public event Action<HazardZone> HazardEntered;

        /// <summary>When false, contacts are ignored (e.g. while already respawning).</summary>
        public bool Armed { get; set; } = true;

        void OnTriggerEnter(Collider other) => Check(other);
        void OnTriggerStay(Collider other) => Check(other);

        void Check(Collider other)
        {
            if (!Armed) return;
            var zone = other.GetComponentInParent<HazardZone>();
            if (zone == null) return;
            Armed = false;
            HazardEntered?.Invoke(zone);
        }
    }
}
