using UnityEngine;
using UnityEngine.InputSystem;

namespace CinderPass.Core
{
    /// <summary>
    /// Owns every input action in the game (keyboard + mouse + gamepad) so bindings live in one place.
    /// Actions are built in code, which keeps the project free of fragile asset references.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-200)]
    public sealed class InputHub : MonoBehaviour
    {
        InputAction throttle, brake, steer, handbrake, reset, skip, cameraCycle, overview, debug, look, lookHold;

        public float Throttle => Read(throttle);
        public float Brake => Read(brake);
        public float Steer => Read(steer);
        public bool Handbrake => handbrake != null && handbrake.IsPressed();
        public bool ResetPressed => Pressed(reset);
        public bool SkipPressed => Pressed(skip);
        public bool CameraCyclePressed => Pressed(cameraCycle);
        public bool OverviewPressed => Pressed(overview);
        public bool DebugTogglePressed => Pressed(debug);
        /// <summary>Camera orbit input (mouse delta while RMB held, or right stick).</summary>
        public Vector2 Look
        {
            get
            {
                if (look == null) return Vector2.zero;
                Vector2 v = look.ReadValue<Vector2>();
                var mouse = Mouse.current;
                if (mouse != null && lookHold.IsPressed()) v += mouse.delta.ReadValue() * 0.06f;
                return v;
            }
        }

        void OnEnable()
        {
            throttle = new InputAction("Throttle", InputActionType.Value);
            throttle.AddBinding("<Keyboard>/w");
            throttle.AddBinding("<Keyboard>/upArrow");
            throttle.AddBinding("<Gamepad>/rightTrigger");

            brake = new InputAction("Brake", InputActionType.Value);
            brake.AddBinding("<Keyboard>/s");
            brake.AddBinding("<Keyboard>/downArrow");
            brake.AddBinding("<Gamepad>/leftTrigger");

            steer = new InputAction("Steer", InputActionType.Value);
            steer.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/a").With("Positive", "<Keyboard>/d");
            steer.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/leftArrow").With("Positive", "<Keyboard>/rightArrow");
            steer.AddBinding("<Gamepad>/leftStick/x");

            handbrake = new InputAction("Handbrake", InputActionType.Button);
            handbrake.AddBinding("<Keyboard>/space");
            handbrake.AddBinding("<Gamepad>/buttonSouth");

            reset = new InputAction("Reset", InputActionType.Button);
            reset.AddBinding("<Keyboard>/r");
            reset.AddBinding("<Gamepad>/buttonNorth");

            skip = new InputAction("SkipIntro", InputActionType.Button);
            skip.AddBinding("<Keyboard>/space");
            skip.AddBinding("<Keyboard>/enter");
            skip.AddBinding("<Gamepad>/start");
            skip.AddBinding("<Gamepad>/buttonSouth");

            cameraCycle = new InputAction("CameraCycle", InputActionType.Button);
            cameraCycle.AddBinding("<Keyboard>/c");
            cameraCycle.AddBinding("<Gamepad>/rightShoulder");

            overview = new InputAction("Overview", InputActionType.Button);
            overview.AddBinding("<Keyboard>/v");
            overview.AddBinding("<Gamepad>/select");

            debug = new InputAction("Debug", InputActionType.Button);
            debug.AddBinding("<Keyboard>/f1");

            look = new InputAction("Look", InputActionType.Value);
            look.AddBinding("<Gamepad>/rightStick");
            lookHold = new InputAction("LookHold", InputActionType.Button);
            lookHold.AddBinding("<Mouse>/rightButton");

            foreach (var a in All()) a.Enable();
        }

        void OnDisable()
        {
            foreach (var a in All())
            {
                if (a == null) continue;
                a.Disable();
                a.Dispose();
            }
        }

        InputAction[] All() => new[] { throttle, brake, steer, handbrake, reset, skip, cameraCycle, overview, debug, look, lookHold };

        static float Read(InputAction a) => a != null ? a.ReadValue<float>() : 0f;
        static bool Pressed(InputAction a) => a != null && a.WasPressedThisFrame();
    }
}
