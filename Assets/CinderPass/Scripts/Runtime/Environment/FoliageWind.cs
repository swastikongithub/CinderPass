using UnityEngine;

namespace CinderPass.Environment
{
    /// <summary>Publishes global wind parameters consumed by the foliage shader (trees and grass sway in unison).</summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class FoliageWind : MonoBehaviour
    {
        static readonly int WindId = Shader.PropertyToID("_CP_Wind");
        static readonly int WindDirId = Shader.PropertyToID("_CP_WindDirection");

        [SerializeField, Min(0f)] float strength = 0.35f;
        [SerializeField, Min(0f)] float speed = 1.1f;
        [Tooltip("World-space scale of gust waves (metres).")]
        [SerializeField, Min(1f)] float gustScale = 60f;
        [SerializeField, Range(0f, 1f)] float gustStrength = 0.6f;

        void OnEnable() => Publish();
        void OnValidate() => Publish();
        void Update()
        {
            if (transform.hasChanged) { Publish(); transform.hasChanged = false; }
        }

        void Publish()
        {
            Vector3 d = transform.forward;
            d.y = 0f;
            d = d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.forward;
            Shader.SetGlobalVector(WindId, new Vector4(strength, speed, 1f / gustScale, gustStrength));
            Shader.SetGlobalVector(WindDirId, new Vector4(d.x, 0f, d.z, 0f));
        }
    }
}
