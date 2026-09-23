using CinderPass.Core;
using CinderPass.Route;
using CinderPass.Vehicle;
using Unity.Profiling;
using UnityEngine;

namespace CinderPass.Diagnostics
{
    /// <summary>
    /// Development overlay (F1): frame timing, rendering counters, vehicle telemetry and route tracking.
    /// Hidden by default so it never appears in the presentation.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DebugOverlay : MonoBehaviour
    {
        [SerializeField] InputHub input;
        [SerializeField] GameFlow flow;
        [SerializeField] VehicleController vehicle;
        [SerializeField] SplineAutopilot autopilot;
        [SerializeField] RouteTrack route;
        [SerializeField] bool visible;

        ProfilerRecorder batches, setPass, triangles, drawCalls, mainThread;
        float fpsSmoothed;
        GUIStyle style;
        int hint = -1;
        readonly System.Text.StringBuilder sb = new System.Text.StringBuilder(1024);

        void OnEnable()
        {
            batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 15);
        }

        void OnDisable()
        {
            batches.Dispose();
            setPass.Dispose();
            triangles.Dispose();
            drawCalls.Dispose();
            mainThread.Dispose();
        }

        void Update()
        {
            if (input != null && input.DebugTogglePressed) visible = !visible;
            float fps = 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
            fpsSmoothed = Mathf.Lerp(fpsSmoothed <= 0f ? fps : fpsSmoothed, fps, 0.05f);
        }

        void OnGUI()
        {
            if (!visible) return;
            style ??= new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true };
            sb.Clear();
            sb.Append("<b>CINDER PASS — diagnostics (F1)</b>\n");
            sb.AppendFormat("FPS {0:0}   frame {1:0.0} ms   main {2:0.0} ms\n", fpsSmoothed, 1000f / Mathf.Max(fpsSmoothed, 1f), mainThread.Valid ? Average(mainThread) * 1e-6f : 0f);
            sb.AppendFormat("Batches {0}   SetPass {1}   Draws {2}   Tris {3:0.00}M\n", batches.LastValue, setPass.LastValue, drawCalls.LastValue, triangles.LastValue / 1e6f);
            if (flow != null) sb.AppendFormat("State: {0}\n", flow.State);
            if (vehicle != null)
            {
                sb.AppendFormat("Speed {0:0.0} km/h   RPM {1:0}   Gear {2}{3}   Grounded {4}/4   Steer {5:0.0}°\n",
                    vehicle.SpeedKmh, vehicle.Drivetrain.Rpm, vehicle.Drivetrain.InReverse ? "R" : vehicle.Drivetrain.Gear.ToString(),
                    vehicle.Drivetrain.IsShifting ? " (shift)" : "", vehicle.GroundedWheelCount, vehicle.SteerAngle);
                foreach (var w in vehicle.Wheels)
                    sb.AppendFormat("  {0}{1}: {2}  comp {3:0.00}  slip f{4:0.00} s{5:0.00}  {6}\n", w.IsFront ? "F" : "R", w.isLeft ? "L" : "R",
                        w.grounded ? "contact" : "air    ", w.compression, w.hit.forwardSlip, w.hit.sidewaysSlip, w.surface != null ? w.surface.name : "-");
            }
            if (route != null && vehicle != null)
            {
                float s = route.FindNearest(vehicle.Body.position, ref hint, 60);
                sb.AppendFormat("Route {0:0}/{1:0} m   offset {2:0.00} m\n", s, route.Length, route.LateralOffset(vehicle.Body.position, s));
            }
            if (autopilot != null && autopilot.Active)
                sb.AppendFormat("Autopilot: travelled {0:0} m  target {1:0.0} m/s  cross-track {2:0.00} m\n", autopilot.Travelled, autopilot.TargetSpeed, autopilot.CrossTrackError);

            var rect = new Rect(12, 12, 560, 260);
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(rect.x + 8, rect.y + 6, rect.width - 16, rect.height - 12), sb.ToString(), style);
        }

        readonly System.Collections.Generic.List<ProfilerRecorderSample> samples = new System.Collections.Generic.List<ProfilerRecorderSample>(16);

        double Average(ProfilerRecorder r)
        {
            r.CopyTo(samples);
            if (samples.Count == 0) return 0;
            double sum = 0;
            foreach (var s in samples) sum += s.Value;
            return sum / samples.Count;
        }
    }
}
