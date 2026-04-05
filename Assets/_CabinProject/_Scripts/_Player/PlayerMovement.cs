using UnityEngine;
using CabinProject;

/// <summary>
/// Handles first-person character movement, sprinting, jumping, and manual gravity.
/// Attach this to the Player GameObject that has a CharacterController component.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float _walkSpeed = 4f;
    [SerializeField] private float _sprintSpeed = 7f;

    [Header("Jump")]
    [SerializeField] private float _jumpHeight = 1.5f;
    [SerializeField] private float _gravity = -9.81f;

    private CharacterController _characterController;
    private Vector3 _velocity;

    private void Awake()
    {
        _characterController = GetComponent<CharacterController>();
    }

    private void Update()
    {
        bool isGrounded = _characterController.isGrounded;
        GameInput input = GameInput.Instance;

        // Keep the controller snapped to the ground so it does not accumulate downward velocity.
        if (isGrounded && _velocity.y < 0f)
        {
            _velocity.y = -2f;
        }

        float moveSpeed = input != null && input.IsHoldingDownSprint ? _sprintSpeed : _walkSpeed;
        Vector2 moveInput = input != null ? input.MoveInput : Vector2.zero;

        Vector3 move = transform.right * moveInput.x + transform.forward * moveInput.y;
        _characterController.Move(move * moveSpeed * Time.deltaTime);

        if (isGrounded && input != null && input.ConsumeJumpPressed())
        {
            // v = sqrt(h * -2g) produces the initial jump velocity needed for the desired height.
            _velocity.y = Mathf.Sqrt(_jumpHeight * -2f * _gravity);
        }

        _velocity.y += _gravity * Time.deltaTime;
        _characterController.Move(_velocity * Time.deltaTime);
    }
}
