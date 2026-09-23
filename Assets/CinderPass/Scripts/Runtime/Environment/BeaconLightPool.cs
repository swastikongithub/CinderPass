using System.Collections.Generic;
using UnityEngine;

namespace CinderPass.Environment
{
    /// <summary>
    /// Keeps real-time lighting cost fixed regardless of beacon count: a small pool of shadowless point
    /// lights is assigned to the beacons nearest the viewer, fading in/out as the assignment changes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BeaconLightPool : MonoBehaviour
    {
        [SerializeField] Transform viewer;
        [SerializeField, Range(1, 8)] int poolSize = 4;
        [SerializeField, Min(5f)] float maxDistance = 70f;
        [SerializeField, Min(0.02f)] float reassignInterval = 0.25f;
        [SerializeField, Min(0.1f)] float fadeSpeed = 3f;

        Light[] lights;
        GlowBeacon[] assigned;
        float[] fades;
        float timer;
        readonly List<GlowBeacon> candidates = new List<GlowBeacon>(64);

        public void SetViewer(Transform t) => viewer = t;

        void Awake()
        {
            lights = new Light[poolSize];
            assigned = new GlowBeacon[poolSize];
            fades = new float[poolSize];
            for (int i = 0; i < poolSize; i++)
            {
                var go = new GameObject($"BeaconLight_{i}");
                go.transform.SetParent(transform, false);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.shadows = LightShadows.None;
                l.renderMode = LightRenderMode.ForcePixel;
                l.intensity = 0f;
                l.enabled = false;
                lights[i] = l;
            }
        }

        void Update()
        {
            if (viewer == null) return;
            timer -= Time.deltaTime;
            if (timer <= 0f)
            {
                timer = reassignInterval;
                Reassign();
            }

            float t = Time.time;
            for (int i = 0; i < poolSize; i++)
            {
                var b = assigned[i];
                float target = b != null ? 1f : 0f;
                fades[i] = Mathf.MoveTowards(fades[i], target, fadeSpeed * Time.deltaTime);
                var l = lights[i];
                if (fades[i] <= 0.001f) { l.enabled = false; continue; }
                if (b == null) { l.intensity *= 0.9f; continue; }
                var style = b.Style;
                float d = Vector3.Distance(viewer.position, b.transform.position);
                float distanceFade = 1f - Mathf.Clamp01((d - maxDistance * 0.7f) / (maxDistance * 0.3f));
                l.enabled = true;
                l.transform.position = b.LightPosition;
                l.color = style.lightColor;
                l.range = style.lightRange;
                l.intensity = style.lightIntensity * style.EvaluatePulse(b.transform.position, t) * fades[i] * distanceFade;
            }
        }

        void Reassign()
        {
            candidates.Clear();
            Vector3 v = viewer.position;
            float maxSq = maxDistance * maxDistance;
            var all = GlowBeacon.Active;
            for (int i = 0; i < all.Count; i++)
                if ((all[i].transform.position - v).sqrMagnitude < maxSq) candidates.Add(all[i]);
            candidates.Sort((a, b) => (a.transform.position - v).sqrMagnitude.CompareTo((b.transform.position - v).sqrMagnitude));

            // Keep existing assignments that are still wanted (avoids popping), fill the rest.
            int wanted = Mathf.Min(poolSize, candidates.Count);
            for (int i = 0; i < poolSize; i++)
            {
                if (assigned[i] == null) continue;
                int idx = candidates.IndexOf(assigned[i]);
                if (idx < 0 || idx >= wanted) assigned[i] = null;
            }
            for (int c = 0; c < wanted; c++)
            {
                var b = candidates[c];
                if (System.Array.IndexOf(assigned, b) >= 0) continue;
                for (int i = 0; i < poolSize; i++)
                {
                    if (assigned[i] != null || fades[i] > 0.05f) continue;
                    assigned[i] = b;
                    break;
                }
            }
        }
    }
}
