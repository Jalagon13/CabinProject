using System;
using System.Collections.Generic;
using UnityEngine;

namespace CabinProject
{
    public class PickupPanelHandler : MonoBehaviour
    {
        [SerializeField] private PickupPanelUI _pickupPanelUIPrefab;
        [SerializeField] private float _itemPickupSFXCooldown = 0.2f;

        private List<PickupPanelUI> _activePanels = new List<PickupPanelUI>();
        private Timer _itemPickupSFXTimer;
        
        private void Awake()
        {
            _itemPickupSFXTimer = new Timer(_itemPickupSFXCooldown);
        }

        private void Start()
        {
            InventoryManager.Instance.OnItemCollected += InventoryManager_OnItemCollected;
        }
        
        private void OnDestroy()
        {
            InventoryManager.Instance.OnItemCollected -= InventoryManager_OnItemCollected;
        }

        private void Update()
        {
            _itemPickupSFXTimer.Tick(Time.deltaTime);
        }

        private void InventoryManager_OnItemCollected(CollectableData data)
        {
            InventoryStack stack = new InventoryStack(data, 1);
        
            // Remove any references to panels that have already been destroyed/expired
            _activePanels.RemoveAll(p => p == null);

            // Check if we already have a panel active for this specific ItemSO
            PickupPanelUI existingPanel = _activePanels.Find(p => p.InventoryStack != null && p.InventoryStack.Collectable == stack.Collectable);
            int totalAmount = stack.Quantity;

            if (existingPanel != null)
            {
                totalAmount += existingPanel.InventoryStack.Quantity;
                _activePanels.Remove(existingPanel);
                Destroy(existingPanel.gameObject);
            }

            PickupPanelUI newPanel = CreatePanel();
            newPanel.Setup(stack.CreateIndependentCopy(totalAmount));
            _activePanels.Add(newPanel);

            if (_itemPickupSFXTimer.RemainingSeconds <= 0f)
            {
                AudioManager.Instance.PlayOneShot(FMODEvents.Instance.ItemPickupSFX, transform.position);
                _itemPickupSFXTimer.Reset();
            }
        }

        private PickupPanelUI CreatePanel()
        {
            return Instantiate(_pickupPanelUIPrefab.gameObject, transform).GetComponent<PickupPanelUI>();
        }
    }
}
