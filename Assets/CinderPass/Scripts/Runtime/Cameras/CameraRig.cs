using UnityEngine;

namespace CinderPass.Cameras
{
    public struct CameraPose
    {
        public Vector3 position;
        public Quaternion rotation;
        public float fieldOfView;

        public CameraPose(Vector3 p, Quaternion r, float fov)
        {
            position = p;
            rotation = r;
            fieldOfView = fov;
        }

        public static CameraPose Lerp(in CameraPose a, in CameraPose b, float t) => new CameraPose(
            Vector3.LerpUnclamped(a.position, b.position, t),
            Quaternion.SlerpUnclamped(a.rotation, b.rotation, t),
            Mathf.LerpUnclamped(a.fieldOfView, b.fieldOfView, t));
    }

    /// <summary>A camera behaviour that produces a pose each frame. Only the <see cref="CameraRig"/> moves the camera.</summary>
    public abstract class CameraMode : MonoBehaviour
    {
        /// <summary>Called when the rig switches to this mode; <paramref name="current"/> is the camera's pose at that moment.</summary>
        public virtual void Activate(in CameraPose current) { }
        public abstract CameraPose Evaluate(float deltaTime);
    }

    /// <summary>
    /// Drives the single gameplay camera. Holds the active <see cref="CameraMode"/>, blends between modes,
    /// and layers procedural shake on top.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(100)]
    public sealed class CameraRig : MonoBehaviour
    {
        [SerializeField] CameraMode initialMode;
        [SerializeField, Min(0f)] float shakeFrequency = 18f;
        [SerializeField, Min(0f)] float shakeDecay = 2.8f;
        [SerializeField, Min(0f)] float maxShakeAngle = 1.6f;

        Camera cam;
        CameraMode current;
        CameraMode previous;
        CameraPose lastPose;
        CameraPose frozenFrom;
        float blendTime;
        float blendDuration;
        float shake;
        float shakeSeed;

        public CameraMode Current => current;
        public Camera Camera => cam;

        void Awake()
        {
            cam = GetComponent<Camera>();
            lastPose = new CameraPose(transform.position, transform.rotation, cam.fieldOfView);
            shakeSeed = Random.value * 100f;
            if (initialMode != null) SetMode(initialMode, 0f);
        }

        public void SetMode(CameraMode mode, float blendSeconds)
        {
            if (mode == null || mode == current) return;
            previous = current;
            frozenFrom = lastPose;
            current = mode;
            current.Activate(lastPose);
            blendDuration = Mathf.Max(0f, blendSeconds);
            blendTime = 0f;
        }

        /// <summary>Activates a mode immediately with no blend (re-activating it if already current).</summary>
        public void Snap(CameraMode mode)
        {
            if (mode == null) return;
            previous = null;
            current = mode;
            current.Activate(lastPose);
            blendDuration = 0f;
            blendTime = 0f;
        }

        /// <summary>Adds trauma (0..1) that decays over time.</summary>
        public void AddShake(float amount) => shake = Mathf.Clamp01(shake + amount);

        void LateUpdate()
        {
            if (current == null) return;
            float dt = Time.deltaTime;
            CameraPose pose = current.Evaluate(dt);

            if (blendTime < blendDuration)
            {
                blendTime += dt;
                float w = Mathf.SmoothStep(0f, 1f, blendTime / blendDuration);
                // Keep evaluating the outgoing mode so the blend tracks a moving target instead of a stale pose.
                CameraPose from = previous != null && previous.isActiveAndEnabled ? previous.Evaluate(dt) : frozenFrom;
                pose = CameraPose.Lerp(from, pose, w);
            }

            lastPose = pose;
            Quaternion rot = pose.rotation;
            if (shake > 0.001f)
            {
                float t = Time.time * shakeFrequency;
                float s = shake * shake * maxShakeAngle;
                rot *= Quaternion.Euler(
                    (Mathf.PerlinNoise(shakeSeed, t) - 0.5f) * 2f * s,
                    (Mathf.PerlinNoise(shakeSeed + 7f, t) - 0.5f) * 2f * s,
                    (Mathf.PerlinNoise(shakeSeed + 13f, t) - 0.5f) * 2f * s * 0.5f);
                shake = Mathf.Max(0f, shake - shakeDecay * dt);
            }
            transform.SetPositionAndRotation(pose.position, rot);
            cam.fieldOfView = pose.fieldOfView;
        }
    }
}
