using UnityEngine;

namespace CinderPass.Environment
{
    /// <summary>
    /// Ambient particles that follow the viewer but only emit inside a region (e.g. ash fall in the
    /// volcanic basin), fading smoothly across the region boundary.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class RegionalEmission : MonoBehaviour
    {
        [SerializeField] Transform follow;
        [SerializeField] Vector3 followOffset = new Vector3(0f, 14f, 0f);
        [SerializeField] Vector3 regionCentre;
        [SerializeField, Min(0f)] float innerRadius = 220f;
        [SerializeField, Min(1f)] float outerRadius = 340f;
        [SerializeField, Min(0f)] float maxRate = 70f;

        ParticleSystem system;
        ParticleSystem.EmissionModule emission;

        public void Configure(Transform followTarget, Vector3 centre, float inner, float outer, float rate)
        {
            follow = followTarget;
            regionCentre = centre;
            innerRadius = inner;
            outerRadius = outer;
            maxRate = rate;
        }

        void Awake()
        {
            system = GetComponent<ParticleSystem>();
            emission = system.emission;
        }

        void LateUpdate()
        {
            if (follow == null) return;
            transform.position = follow.position + followOffset;
            Vector3 d = follow.position - regionCentre;
            d.y = 0f;
            float k = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(innerRadius, outerRadius, d.magnitude));
            emission.rateOverTime = maxRate * k;
        }
    }
}
