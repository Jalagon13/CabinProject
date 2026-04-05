using UnityEngine;
using CabinProject;

/// <summary>
/// Handles first-person mouse look.
/// Horizontal rotation turns the player body; vertical rotation turns only the camera.
/// Attach this to the Player GameObject and assign the child Camera transform.
/// </summary>
public class PlayerLook : MonoBehaviour
{
    [Header("Look")]
    [SerializeField] private float _mouseSensitivity = 150f;
    [SerializeField] private Transform _playerCamera;

    private float xRotation;

    private void Awake()
    {
        if (_playerCamera == null)
        {
            Camera camera = GetComponentInChildren<Camera>();
            if (camera != null)
            {
                _playerCamera = camera.transform;
            }
        }
    }

    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void LateUpdate()
    {
        GameInput input = GameInput.Instance;
        Vector2 lookInput = input != null ? input.LookInput : Vector2.zero;
        float mouseX = lookInput.x * _mouseSensitivity * Time.deltaTime;
        float mouseY = lookInput.y * _mouseSensitivity * Time.deltaTime;

        // Horizontal look rotates the player body around the Y axis.
        transform.Rotate(Vector3.up * mouseX);

        // Vertical look rotates only the camera, clamped to prevent flipping.
        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);

        if (_playerCamera != null)
        {
            _playerCamera.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
        }
    }
}
