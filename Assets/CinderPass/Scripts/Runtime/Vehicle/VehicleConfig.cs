using UnityEngine;

namespace CinderPass.Vehicle
{
    /// <summary>
    /// Complete tuning data for a vehicle. Kept out of the MonoBehaviour so handling can be tuned
    /// (even during Play Mode) without touching prefabs or code.
    /// </summary>
    [CreateAssetMenu(menuName = "Cinder Pass/Vehicle Config", fileName = "VehicleConfig")]
    public sealed class VehicleConfig : ScriptableObject
    {
        [Header("Body")]
        [Min(100f)] public float mass = 1650f;
        [Min(0f)] public float linearDamping = 0.02f;
        [Min(0f)] public float angularDamping = 0.4f;
        [Min(1f)] public float maxAngularVelocity = 7f;

        [Header("Engine")]
        [Tooltip("Engine torque (Nm) as a function of RPM.")]
        public AnimationCurve torqueCurve = new AnimationCurve(
            new Keyframe(800f, 260f), new Keyframe(2200f, 400f), new Keyframe(3800f, 450f),
            new Keyframe(5200f, 420f), new Keyframe(6400f, 330f));
        [Min(300f)] public float idleRpm = 850f;
        [Min(1000f)] public float maxRpm = 6400f;
        [Tooltip("How quickly engine RPM follows its target (1/s).")]
        [Min(0.1f)] public float rpmResponse = 7f;

        [Header("Transmission")]
        public float[] gearRatios = { 3.6f, 2.25f, 1.6f, 1.2f, 0.95f };
        [Min(0.1f)] public float reverseRatio = 3.4f;
        [Min(0.1f)] public float finalDrive = 3.9f;
        [Range(0.3f, 1f)] public float drivetrainEfficiency = 0.85f;
        [Min(0f)] public float shiftUpRpm = 5500f;
        [Min(0f)] public float shiftDownRpm = 2500f;
        [Tooltip("Upshift RPM at minimal throttle (cruising). Shift points blend toward shiftUpRpm with throttle.")]
        [Min(0f)] public float cruiseShiftUpRpm = 2900f;
        [Tooltip("Downshift RPM at minimal throttle.")]
        [Min(0f)] public float cruiseShiftDownRpm = 1500f;
        [Min(0f)] public float shiftDuration = 0.22f;
        [Tooltip("Share of drive torque sent to the front axle (4x4).")]
        [Range(0f, 1f)] public float frontTorqueShare = 0.42f;
        [Min(1f)] public float topSpeedKmh = 118f;
        [Min(1f)] public float reverseTopSpeedKmh = 32f;

        [Header("Brakes")]
        [Min(0f)] public float brakeTorque = 3400f;
        [Range(0f, 1f)] public float frontBrakeBias = 0.62f;
        [Min(0f)] public float handbrakeTorque = 5200f;
        [Tooltip("Drag torque per wheel when coasting off-throttle.")]
        [Min(0f)] public float engineBrakeTorque = 280f;
        [Tooltip("Brake torque per wheel used to hold the vehicle still on slopes with no input.")]
        [Min(0f)] public float holdTorque = 900f;

        [Header("Steering")]
        [Range(5f, 50f)] public float maxSteerAngle = 34f;
        [Range(2f, 40f)] public float highSpeedSteerAngle = 11f;
        [Min(1f)] public float steerFadeSpeedKmh = 95f;
        [Tooltip("Steering input slew rate toward full lock (1/s).")]
        [Min(0.1f)] public float steerSpeed = 3.2f;
        [Tooltip("Steering slew rate back toward centre (1/s).")]
        [Min(0.1f)] public float steerReturnSpeed = 5.5f;

        [Header("Suspension")]
        [Min(0.05f)] public float suspensionDistance = 0.3f;
        [Tooltip("Natural frequency of the corner spring (Hz). ~1.4-1.8 for a long-travel off-roader.")]
        [Min(0.3f)] public float springFrequency = 1.6f;
        [Range(0.05f, 1.5f)] public float dampingRatio = 0.42f;
        [Range(0f, 1f)] public float suspensionTargetPosition = 0.45f;
        [Tooltip("Force application point relative to wheel centre. Near the CoM height reduces body roll.")]
        public float forceAppPointDistance = 0.12f;
        [Min(1f)] public float wheelMass = 28f;
        [Min(0f)] public float antiRollStiffness = 11000f;

        [Header("Tyres")]
        public float forwardExtremumSlip = 0.4f;
        public float forwardExtremumValue = 1f;
        public float forwardAsymptoteSlip = 0.8f;
        public float forwardAsymptoteValue = 0.55f;
        public float forwardStiffness = 1.15f;
        public float sidewaysExtremumSlip = 0.22f;
        public float sidewaysExtremumValue = 1f;
        public float sidewaysAsymptoteSlip = 0.5f;
        public float sidewaysAsymptoteValue = 0.78f;
        public float sidewaysStiffness = 1.3f;
        [Tooltip("Rear lateral grip multiplier while the handbrake is held.")]
        [Range(0.1f, 1f)] public float handbrakeRearGrip = 0.55f;

        [Header("Traction control")]
        [Range(0.05f, 2f)] public float tractionSlipLimit = 0.45f;
        [Min(0.1f)] public float tractionRecoverRate = 3f;

        [Header("Aerodynamics & stability")]
        [Tooltip("Downforce in N per (m/s)^2 while grounded.")]
        [Min(0f)] public float downforce = 2.2f;
        [Tooltip("Self-righting assistance while airborne (rad/s^2 per unit tilt).")]
        [Min(0f)] public float airborneUprightStrength = 2.5f;
        [Min(0f)] public float airborneAngularDamping = 1.2f;

        public int ForwardGearCount => gearRatios != null ? gearRatios.Length : 0;

        void OnValidate()
        {
            if (gearRatios == null || gearRatios.Length == 0) gearRatios = new[] { 3.6f, 2.25f, 1.6f, 1.2f, 0.95f };
            maxRpm = Mathf.Max(maxRpm, idleRpm + 500f);
            shiftUpRpm = Mathf.Clamp(shiftUpRpm, idleRpm + 500f, maxRpm);
            shiftDownRpm = Mathf.Clamp(shiftDownRpm, idleRpm, shiftUpRpm - 500f);
            cruiseShiftUpRpm = Mathf.Clamp(cruiseShiftUpRpm, idleRpm + 500f, shiftUpRpm);
            cruiseShiftDownRpm = Mathf.Clamp(cruiseShiftDownRpm, idleRpm, cruiseShiftUpRpm * 0.55f);
        }
    }
}
