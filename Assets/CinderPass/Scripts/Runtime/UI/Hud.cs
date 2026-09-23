using CinderPass.Vehicle;
using UnityEngine;
using UnityEngine.UI;

namespace CinderPass.UI
{
    /// <summary>
    /// Minimal, presentation-quality HUD: speed/gear, a mode chip (autopilot vs manual), transient
    /// hints, the hazard warning, the intro title card and a full-screen fade used for respawns.
    /// All transitions are alpha tweens on CanvasGroups driven from one Update (no coroutines, no allocs).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Hud : MonoBehaviour
    {
        [SerializeField] VehicleController vehicle;

        [Header("Drive readout")]
        [SerializeField] CanvasGroup driveGroup;
        [SerializeField] Text speedText;
        [SerializeField] Text gearText;

        [Header("Mode chip")]
        [SerializeField] CanvasGroup modeGroup;
        [SerializeField] Text modeText;
        [SerializeField] Image modeAccent;

        [Header("Messages")]
        [SerializeField] CanvasGroup titleGroup;
        [SerializeField] Text titleText;
        [SerializeField] Text subtitleText;
        [SerializeField] CanvasGroup hintGroup;
        [SerializeField] Text hintText;
        [SerializeField] CanvasGroup hazardGroup;
        [SerializeField] Text hazardText;
        [SerializeField] CanvasGroup fadeGroup;

        [SerializeField, Min(0.1f)] float messageFadeSpeed = 2.5f;

        float driveTarget, modeTarget = 1f, titleTarget, hintTarget, hazardTarget, fadeTarget, fadeSpeed = 2f;
        float titleTimer, hintTimer;
        int lastSpeed = -1;
        string lastGear;

        // Readout strings are cached so the per-frame update never allocates.
        static readonly string[] GearLabels = { "N", "1", "2", "3", "4", "5", "6", "7", "8", "9" };
        static readonly string[] SpeedLabels = new string[400];
        static string SpeedLabel(int kmh) => SpeedLabels[kmh] ?? (SpeedLabels[kmh] = kmh.ToString());

        public void SetDriveHudVisible(bool visible) => driveTarget = visible ? 1f : 0f;
        public void SetModeChipVisible(bool visible) => modeTarget = visible ? 1f : 0f;

        public void SetMode(string label, Color accent)
        {
            if (modeText != null) modeText.text = label;
            if (modeAccent != null) modeAccent.color = accent;
        }

        public void ShowTitle(string title, string subtitle, float seconds)
        {
            if (titleText != null) titleText.text = title;
            if (subtitleText != null) subtitleText.text = subtitle;
            titleTarget = 1f;
            titleTimer = seconds;
        }

        public void ShowHint(string text, float seconds)
        {
            if (hintText != null) hintText.text = text;
            hintTarget = 1f;
            hintTimer = seconds;
        }

        public void HideHint()
        {
            hintTarget = 0f;
            hintTimer = 0f;
        }

        public void ShowHazard(string text)
        {
            if (hazardText != null) hazardText.text = text;
            hazardTarget = 1f;
        }

        public void HideHazard() => hazardTarget = 0f;

        /// <summary>Fades the screen to <paramref name="alpha"/> over <paramref name="seconds"/>.</summary>
        public void FadeTo(float alpha, float seconds)
        {
            fadeTarget = alpha;
            fadeSpeed = seconds <= 0f ? 1000f : 1f / seconds;
            if (seconds <= 0f && fadeGroup != null) fadeGroup.alpha = alpha;
        }

        void Awake()
        {
            SetAlpha(driveGroup, 0f);
            SetAlpha(titleGroup, 0f);
            SetAlpha(hintGroup, 0f);
            SetAlpha(hazardGroup, 0f);
            SetAlpha(fadeGroup, 0f);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (titleTimer > 0f && (titleTimer -= dt) <= 0f) titleTarget = 0f;
            if (hintTimer > 0f && (hintTimer -= dt) <= 0f) hintTarget = 0f;

            Tween(driveGroup, driveTarget, messageFadeSpeed, dt);
            Tween(modeGroup, modeTarget, messageFadeSpeed, dt);
            Tween(titleGroup, titleTarget, 1.2f, dt);
            Tween(hintGroup, hintTarget, messageFadeSpeed, dt);
            Tween(hazardGroup, hazardTarget, 6f, dt);
            Tween(fadeGroup, fadeTarget, fadeSpeed, dt);

            if (vehicle != null && driveGroup != null && driveGroup.alpha > 0f)
            {
                int kmh = Mathf.Clamp(Mathf.RoundToInt(vehicle.SpeedKmh), 0, SpeedLabels.Length - 1);
                if (kmh != lastSpeed && speedText != null) { speedText.text = SpeedLabel(kmh); lastSpeed = kmh; }
                string gear = vehicle.Drivetrain.InReverse ? "R"
                    : vehicle.SpeedKmh < 0.5f && vehicle.CurrentInput.Throttle < 0.05f ? GearLabels[0]
                    : GearLabels[Mathf.Clamp(vehicle.Drivetrain.Gear, 1, GearLabels.Length - 1)];
                if (!ReferenceEquals(gear, lastGear) && gearText != null) { gearText.text = gear; lastGear = gear; }
            }
        }

        static void Tween(CanvasGroup g, float target, float speed, float dt)
        {
            if (g == null || Mathf.Approximately(g.alpha, target)) return;
            g.alpha = Mathf.MoveTowards(g.alpha, target, speed * dt);
        }

        static void SetAlpha(CanvasGroup g, float a)
        {
            if (g != null) g.alpha = a;
        }
    }
}
