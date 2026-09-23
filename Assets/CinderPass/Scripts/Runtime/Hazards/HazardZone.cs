using UnityEngine;

namespace CinderPass.Hazards
{
    public enum HazardKind { Lava, OutOfBounds }

    /// <summary>
    /// A trigger volume that is lethal to the vehicle (e.g. an active lava surface).
    /// The zone only reports; the game flow decides how to respond.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HazardZone : MonoBehaviour
    {
        [SerializeField] HazardKind kind = HazardKind.Lava;
        [SerializeField] string displayName = "Lava";
        [Tooltip("World-space height of the dangerous surface (used for the sink effect).")]
        [SerializeField] float surfaceHeight;

        public HazardKind Kind => kind;
        public string DisplayName => displayName;
        public float SurfaceHeight => surfaceHeight;

        public void Configure(HazardKind k, string label, float surfaceY)
        {
            kind = k;
            displayName = label;
            surfaceHeight = surfaceY;
        }

        void Reset() => EnsureTrigger();
        void Awake() => EnsureTrigger();

        void EnsureTrigger()
        {
            foreach (var c in GetComponents<Collider>()) c.isTrigger = true;
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.35f, 0.05f, 0.35f);
            foreach (var c in GetComponents<Collider>())
                Gizmos.DrawWireCube(c.bounds.center, c.bounds.size);
        }
#endif
    }
}
