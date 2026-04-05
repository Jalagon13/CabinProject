using UnityEngine;
using UnityEngine.InputSystem;

namespace CabinProject
{
    public class CrosshairManager : MonoBehaviour
    {
        public static CrosshairManager Instance { get; private set; }

        [SerializeField] private float _interactRange = 3.5f;
        
        private bool _isSubscribed;

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            TrySubscribeToInput();
        }

        private void OnEnable()
        {
            TrySubscribeToInput();
        }

        private void OnDisable()
        {
            UnsubscribeFromInput();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            UnsubscribeFromInput();
        }

        private void HandleAttack(object sender, InputAction.CallbackContext context)
        {
            Camera mainCamera = Camera.main;
            if (mainCamera == null)
            {
                Debug.LogWarning("CrosshairManager could not fire attack raycast because no Main Camera was found.");
                return;
            }

            Ray ray = new Ray(mainCamera.transform.position, mainCamera.transform.forward);

            if (Physics.Raycast(ray, out RaycastHit hit, _interactRange))
            {
                MarchingCubesVoxelDestruction voxelTarget = hit.collider.GetComponentInParent<MarchingCubesVoxelDestruction>();
                if (voxelTarget != null)
                {
                    voxelTarget.Excavate(hit.point);
                    Debug.Log($"Excavated {voxelTarget.name} at {hit.point}.");
                    return;
                }

                Debug.Log($"Attack raycast hit {hit.collider.gameObject.name} at distance {hit.distance:F2}.");
                return;
            }

            Debug.Log("Attack raycast did not hit anything.");
        }

        private void TrySubscribeToInput()
        {
            if (_isSubscribed || GameInput.Instance == null)
            {
                return;
            }

            GameInput.Instance.OnAttack += HandleAttack;
            _isSubscribed = true;
        }

        private void UnsubscribeFromInput()
        {
            if (!_isSubscribed || GameInput.Instance == null)
            {
                return;
            }

            GameInput.Instance.OnAttack -= HandleAttack;
            _isSubscribed = false;
        }
    }
}
