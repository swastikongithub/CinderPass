using System;
using System.Collections.Generic;
using CinderPass.Environment;
using UnityEngine;

namespace CinderPass.Vehicle
{
    /// <summary>
    /// Physics core of the off-road vehicle: WheelCollider suspension, 4x4 drivetrain, Ackermann steering,
    /// anti-roll bars, surface-dependent grip, traction control and airborne stabilisation.
    /// It does not know who is driving - it consumes an <see cref="IVehicleInputSource"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    [DefaultExecutionOrder(-10)]
    public sealed class VehicleController : MonoBehaviour
    {
        [SerializeField] VehicleConfig config;
        [SerializeField] Transform centerOfMass;
        [SerializeField] VehicleWheel[] wheels = Array.Empty<VehicleWheel>();
        [SerializeField] TerrainSurfaceMap surfaceMap;

        readonly Drivetrain drivetrain = new Drivetrain();
        Rigidbody body;
        float steerState;
        float wheelbase = 2.9f;
        float trackWidth = 1.9f;
        bool frozen;

        public IVehicleInputSource InputSource { get; set; }
        public Rigidbody Body => body;
        public VehicleConfig Config => config;
        public IReadOnlyList<VehicleWheel> Wheels => wheels;
        public Drivetrain Drivetrain => drivetrain;
        public VehicleInputState CurrentInput { get; private set; }
        /// <summary>Signed speed along the vehicle's forward axis (m/s).</summary>
        public float ForwardSpeed { get; private set; }
        public float SpeedKmh => body != null ? body.linearVelocity.magnitude * 3.6f : 0f;
        public int GroundedWheelCount { get; private set; }
        public float Wheelbase => wheelbase;
        public float TrackWidth => trackWidth;
        public float CurrentMaxSteerAngle { get; private set; }
        public float SteerAngle { get; private set; }
        public bool IsFrozen => frozen;
        /// <summary>Normalised 0..1 total tyre slip, useful for audio/VFX.</summary>
        public float TyreSlip { get; private set; }
        public event Action<Collision> Impact;

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            if (config == null)
            {
                Debug.LogError($"{name}: VehicleController has no VehicleConfig assigned.", this);
                enabled = false;
                return;
            }
            ApplyConfig();
        }

        /// <summary>Pushes the config asset into the rigidbody and wheel colliders. Safe to call at runtime.</summary>
        public void ApplyConfig()
        {
            drivetrain.Initialize(config);
            body.mass = config.mass;
            body.linearDamping = config.linearDamping;
            body.angularDamping = config.angularDamping;
            body.maxAngularVelocity = config.maxAngularVelocity;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            if (centerOfMass != null) body.centerOfMass = transform.InverseTransformPoint(centerOfMass.position);

            float sprungMassPerCorner = config.mass / Mathf.Max(1, wheels.Length);
            float omega = 2f * Mathf.PI * config.springFrequency;
            float spring = sprungMassPerCorner * omega * omega;
            float damper = 2f * config.dampingRatio * Mathf.Sqrt(spring * sprungMassPerCorner);

            foreach (var w in wheels)
            {
                var wc = w.collider;
                wc.mass = config.wheelMass;
                wc.suspensionDistance = config.suspensionDistance;
                wc.forceAppPointDistance = config.forceAppPointDistance;
                wc.suspensionSpring = new JointSpring { spring = spring, damper = damper, targetPosition = config.suspensionTargetPosition };
                wc.wheelDampingRate = 0.6f;
                w.appliedGrip = -1f;
                w.tractionFactor = 1f;
                ApplyFriction(w, 1f, 1f);
            }
            if (wheels.Length > 0) wheels[0].collider.ConfigureVehicleSubsteps(6f, 14, 18);

            MeasureGeometry();
        }

        void MeasureGeometry()
        {
            Vector3 front = Vector3.zero, rear = Vector3.zero;
            float left = 0f, right = 0f;
            int nf = 0, nr = 0, nl = 0, nrt = 0;
            foreach (var w in wheels)
            {
                Vector3 p = transform.InverseTransformPoint(w.collider.transform.position);
                if (w.IsFront) { front += p; nf++; } else { rear += p; nr++; }
                if (w.isLeft) { left += p.x; nl++; } else { right += p.x; nrt++; }
            }
            if (nf > 0 && nr > 0) wheelbase = Mathf.Max(0.5f, (front / nf).z - (rear / nr).z);
            if (nl > 0 && nrt > 0) trackWidth = Mathf.Max(0.5f, right / nrt - left / nl);
        }

        void FixedUpdate()
        {
            if (frozen) return;
            float dt = Time.fixedDeltaTime;
            var input = InputSource != null ? InputSource.ReadInput(dt) : VehicleInputState.Idle;
            CurrentInput = input;
            // WheelCollider torque does not wake a sleeping rigidbody: without this, a car that came to rest
            // (or was just respawned) would rev its engine but never move.
            bool hasInput = input.Throttle > 0.01f || input.Brake > 0.01f || Mathf.Abs(input.Steer) > 0.01f || input.Handbrake;
            if (hasInput && body.IsSleeping()) body.WakeUp();
            ForwardSpeed = Vector3.Dot(body.linearVelocity, transform.forward);

            UpdateContacts();
            UpdateSteering(input.Steer, dt);
            UpdateDrive(input, dt);
            ApplyAntiRoll();
            ApplySurfaceDrag();
            ApplyAerodynamics();
            CacheWheelPoses();
        }

        void LateUpdate()
        {
            // The transform is interpolated; re-express the cached physics-space wheel poses relative to it
            // so the wheels never jitter against the body.
            Quaternion rot = transform.rotation;
            Vector3 pos = transform.position;
            foreach (var w in wheels)
            {
                if (w.visual == null) continue;
                w.visual.SetPositionAndRotation(pos + rot * w.localPosition, rot * w.localRotation);
            }
        }

        void UpdateContacts()
        {
            int grounded = 0;
            float slip = 0f;
            foreach (var w in wheels)
            {
                var wc = w.collider;
                w.grounded = wc.GetGroundHit(out w.hit);
                if (w.grounded)
                {
                    grounded++;
                    float travel = -wc.transform.InverseTransformPoint(w.hit.point).y - wc.radius;
                    w.compression = 1f - Mathf.Clamp01(travel / Mathf.Max(0.01f, wc.suspensionDistance));
                    w.surface = ResolveSurface(w.hit);
                    slip += Mathf.Abs(w.hit.sidewaysSlip) + Mathf.Abs(w.hit.forwardSlip) * 0.5f;
                }
                else
                {
                    w.compression = Mathf.MoveTowards(w.compression, 0f, Time.fixedDeltaTime * 4f);
                }
            }
            GroundedWheelCount = grounded;
            TyreSlip = Mathf.Clamp01(slip / Mathf.Max(1, wheels.Length));
        }

        SurfaceInfo ResolveSurface(in WheelHit hit)
        {
            if (surfaceMap != null && hit.collider is TerrainCollider) return surfaceMap.Sample(hit.point);
            return surfaceMap != null && surfaceMap.Library != null ? surfaceMap.Library.Fallback : null;
        }

        void UpdateSteering(float target, float dt)
        {
            float speedKmh = Mathf.Abs(ForwardSpeed) * 3.6f;
            CurrentMaxSteerAngle = Mathf.Lerp(config.maxSteerAngle, config.highSpeedSteerAngle, Mathf.Clamp01(speedKmh / config.steerFadeSpeedKmh));
            target = Mathf.Clamp(target, -1f, 1f);
            bool returning = Mathf.Abs(target) < Mathf.Abs(steerState) || Mathf.Sign(target) != Mathf.Sign(steerState);
            steerState = Mathf.MoveTowards(steerState, target, (returning ? config.steerReturnSpeed : config.steerSpeed) * dt);

            float angle = steerState * CurrentMaxSteerAngle;
            SteerAngle = angle;
            float inner = angle, outer = angle;
            if (Mathf.Abs(angle) > 0.1f)
            {
                // Ackermann: the inside wheel turns tighter so both follow concentric circles.
                float radius = wheelbase / Mathf.Tan(Mathf.Abs(angle) * Mathf.Deg2Rad);
                inner = Mathf.Sign(angle) * Mathf.Atan(wheelbase / (radius - trackWidth * 0.5f)) * Mathf.Rad2Deg;
                outer = Mathf.Sign(angle) * Mathf.Atan(wheelbase / (radius + trackWidth * 0.5f)) * Mathf.Rad2Deg;
            }
            foreach (var w in wheels)
            {
                if (!w.IsFront) continue;
                bool isInside = (angle > 0f && !w.isLeft) || (angle < 0f && w.isLeft);
                w.collider.steerAngle = isInside ? inner : outer;
            }
        }

        void UpdateDrive(VehicleInputState input, float dt)
        {
            float speed = ForwardSpeed;
            bool wantForward = input.Throttle > 0.05f;
            bool wantBack = input.Brake > 0.05f;

            // Direction selection: brake held at a standstill engages reverse; throttle returns to drive.
            if (drivetrain.InReverse)
            {
                if (wantForward && !wantBack && speed > -1.2f) drivetrain.SetReverse(false);
            }
            else if (wantBack && !wantForward && speed < 0.8f)
            {
                drivetrain.SetReverse(true);
            }

            float throttle, brake;
            if (!drivetrain.InReverse)
            {
                throttle = input.Throttle;
                brake = input.Brake;
                if (wantForward && speed < -1f) { brake = Mathf.Max(brake, input.Throttle); throttle = 0f; }
            }
            else
            {
                throttle = input.Brake;
                brake = speed > 1f ? input.Brake : 0f;
                if (wantForward) { brake = Mathf.Max(brake, input.Throttle); throttle = 0f; }
            }

            // Driven wheel speed for the engine model.
            float wheelRpm = 0f;
            int drivenGrounded = 0, drivenCount = 0;
            foreach (var w in wheels)
            {
                if (!w.driven) continue;
                drivenCount++;
                if (w.grounded) { wheelRpm += Mathf.Abs(w.collider.rpm); drivenGrounded++; }
            }
            wheelRpm = drivenGrounded > 0 ? wheelRpm / drivenGrounded : 0f;

            float limitKmh = drivetrain.InReverse ? config.reverseTopSpeedKmh : config.topSpeedKmh;
            float speedFactor = Mathf.Clamp01((limitKmh - Mathf.Abs(speed) * 3.6f) / 8f);
            float axleTorque = drivetrain.Step(wheelRpm, throttle, drivenGrounded > 0, speedFactor, dt);

            bool coasting = throttle < 0.02f && brake < 0.02f;
            bool holding = coasting && Mathf.Abs(speed) < 0.6f && !input.Handbrake;
            int frontDriven = 0, rearDriven = 0;
            foreach (var w in wheels) if (w.driven) { if (w.IsFront) frontDriven++; else rearDriven++; }

            foreach (var w in wheels)
            {
                var wc = w.collider;
                float share = w.IsFront ? config.frontTorqueShare / Mathf.Max(1, frontDriven) : (1f - config.frontTorqueShare) / Mathf.Max(1, rearDriven);
                if (!w.driven) share = 0f;

                // Traction control: trim torque on a wheel that is spinning up beyond the slip limit.
                // (Unity reports acceleration slip as negative forwardSlip.)
                if (w.grounded && Mathf.Abs(w.hit.forwardSlip) > config.tractionSlipLimit && Mathf.Sign(w.hit.forwardSlip) == -Mathf.Sign(axleTorque))
                    w.tractionFactor = Mathf.Max(0.25f, w.tractionFactor - dt * config.tractionRecoverRate * 2f);
                else
                    w.tractionFactor = Mathf.MoveTowards(w.tractionFactor, 1f, dt * config.tractionRecoverRate);

                wc.motorTorque = axleTorque * share * w.tractionFactor;

                float brakeTorque = brake * config.brakeTorque * (w.IsFront ? config.frontBrakeBias : 1f - config.frontBrakeBias) * 2f;
                if (coasting) brakeTorque += config.engineBrakeTorque;
                if (holding) brakeTorque = Mathf.Max(brakeTorque, config.holdTorque);
                if (input.Handbrake && !w.IsFront) brakeTorque = Mathf.Max(brakeTorque, config.handbrakeTorque);
                // PhysX locks a stationary wheel whenever brakeTorque is non-zero, however small, so any
                // "always on" drag here would stop the car pulling away from rest. Brake torque is only
                // applied for genuine braking; surface drag is applied as a body force (ApplySurfaceDrag).
                wc.brakeTorque = brakeTorque > 1f ? brakeTorque : 0f;

                float grip = w.surface != null ? w.surface.grip : 1f;
                float lateral = input.Handbrake && !w.IsFront ? config.handbrakeRearGrip : 1f;
                ApplyFriction(w, grip, lateral);
            }
        }

        void ApplyFriction(VehicleWheel w, float grip, float lateralScale)
        {
            float key = grip * 10f + lateralScale;
            if (Mathf.Approximately(w.appliedGrip, key)) return;
            w.appliedGrip = key;
            w.collider.forwardFriction = new WheelFrictionCurve
            {
                extremumSlip = config.forwardExtremumSlip,
                extremumValue = config.forwardExtremumValue,
                asymptoteSlip = config.forwardAsymptoteSlip,
                asymptoteValue = config.forwardAsymptoteValue,
                stiffness = config.forwardStiffness * grip
            };
            w.collider.sidewaysFriction = new WheelFrictionCurve
            {
                extremumSlip = config.sidewaysExtremumSlip,
                extremumValue = config.sidewaysExtremumValue,
                asymptoteSlip = config.sidewaysAsymptoteSlip,
                asymptoteValue = config.sidewaysAsymptoteValue,
                stiffness = config.sidewaysStiffness * grip * lateralScale
            };
        }

        void ApplyAntiRoll()
        {
            if (config.antiRollStiffness <= 0f) return;
            for (int i = 0; i < wheels.Length; i++)
            {
                var a = wheels[i];
                if (!a.isLeft) continue;
                for (int j = 0; j < wheels.Length; j++)
                {
                    var b = wheels[j];
                    if (b.isLeft || b.axle != a.axle) continue;
                    float travelA = a.grounded ? 1f - a.compression : 1f;
                    float travelB = b.grounded ? 1f - b.compression : 1f;
                    float force = (travelA - travelB) * config.antiRollStiffness;
                    if (a.grounded) body.AddForceAtPosition(a.collider.transform.up * -force, a.collider.transform.position);
                    if (b.grounded) body.AddForceAtPosition(b.collider.transform.up * force, b.collider.transform.position);
                }
            }
        }

        /// <summary>Rolling resistance of soft surfaces (ash, grass, forest floor) as a drag force on the body.</summary>
        void ApplySurfaceDrag()
        {
            Vector3 v = body.linearVelocity;
            float speed = v.magnitude;
            if (speed < 0.3f) return;
            float force = 0f;
            foreach (var w in wheels)
                if (w.grounded && w.surface != null) force += w.surface.rollingResistance / Mathf.Max(0.1f, w.collider.radius);
            if (force > 0f) body.AddForce(-v / speed * force * Mathf.Clamp01(speed / 2f));
        }

        void ApplyAerodynamics()
        {
            Vector3 v = body.linearVelocity;
            if (GroundedWheelCount > 0)
            {
                body.AddForce(-transform.up * (config.downforce * v.sqrMagnitude));
                return;
            }
            // Airborne: gently level the chassis and bleed off tumble so jumps land on the wheels.
            Vector3 tilt = Vector3.Cross(transform.up, Vector3.up);
            Vector3 w = body.angularVelocity;
            Vector3 yaw = Vector3.Project(w, transform.up);
            body.AddTorque(tilt * config.airborneUprightStrength - (w - yaw) * config.airborneAngularDamping, ForceMode.Acceleration);
        }

        void CacheWheelPoses()
        {
            Quaternion inv = Quaternion.Inverse(body.rotation);
            Vector3 origin = body.position;
            foreach (var w in wheels)
            {
                w.collider.GetWorldPose(out var p, out var r);
                w.localPosition = inv * (p - origin);
                w.localRotation = inv * r;
            }
        }

        /// <summary>Stops all simulation (used while a hazard sequence plays). Physics state is discarded.</summary>
        public void SetFrozen(bool value)
        {
            if (frozen == value) return;
            frozen = value;
            if (value)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
                foreach (var w in wheels) { w.collider.motorTorque = 0f; w.collider.brakeTorque = config.holdTorque * 4f; }
            }
            else
            {
                body.isKinematic = false;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.WakeUp();
            }
        }

        /// <summary>Instantly places the vehicle at a pose with zero velocity (respawn / intro placement).</summary>
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            bool wasKinematic = body.isKinematic;
            if (!wasKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.position = position;
            body.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
            Physics.SyncTransforms();
            steerState = 0f;
            drivetrain.Reset();
            foreach (var w in wheels)
            {
                w.collider.motorTorque = 0f;
                w.collider.brakeTorque = config.holdTorque * 4f;
                w.collider.steerAngle = 0f;
                w.tractionFactor = 1f;
            }
            if (!body.isKinematic) body.WakeUp();
            CacheWheelPoses();
        }

        void OnCollisionEnter(Collision collision)
        {
            if (collision.impulse.magnitude > config.mass * 2.5f) Impact?.Invoke(collision);
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            var rb = body != null ? body : GetComponent<Rigidbody>();
            if (rb != null && Application.isPlaying)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(rb.worldCenterOfMass, 0.12f);
            }
            if (wheels == null) return;
            foreach (var w in wheels)
            {
                if (w?.collider == null) continue;
                Gizmos.color = w.grounded ? new Color(0.2f, 1f, 0.3f) : new Color(1f, 0.3f, 0.2f);
                if (w.grounded) Gizmos.DrawLine(w.collider.transform.position, w.hit.point);
            }
        }
#endif
    }
}
