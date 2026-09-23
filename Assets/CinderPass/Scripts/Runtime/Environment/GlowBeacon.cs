using System.Collections.Generic;
using UnityEngine;

namespace CinderPass.Environment
{
    /// <summary>
    /// Marker component on the RouteBeacon prefab root. Beacons carry no per-frame logic: the glow is
    /// animated in the shared shader and real-time light is lent to the nearest few by <see cref="BeaconLightPool"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GlowBeacon : MonoBehaviour
    {
        static readonly List<GlowBeacon> active = new List<GlowBeacon>();

        [SerializeField] BeaconStyle style;
        [Tooltip("Where a pooled light is placed when this beacon is lit.")]
        [SerializeField] Transform lightAnchor;

        public static IReadOnlyList<GlowBeacon> Active => active;
        public BeaconStyle Style => style;
        public Vector3 LightPosition => lightAnchor != null ? lightAnchor.position : transform.position + Vector3.up * 2f;

        void OnEnable() => active.Add(this);
        void OnDisable() => active.Remove(this);
    }
}
