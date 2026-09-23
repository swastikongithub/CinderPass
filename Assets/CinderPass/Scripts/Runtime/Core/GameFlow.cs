using System;
using System.Collections;
using CinderPass.Cameras;
using CinderPass.Environment;
using CinderPass.Hazards;
using CinderPass.UI;
using CinderPass.Vehicle;
using UnityEngine;

namespace CinderPass.Core
{
    /// <summary>
    /// The game's explicit state machine.
    /// <list type="bullet">
    /// <item>Intro: the spline autopilot drives, the cinematic camera presents the level, player input is ignored.</item>
    /// <item>Playing: the player drives with full physics.</item>
    /// <item>Hazard: the vehicle touched lava; physics is frozen while the hazard effect plays.</item>
    /// <item>Respawning: screen fades, vehicle is restored to the last safe route position, control returns.</item>
    /// </list>
    /// Every system is referenced explicitly (wired by the scene assembler) - there are no singletons.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    public sealed class GameFlow : MonoBehaviour
    {
        [Header("Systems")]
        [SerializeField] InputHub input;
        [SerializeField] VehicleController vehicle;
        [SerializeField] SplineAutopilot autopilot;
        [SerializeField] PlayerVehicleInput playerInput;
        [SerializeField] VehicleHazardSensor hazardSensor;
        [SerializeField] RespawnSystem respawn;
        [SerializeField] HazardEffect hazardEffect;

        [Header("Presentation")]
        [SerializeField] CameraRig cameraRig;
        [SerializeField] IntroCameraDirector introCamera;
        [SerializeField] ChaseCamera chaseCamera;
        [SerializeField] OverviewCamera overviewCamera;
        [SerializeField] Hud hud;

        [Header("Flow")]
        [SerializeField] bool playIntro = true;
        [SerializeField, Min(0f)] float introToGameplayBlend = 2.4f;
        [SerializeField, Min(0f)] float hazardDuration = 1.5f;
        [SerializeField, Min(0f)] float hazardSinkDepth = 0.7f;
        [SerializeField, Min(0f)] float fadeOutTime = 0.45f;
        [SerializeField, Min(0f)] float fadeInTime = 0.7f;
        [SerializeField, Min(0f)] float settleTime = 0.35f;

        static readonly Color AutopilotColor = new Color(0.36f, 0.86f, 0.8f);
        static readonly Color ManualColor = new Color(0.95f, 0.66f, 0.3f);
        static readonly Color HazardColor = new Color(1f, 0.32f, 0.12f);

        int overviewIndex = -1;

        public GameState State { get; private set; } = GameState.Boot;
        public event Action<GameState, GameState> StateChanged;

        void Start()
        {
            hazardSensor.HazardEntered += OnHazard;
            autopilot.Completed += OnIntroFinished;
            if (playIntro) EnterIntro();
            else
            {
                autopilot.PlaceAtStart();
                EnterPlaying("Manual control", 6f);
                cameraRig.SetMode(chaseCamera, 0f);
            }
        }

        void OnDestroy()
        {
            if (hazardSensor != null) hazardSensor.HazardEntered -= OnHazard;
            if (autopilot != null) autopilot.Completed -= OnIntroFinished;
        }

        void Update()
        {
            switch (State)
            {
                case GameState.Intro:
                    if (input.SkipPressed) OnIntroFinished();
                    break;
                case GameState.Playing:
                    if (input.ResetPressed && overviewIndex < 0) StartCoroutine(RespawnSequence("Vehicle recovered to the track"));
                    if (input.OverviewPressed) CycleOverview();
                    if (input.CameraCyclePressed && overviewIndex < 0) chaseCamera.CyclePreset();
                    break;
            }
        }

        void SetState(GameState next)
        {
            if (next == State) return;
            var prev = State;
            State = next;
            StateChanged?.Invoke(prev, next);
        }

        void EnterIntro()
        {
            SetState(GameState.Intro);
            respawn.Tracking = false;
            autopilot.PlaceAtStart();
            autopilot.Begin();
            cameraRig.SetMode(introCamera, 0f);
            hud.SetDriveHudVisible(false);
            hud.SetMode("AUTOPILOT  ·  following route spline", AutopilotColor);
            hud.ShowTitle("CINDER PASS", "Survey Route 7  —  Pine Hollow to the Ember Basin", 6f);
            hud.ShowHint("Introduction  ·  press SPACE to take control early", 9f);
        }

        void OnIntroFinished()
        {
            if (State != GameState.Intro) return;
            autopilot.Stop();
            // Seamless handover: same rigidbody, same velocity, only the input source changes.
            respawn.ResetTracking(autopilot.CurrentRouteDistance);
            EnterPlaying("You have control  ·  W/S drive  ·  A/D steer  ·  SPACE handbrake  ·  R recover  ·  V viewpoints  ·  C camera", 9f);
            cameraRig.SetMode(chaseCamera, introToGameplayBlend);
        }

        void EnterPlaying(string hint, float hintSeconds)
        {
            vehicle.InputSource = playerInput;
            respawn.Tracking = true;
            hazardSensor.Armed = true;
            hud.SetDriveHudVisible(true);
            hud.SetMode("MANUAL CONTROL", ManualColor);
            if (!string.IsNullOrEmpty(hint)) hud.ShowHint(hint, hintSeconds);
            SetState(GameState.Playing);
        }

        void CycleOverview()
        {
            if (overviewCamera == null || overviewCamera.Count == 0) return;
            overviewIndex++;
            if (overviewIndex >= overviewCamera.Count)
            {
                overviewIndex = -1;
                vehicle.InputSource = playerInput;
                cameraRig.SetMode(chaseCamera, 1.5f);
                hud.SetDriveHudVisible(true);
                hud.SetMode("MANUAL CONTROL", ManualColor);
                return;
            }
            vehicle.InputSource = null; // hold the vehicle while presenting
            overviewCamera.Show(overviewIndex);
            if (cameraRig.Current != overviewCamera) cameraRig.SetMode(overviewCamera, 1.2f);
            hud.SetDriveHudVisible(false);
            hud.SetMode($"VIEWPOINT {overviewIndex + 1}/{overviewCamera.Count}  ·  {overviewCamera.CurrentLabel}  ·  V next", AutopilotColor);
        }

        void OnHazard(HazardZone zone)
        {
            if (State != GameState.Playing)
            {
                // The intro route never touches lava; ignore spurious contacts in other states.
                if (State == GameState.Intro) hazardSensor.Armed = true;
                return;
            }
            if (overviewIndex >= 0) CycleOverviewOff();
            StartCoroutine(HazardSequence(zone));
        }

        void CycleOverviewOff()
        {
            overviewIndex = -1;
            cameraRig.SetMode(chaseCamera, 0.5f);
        }

        IEnumerator HazardSequence(HazardZone zone)
        {
            SetState(GameState.Hazard);
            respawn.Tracking = false;
            vehicle.InputSource = null;
            Vector3 start = vehicle.Body.position;
            if (hazardEffect != null) hazardEffect.Play(start, zone);
            vehicle.SetFrozen(true);
            cameraRig.AddShake(0.55f);
            hud.SetMode("HAZARD", HazardColor);
            hud.SetDriveHudVisible(false);
            hud.HideHint();
            hud.ShowHazard($"{zone.DisplayName.ToUpperInvariant()}  —  VEHICLE LOST");

            // Sink into the molten surface while physics is suspended (deterministic, no solver fights).
            Quaternion startRot = vehicle.Body.rotation;
            Quaternion tilt = startRot * Quaternion.Euler(8f, 0f, 5f);
            float t = 0f;
            while (t < hazardDuration)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, t / hazardDuration);
                vehicle.Body.MovePosition(start + Vector3.down * hazardSinkDepth * k);
                vehicle.Body.MoveRotation(Quaternion.Slerp(startRot, tilt, k));
                yield return null;
            }
            yield return RespawnSequence("Recovered to the last safe point on the route");
        }

        IEnumerator RespawnSequence(string message)
        {
            SetState(GameState.Respawning);
            hazardSensor.Armed = false;
            respawn.Tracking = false;
            vehicle.InputSource = null;
            hud.FadeTo(1f, fadeOutTime);
            yield return new WaitForSeconds(fadeOutTime + 0.05f);

            vehicle.SetFrozen(true);
            respawn.RespawnNow();
            vehicle.SetFrozen(false);
            if (hazardEffect != null) hazardEffect.Stop();
            cameraRig.Snap(chaseCamera);
            hud.HideHazard();

            yield return new WaitForSeconds(settleTime);
            hud.FadeTo(0f, fadeInTime);
            overviewIndex = -1;
            EnterPlaying(message, 4f);
        }
    }
}
