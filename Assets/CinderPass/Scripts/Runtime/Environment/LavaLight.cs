using UnityEngine;

namespace CinderPass.Environment
{
    /// <summary>Slow, irregular breathing of a lava pool's bounce light (layered Perlin, no allocations).</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Light))]
    public sealed class LavaLight : MonoBehaviour
    {
        [SerializeField, Min(0f)] float baseIntensity = 6f;
        [SerializeField, Range(0f, 1f)] float flicker = 0.25f;
        [SerializeField, Min(0.01f)] float speed = 0.6f;

        Light lightSource;
        float seed;

        public void Configure(float intensity, float flickerAmount, float flickerSpeed)
        {
            baseIntensity = intensity;
            flicker = flickerAmount;
            speed = flickerSpeed;
        }

        void Awake()
        {
            lightSource = GetComponent<Light>();
            seed = (transform.position.x * 0.13f + transform.position.z * 0.07f) % 100f;
        }

        void Update()
        {
            float t = Time.time * speed;
            float n = Mathf.PerlinNoise(seed, t) * 0.7f + Mathf.PerlinNoise(seed + 31f, t * 3.1f) * 0.3f;
            lightSource.intensity = baseIntensity * (1f - flicker + flicker * 2f * n);
        }
    }
}
