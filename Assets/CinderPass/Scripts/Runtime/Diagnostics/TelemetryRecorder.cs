using System.IO;
using System.Text;
using CinderPass.Core;
using CinderPass.Vehicle;
using UnityEngine;

namespace CinderPass.Diagnostics
{
    /// <summary>
    /// Development tool: records vehicle/game-flow telemetry to a CSV (Temp/telemetry.csv) at a fixed rate,
    /// for diagnosing handling and control issues. Not present in the built scene; add it at runtime.
    /// </summary>
    public sealed class TelemetryRecorder : MonoBehaviour
    {
        [SerializeField, Min(0.02f)] float interval = 0.1f;
        public string path = "Temp/telemetry.csv";

        VehicleController vehicle;
        GameFlow flow;
        InputHub hub;
        SplineAutopilot autopilot;
        readonly StringBuilder sb = new StringBuilder();
        float timer;

        void Start()
        {
            vehicle = FindAnyObjectByType<VehicleController>();
            flow = FindAnyObjectByType<GameFlow>();
            hub = FindAnyObjectByType<InputHub>();
            autopilot = vehicle != null ? vehicle.GetComponent<SplineAutopilot>() : null;
            sb.AppendLine("time,state,source,apActive,hubThr,hubBrake,hubSteer,thr,brake,steer,steerAngle,kmh,gear,rpm,grounded,x,y,z,frozen");
        }

        void FixedUpdate()
        {
            if (vehicle == null) return;
            timer -= Time.fixedDeltaTime;
            if (timer > 0f) return;
            timer = interval;
            var i = vehicle.CurrentInput;
            var p = vehicle.transform.position;
            sb.Append(Time.time.ToString("0.00")).Append(',')
              .Append(flow != null ? flow.State.ToString() : "-").Append(',')
              .Append(vehicle.InputSource != null ? vehicle.InputSource.GetType().Name : "null").Append(',')
              .Append(autopilot != null && autopilot.Active).Append(',')
              .Append(hub != null ? hub.Throttle.ToString("0.00") : "-").Append(',')
              .Append(hub != null ? hub.Brake.ToString("0.00") : "-").Append(',')
              .Append(hub != null ? hub.Steer.ToString("0.00") : "-").Append(',')
              .Append(i.Throttle.ToString("0.00")).Append(',').Append(i.Brake.ToString("0.00")).Append(',').Append(i.Steer.ToString("0.00")).Append(',')
              .Append(vehicle.SteerAngle.ToString("0.0")).Append(',').Append(vehicle.SpeedKmh.ToString("0.0")).Append(',')
              .Append(vehicle.Drivetrain.InReverse ? "R" : vehicle.Drivetrain.Gear.ToString()).Append(',').Append(vehicle.Drivetrain.Rpm.ToString("0")).Append(',')
              .Append(vehicle.GroundedWheelCount).Append(',')
              .Append(p.x.ToString("0.0")).Append(',').Append(p.y.ToString("0.0")).Append(',').Append(p.z.ToString("0.0")).Append(',')
              .Append(vehicle.IsFrozen).AppendLine();
        }

        void OnDisable() => Flush();
        public void Flush() => File.WriteAllText(path, sb.ToString());
    }
}
