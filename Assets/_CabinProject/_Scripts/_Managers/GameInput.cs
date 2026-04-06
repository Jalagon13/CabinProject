using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CabinProject
{
    public class GameInput : MonoBehaviour
    {
        public static GameInput Instance { get; private set; }

        public event EventHandler<InputAction.CallbackContext> OnMove;
        public event EventHandler<InputAction.CallbackContext> OnLook;
        public event EventHandler<InputAction.CallbackContext> OnJump;
        public event EventHandler<InputAction.CallbackContext> OnAttack;
        public event EventHandler<InputAction.CallbackContext> OnSprintStarted;
        public event EventHandler<InputAction.CallbackContext> OnSprintEnded;
        public event EventHandler<InputAction.CallbackContext> OnInventoryToggle;
        public event EventHandler<InputAction.CallbackContext> OnInteract;

        private PlayerInput _playerInput;

        public Vector2 MoveInput { get; private set; }
        public Vector2 LookInput { get; private set; }
        public bool JumpPressed { get; private set; }
        public bool IsHoldingDownJump { get; private set; }
        public bool IsHoldingDownSprint { get; private set; }
        public bool IsHoldingDownAttack { get; private set; }

        private void Awake()
        {
            Instance = this;

            _playerInput = new();
            _playerInput.Enable();

            _playerInput.Player.Move.started += PlayerInput_OnMove;
            _playerInput.Player.Move.performed += PlayerInput_OnMove;
            _playerInput.Player.Move.canceled += PlayerInput_OnMove;
            _playerInput.Player.Look.started += PlayerInput_OnLook;
            _playerInput.Player.Look.performed += PlayerInput_OnLook;
            _playerInput.Player.Look.canceled += PlayerInput_OnLook;
            _playerInput.Player.Jump.started += PlayerInput_OnJump;
            _playerInput.Player.Jump.canceled += PlayerInput_OnJump;
            _playerInput.Player.Sprint.started += PlayerInput_Sprint;
            _playerInput.Player.Sprint.canceled += PlayerInput_Sprint;
            _playerInput.Player.Attack.started += PlayerInput_OnAttack;
            _playerInput.Player.Attack.canceled += PlayerInput_OnAttack;
            _playerInput.Player.Interact.started += PlayerInput_OnInteract;
            
            _playerInput.UI.ToggleInventory.started += PlayerInput_OnIventoryToggle;
        }

        public void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            if (_playerInput == null)
            {
                return;
            }

            _playerInput.Player.Move.started -= PlayerInput_OnMove;
            _playerInput.Player.Move.performed -= PlayerInput_OnMove;
            _playerInput.Player.Move.canceled -= PlayerInput_OnMove;
            _playerInput.Player.Look.started -= PlayerInput_OnLook;
            _playerInput.Player.Look.performed -= PlayerInput_OnLook;
            _playerInput.Player.Look.canceled -= PlayerInput_OnLook;
            _playerInput.Player.Jump.started -= PlayerInput_OnJump;
            _playerInput.Player.Jump.canceled -= PlayerInput_OnJump;
            _playerInput.Player.Sprint.started -= PlayerInput_Sprint;
            _playerInput.Player.Sprint.canceled -= PlayerInput_Sprint;
            _playerInput.Player.Attack.started -= PlayerInput_OnAttack;
            _playerInput.Player.Attack.canceled -= PlayerInput_OnAttack;
            _playerInput.Player.Interact.started -= PlayerInput_OnInteract;

            _playerInput.UI.ToggleInventory.started -= PlayerInput_OnIventoryToggle;

            _playerInput.Disable();
            _playerInput.Dispose();
        }

        private void PlayerInput_OnInteract(InputAction.CallbackContext context)
        {
            OnInteract?.Invoke(this, context);
        }

        private void PlayerInput_OnIventoryToggle(InputAction.CallbackContext context)
        {
            OnInventoryToggle?.Invoke(this, context);
        }

        private void PlayerInput_OnAttack(InputAction.CallbackContext context)
        {
            IsHoldingDownAttack = context.ReadValueAsButton();
            OnAttack?.Invoke(this, context);
        }

        private void PlayerInput_OnMove(InputAction.CallbackContext context)
        {
            MoveInput = context.ReadValue<Vector2>();
            OnMove?.Invoke(this, context);
        }

        private void PlayerInput_OnLook(InputAction.CallbackContext context)
        {
            LookInput = context.ReadValue<Vector2>();
            OnLook?.Invoke(this, context);
        }

        private void PlayerInput_OnJump(InputAction.CallbackContext context)
        {
            if (context.started)
            {
                JumpPressed = true;
            }

            IsHoldingDownJump = context.ReadValueAsButton();
            OnJump?.Invoke(this, context);
        }

        private void PlayerInput_Sprint(InputAction.CallbackContext context)
        {
            IsHoldingDownSprint = context.ReadValueAsButton();

            if (context.started)
            {
                OnSprintStarted?.Invoke(this, context);
            }
            else if (context.canceled)
            {
                OnSprintEnded?.Invoke(this, context);
            }
        }

        public bool ConsumeJumpPressed()
        {
            if (!JumpPressed)
            {
                return false;
            }

            JumpPressed = false;
            return true;
        }
    }
}
