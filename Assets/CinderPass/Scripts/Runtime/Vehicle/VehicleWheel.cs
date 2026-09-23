using System;
using CinderPass.Environment;
using UnityEngine;

namespace CinderPass.Vehicle
{
    public enum Axle { Front, Rear }

    /// <summary>One corner of the vehicle: physics collider, visual, and per-step contact state.</summary>
    [Serializable]
    public sealed class VehicleWheel
    {
        public WheelCollider collider;
        [Tooltip("Visual wheel (tyre + rim). Must not be a child of the WheelCollider.")]
        public Transform visual;
        public Axle axle;
        public bool isLeft;
        public bool driven = true;

        [NonSerialized] public WheelHit hit;
        [NonSerialized] public bool grounded;
        /// <summary>0 = fully extended, 1 = fully compressed.</summary>
        [NonSerialized] public float compression;
        [NonSerialized] public SurfaceInfo surface;
        [NonSerialized] public float tractionFactor = 1f;
        [NonSerialized] internal Vector3 localPosition;
        [NonSerialized] internal Quaternion localRotation = Quaternion.identity;
        [NonSerialized] internal float appliedGrip = -1f;

        public bool IsFront => axle == Axle.Front;
    }
}
