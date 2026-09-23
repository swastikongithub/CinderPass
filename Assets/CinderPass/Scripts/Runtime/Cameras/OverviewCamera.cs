using System;
using UnityEngine;

namespace CinderPass.Cameras
{
    /// <summary>
    /// Presentation viewpoints (aerial overview, landmarks). Each viewpoint slowly dollies along its
    /// own forward/right axes so screenshots and demos feel alive rather than static.
    /// </summary>
    public sealed class OverviewCamera : CameraMode
    {
        [Serializable]
        public sealed class Viewpoint
        {
            public string label;
            public Transform anchor;
            [Range(10f, 90f)] public float fieldOfView = 50f;
            [Tooltip("Dolly velocity in the anchor's local space (m/s).")]
            public Vector3 drift = new Vector3(0.6f, 0f, 0.4f);
            [Tooltip("Seconds before the drift ping-pongs back.")]
            [Min(1f)] public float driftPeriod = 40f;
        }

        [SerializeField] Viewpoint[] viewpoints = Array.Empty<Viewpoint>();
        int index;
        float time;

        public int Count => viewpoints.Length;
        public int Index => index;
        public string CurrentLabel => viewpoints.Length > 0 ? viewpoints[index].label : string.Empty;
        public void SetViewpoints(Viewpoint[] value) => viewpoints = value;

        public void Show(int i)
        {
            index = Mathf.Clamp(i, 0, Mathf.Max(0, viewpoints.Length - 1));
            time = 0f;
        }

        public override CameraPose Evaluate(float dt)
        {
            if (viewpoints.Length == 0 || viewpoints[index].anchor == null)
                return new CameraPose(transform.position, transform.rotation, 50f);
            time += dt;
            var vp = viewpoints[index];
            float phase = Mathf.PingPong(time, vp.driftPeriod) - vp.driftPeriod * 0.5f;
            Vector3 offset = vp.anchor.rotation * (vp.drift * phase);
            return new CameraPose(vp.anchor.position + offset, vp.anchor.rotation, vp.fieldOfView);
        }
    }
}
