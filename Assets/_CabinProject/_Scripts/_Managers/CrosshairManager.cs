using UnityEngine;
using UnityEngine.InputSystem;

namespace CabinProject
{
    public class CrosshairManager : MonoBehaviour
    {
        public static CrosshairManager Instance { get; private set; }

        [SerializeField] private float _interactRange = 3.5f;

        private void Awake()
        {
            Instance = this;
        }
        
        private void Start()
        {
            GameInput.Instance.OnAttack += HandleAttack;
        }
        
        private void OnDestroy()
        {
            GameInput.Instance.OnAttack -= HandleAttack;
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
                Debug.Log($"Attack raycast hit {hit.collider.gameObject.name} at distance {hit.distance:F2}.");
                return;
            }

            Debug.Log("Attack raycast did not hit anything.");
        }
    }
}
