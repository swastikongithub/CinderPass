namespace CinderPass.Vehicle
{
    /// <summary>Normalised driver commands for one physics step.</summary>
    public struct VehicleInputState
    {
        /// <summary>0..1 accelerator (drives in the currently selected direction).</summary>
        public float Throttle;
        /// <summary>0..1 brake. Held at a standstill it selects reverse.</summary>
        public float Brake;
        /// <summary>-1 (left) .. 1 (right), relative to the current speed-sensitive steering lock.</summary>
        public float Steer;
        public bool Handbrake;

        public static VehicleInputState Idle => default;
    }

    /// <summary>
    /// Anything that can drive the vehicle: the player, the spline autopilot, a replay, an AI.
    /// The <see cref="VehicleController"/> owns physics; input sources only decide intent.
    /// </summary>
    public interface IVehicleInputSource
    {
        VehicleInputState ReadInput(float deltaTime);
    }
}
