using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CinderPass.EditorTools
{
    /// <summary>
    /// Renders arbitrary views (or the presentation viewpoints) through a camera configured like the game
    /// camera and writes PNGs - used for art review and documentation screenshots.
    /// </summary>
    public static class ViewCapture
    {
        public static string Capture(Vector3 position, Vector3 lookAt, float fov, string path, int width = 1600, int height = 900)
        {
            var go = new GameObject("CaptureCamera") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            var main = Camera.main;
            if (main != null) cam.CopyFrom(main);
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.25f;
            cam.farClipPlane = 3200f;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(lookAt - position, Vector3.up));
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, tex.EncodeToJPG(90));
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(go);
            return path;
        }

        /// <summary>
        /// Fixed art-review set: chase-height shots along the route (the player's actual view of every
        /// region), a vehicle close-up and the presentation viewpoints. Captured with a tag so passes can be
        /// compared side by side (Screenshots/Review/&lt;tag&gt;_*.jpg).
        /// </summary>
        public static int CaptureReviewSet(string tag, int width = 1280, int height = 720)
        {
            var track = Object.FindAnyObjectByType<CinderPass.Route.RouteTrack>();
            if (track == null) { Debug.LogWarning("No route in the open scene."); return 0; }
            int n = 0;
            string Shot(string name) => $"Screenshots/Review/{tag}_{n++:00}_{name}.jpg";

            var stops = new (string name, float fraction)[]
            {
                ("forest", 0.04f), ("forest_climb", 0.17f), ("highlands", 0.32f), ("canyon", 0.47f),
                ("overlook", 0.58f), ("basin_road", 0.72f), ("basin_lava", 0.84f), ("volcano_flank", 0.95f),
            };
            foreach (var (name, fraction) in stops)
            {
                float d = track.Length * fraction;
                track.SampleFrame(d, out var p, out var fwd, out _);
                Vector3 eye = p - fwd * 9f + Vector3.up * 3.2f;
                Capture(eye, track.PositionAt(d + 30f) + Vector3.up * 1.2f, 60f, Shot(name), width, height);
            }

            var vehicle = Object.FindAnyObjectByType<CinderPass.Vehicle.VehicleController>();
            if (vehicle != null)
            {
                var t = vehicle.transform;
                Vector3 focus = t.position + Vector3.up * 0.8f;
                Capture(focus - t.forward * 5.5f + t.right * 3.2f + Vector3.up * 1.4f, focus, 45f, Shot("vehicle_rear"), width, height);
                Capture(focus + t.forward * 5f - t.right * 3.4f + Vector3.up * 0.9f, focus, 45f, Shot("vehicle_front"), width, height);
            }

            var root = GameObject.Find("GameWorld/Cameras/Viewpoints");
            if (root != null)
                foreach (Transform vp in root.transform)
                    Capture(vp.position, vp.position + vp.forward * 100f, 52f, Shot(vp.name.Replace("VP_", "vp")), width, height);
            Debug.Log($"[ViewCapture] Review set '{tag}': {n} shots in Screenshots/Review/");
            return n;
        }

        [MenuItem("Cinder Pass/Presentation/Capture All Viewpoints", priority = 40)]
        public static void CaptureViewpoints()
        {
            var root = GameObject.Find("GameWorld/Cameras/Viewpoints");
            if (root == null) { Debug.LogWarning("No viewpoints in the open scene."); return; }
            int i = 0;
            foreach (Transform vp in root.transform)
            {
                string path = $"Screenshots/{vp.name}.jpg";
                Capture(vp.position, vp.position + vp.forward * 100f, 52f, path);
                i++;
            }
            Debug.Log($"[ViewCapture] Captured {i} viewpoints into Screenshots/");
        }
    }
}
