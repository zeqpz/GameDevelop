// InputService — every gameplay input crosses this one surface. Actions are
// BUILT IN CODE (no .inputactions asset to hand-maintain), grouped into two
// maps:
//   • Gameplay — move/look/sprint/jump/crouch/aim/interact/camera-cycle.
//     SetGameplayBlocked(true) disables the whole map — the typing gate the
//     Roblox G-key overload taught us (chat/UI will call this).
//   • System — escape only, so cursor release always works even while typing.
//   • Ui — mouse point/clicks, R, Tab; ALWAYS on, so screens (inventory…)
//     keep working while they block the Gameplay map.
// Convention from here on: gameplay code never polls Keyboard/Mouse directly.
// Rebinding later = swapping bindings on these same actions.
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Core
{
    public class InputService
    {
        readonly InputActionMap _gameplay = new InputActionMap("Gameplay");
        readonly InputActionMap _system = new InputActionMap("System");
        readonly InputActionMap _ui = new InputActionMap("Ui");
        readonly InputAction _move, _look, _sprint, _jump, _crouch, _aim,
            _interact, _cameraMode, _escape;
        readonly InputAction _uiPoint, _uiClick, _uiRight, _uiRotate, _uiInventory;

        public bool GameplayBlocked { get; private set; }

        public InputService()
        {
            _move = _gameplay.AddAction("Move", InputActionType.Value);
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            _look = _gameplay.AddAction("Look", InputActionType.Value, "<Mouse>/delta");
            _sprint = _gameplay.AddAction("Sprint", InputActionType.Button, "<Keyboard>/leftShift");
            _jump = _gameplay.AddAction("Jump", InputActionType.Button, "<Keyboard>/space");
            _crouch = _gameplay.AddAction("Crouch", InputActionType.Button, "<Keyboard>/leftCtrl");
            _aim = _gameplay.AddAction("Aim", InputActionType.Button, "<Mouse>/rightButton");
            _interact = _gameplay.AddAction("Interact", InputActionType.Button, "<Keyboard>/e");
            _cameraMode = _gameplay.AddAction("CameraMode", InputActionType.Button, "<Keyboard>/leftAlt");
            _escape = _system.AddAction("Escape", InputActionType.Button, "<Keyboard>/escape");
            _uiPoint = _ui.AddAction("Point", InputActionType.Value, "<Mouse>/position");
            _uiClick = _ui.AddAction("Click", InputActionType.Button, "<Mouse>/leftButton");
            _uiRight = _ui.AddAction("RightClick", InputActionType.Button, "<Mouse>/rightButton");
            _uiRotate = _ui.AddAction("Rotate", InputActionType.Button, "<Keyboard>/r");
            _uiInventory = _ui.AddAction("Inventory", InputActionType.Button, "<Keyboard>/tab");
            _gameplay.Enable();
            _system.Enable();
            _ui.Enable();
        }

        public Vector2 Move => _move.ReadValue<Vector2>();
        public Vector2 LookDelta => _look.ReadValue<Vector2>();
        public bool SprintHeld => _sprint.IsPressed();
        public bool JumpPressed => _jump.WasPressedThisFrame();
        public bool CrouchPressed => _crouch.WasPressedThisFrame();
        public bool AimHeld => _aim.IsPressed();
        public bool InteractPressed => _interact.WasPressedThisFrame();
        public bool CameraCyclePressed => _cameraMode.WasPressedThisFrame();
        public bool EscapePressed => _escape.WasPressedThisFrame();

        // Ui map — always on (screens own the mouse while gameplay is blocked).
        public Vector2 MousePosition => _uiPoint.ReadValue<Vector2>();
        public bool UiClickPressed => _uiClick.WasPressedThisFrame();
        public bool UiClickReleased => _uiClick.WasReleasedThisFrame();
        public bool UiRightPressed => _uiRight.WasPressedThisFrame();
        public bool UiRotatePressed => _uiRotate.WasPressedThisFrame();
        public bool InventoryTogglePressed => _uiInventory.WasPressedThisFrame();

        // The typing gate: UI/chat blocks gameplay wholesale, escape survives.
        public void SetGameplayBlocked(bool blocked)
        {
            GameplayBlocked = blocked;
            if (blocked) _gameplay.Disable();
            else _gameplay.Enable();
        }

        public void Dispose()
        {
            _gameplay.Dispose();
            _system.Dispose();
            _ui.Dispose();
        }
    }
}
