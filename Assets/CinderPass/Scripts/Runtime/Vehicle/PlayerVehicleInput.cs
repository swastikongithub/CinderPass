using CinderPass.Core;
using UnityEngine;

namespace CinderPass.Vehicle
{
    /// <summary>Maps the player's input devices to vehicle commands.</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerVehicleInput : MonoBehaviour, IVehicleInputSource
    {
        [SerializeField] InputHub input;

        public VehicleInputState ReadInput(float deltaTime)
        {
            if (input == null) return VehicleInputState.Idle;
            return new VehicleInputState
            {
                Throttle = Mathf.Clamp01(input.Throttle),
                Brake = Mathf.Clamp01(input.Brake),
                Steer = Mathf.Clamp(input.Steer, -1f, 1f),
                Handbrake = input.Handbrake
            };
        }
    }
}
