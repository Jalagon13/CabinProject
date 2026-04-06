using UnityEngine;
using UnityEngine.InputSystem;

namespace CabinProject
{
    public class ShippingBinUI : MonoBehaviour
    {
        public static ShippingBinUI Instance { get; private set; }

        [SerializeField] private RectTransform _shippingBinMenuRt;

        private bool _isOpen;
        private bool _blockNextInventoryToggle;
        public bool IsOpen => _isOpen;

        private void Awake()
        {
            Instance = this;

            if (_shippingBinMenuRt == null)
            {
                if (transform.childCount > 0)
                {
                    _shippingBinMenuRt = transform.GetChild(0).GetComponent<RectTransform>();
                }
                else
                {
                    _shippingBinMenuRt = GetComponent<RectTransform>();
                }
            }
        }

        private void Start()
        {
            HideShippingBinMenu();

            if (GameInput.Instance != null)
            {
                GameInput.Instance.OnInteract += OnInteract;
                GameInput.Instance.OnInventoryToggle += OnInventoryToggle;
            }
        }

        private void OnDestroy()
        {
            if (GameInput.Instance != null)
            {
                GameInput.Instance.OnInteract -= OnInteract;
                GameInput.Instance.OnInventoryToggle -= OnInventoryToggle;
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnInteract(object sender, InputAction.CallbackContext context)
        {
            if (_isOpen || !IsLookingAtShippingBin())
            {
                return;
            }

            ShowShippingBinMenu();
        }

        private void OnInventoryToggle(object sender, InputAction.CallbackContext context)
        {
            if (!_isOpen)
            {
                return;
            }

            _blockNextInventoryToggle = true;
            HideShippingBinMenu();
        }

        public bool ConsumeInventoryToggleBlock()
        {
            if (_isOpen)
            {
                return true;
            }

            if (!_blockNextInventoryToggle)
            {
                return false;
            }

            _blockNextInventoryToggle = false;
            return true;
        }

        private bool IsLookingAtShippingBin()
        {
            Camera mainCamera = Camera.main;
            if (mainCamera == null)
            {
                return false;
            }

            float interactRange = CrosshairManager.Instance != null ? CrosshairManager.Instance.InteractRange : 3.5f;
            Ray ray = new Ray(mainCamera.transform.position, mainCamera.transform.forward);

            if (!Physics.Raycast(ray, out RaycastHit hit, interactRange, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
            {
                return false;
            }

            return hit.collider.GetComponentInParent<ShippingBin>() != null;
        }

        private void ShowShippingBinMenu()
        {
            _isOpen = true;

            if (_shippingBinMenuRt != null)
            {
                _shippingBinMenuRt.gameObject.SetActive(true);
            }

            Time.timeScale = 0f;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        private void HideShippingBinMenu()
        {
            _isOpen = false;

            if (_shippingBinMenuRt != null)
            {
                _shippingBinMenuRt.gameObject.SetActive(false);
            }

            Time.timeScale = 1f;
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }
    }
}
