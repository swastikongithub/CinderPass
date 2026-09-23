using UnityEngine;

namespace CinderPass.Vehicle
{
    /// <summary>
    /// Engine + automatic gearbox model. Pure C#, no Unity lifecycle: given driven wheel speed and
    /// throttle it produces the axle torque and exposes RPM/gear for audio and UI.
    /// </summary>
    public sealed class Drivetrain
    {
        VehicleConfig config;
        float shiftTimer;

        /// <summary>1..N forward gear. Reverse is tracked separately.</summary>
        public int Gear { get; private set; } = 1;
        public bool InReverse { get; private set; }
        public float Rpm { get; private set; }
        public bool IsShifting => shiftTimer > 0f;
        /// <summary>0..1 normalised RPM for audio.</summary>
        public float RpmNormalized => config == null ? 0f : Mathf.InverseLerp(config.idleRpm, config.maxRpm, Rpm);

        public void Initialize(VehicleConfig cfg)
        {
            config = cfg;
            Reset();
        }

        public void Reset()
        {
            Gear = 1;
            InReverse = false;
            shiftTimer = 0f;
            Rpm = config != null ? config.idleRpm : 0f;
        }

        public void SetReverse(bool reverse)
        {
            if (reverse == InReverse) return;
            InReverse = reverse;
            Gear = 1;
            shiftTimer = config.shiftDuration;
        }

        float CurrentRatio => (InReverse ? config.reverseRatio : config.gearRatios[Mathf.Clamp(Gear - 1, 0, config.gearRatios.Length - 1)]) * config.finalDrive;

        /// <summary>
        /// Advances the engine and returns total drive torque at the wheels (signed: negative in reverse).
        /// </summary>
        /// <param name="drivenWheelRpm">Average |rpm| of driven wheels that are on the ground.</param>
        /// <param name="throttle">0..1 accelerator.</param>
        /// <param name="grounded">True when at least one driven wheel has contact.</param>
        /// <param name="speedLimitFactor">0..1 multiplier applied near top speed.</param>
        public float Step(float drivenWheelRpm, float throttle, bool grounded, float speedLimitFactor, float dt)
        {
            float ratio = CurrentRatio;
            float wheelDrivenRpm = Mathf.Abs(drivenWheelRpm) * ratio;
            float freeRevRpm = Mathf.Lerp(config.idleRpm, config.maxRpm * 0.92f, throttle);
            // When the tyres are loaded, the engine is locked to the wheels (with a little converter slip);
            // airborne it free-revs toward the throttle target.
            float target = grounded
                ? Mathf.Max(config.idleRpm, Mathf.Lerp(wheelDrivenRpm, Mathf.Max(wheelDrivenRpm, freeRevRpm * 0.55f), 0.35f))
                : freeRevRpm;
            Rpm = Mathf.Lerp(Rpm, Mathf.Min(target, config.maxRpm), 1f - Mathf.Exp(-config.rpmResponse * dt));

            if (shiftTimer > 0f)
            {
                shiftTimer -= dt;
                return 0f;
            }

            if (!InReverse && grounded)
            {
                // Load-dependent shift points: light throttle short-shifts to cruise at low RPM, full throttle
                // holds gears to the configured points. The down point stays below the post-upshift RPM
                // (widest ratio step is ~0.62) so the gearbox never hunts.
                float upRpm = Mathf.Lerp(config.cruiseShiftUpRpm, config.shiftUpRpm, throttle);
                float downRpm = Mathf.Lerp(config.cruiseShiftDownRpm, config.shiftDownRpm, throttle);
                if (Rpm > upRpm && Gear < config.ForwardGearCount && throttle > 0.05f)
                {
                    Gear++;
                    shiftTimer = config.shiftDuration;
                    return 0f;
                }
                if (Rpm < downRpm && Gear > 1)
                {
                    Gear--;
                    shiftTimer = config.shiftDuration * 0.6f;
                }
            }

            float limiter = Rpm >= config.maxRpm - 20f ? 0f : 1f;
            float engineTorque = config.torqueCurve.Evaluate(Rpm) * throttle * limiter * speedLimitFactor;
            float wheelTorque = engineTorque * ratio * config.drivetrainEfficiency;
            return InReverse ? -wheelTorque : wheelTorque;
        }
    }
}
