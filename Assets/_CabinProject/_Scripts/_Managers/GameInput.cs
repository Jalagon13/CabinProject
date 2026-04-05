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
        public event EventHandler<InputAction.CallbackContext> OnSprintStarted;
        public event EventHandler<InputAction.CallbackContext> OnSprintEnded;

        private PlayerInput _playerInput;

        public bool IsHoldingDownJump { get; private set; }
        public bool IsHoldingDownSprint { get; private set; }

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
        }

        public void OnDestroy()
        {
            _playerInput.Disable();
            _playerInput.Dispose();
        }

        private void PlayerInput_OnMove(InputAction.CallbackContext context)
        {
            throw new NotImplementedException();
        }

        private void PlayerInput_OnLook(InputAction.CallbackContext context)
        {
            throw new NotImplementedException();
        }

        private void PlayerInput_OnJump(InputAction.CallbackContext context)
        {
            throw new NotImplementedException();
        }

        private void PlayerInput_Sprint(InputAction.CallbackContext context)
        {
            throw new NotImplementedException();
        }
    }
}
