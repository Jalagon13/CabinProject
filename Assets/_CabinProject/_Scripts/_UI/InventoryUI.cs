using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CabinProject
{
    public class InventoryUI : MonoBehaviour
    {
        [SerializeField] private ItemTextUI _itemTextUIPrefab;
        [SerializeField] private RectTransform _inventoryMenuRt;
        [SerializeField] private RectTransform _inventoryItemTextHolder;
        [SerializeField] private RectTransform _inventoryFullRt;
        [SerializeField] private TextMeshProUGUI _inventoryCapacityText;
        [SerializeField] private float _displayLength = 3f;
        
        private bool _inventoryOpen = false;
        public bool InventoryOpen => _inventoryOpen;
    
        private void Start()
        {
            HideInventoryMenu();
            _inventoryCapacityText.text = $"Total: {InventoryManager.Instance.Count} / {InventoryManager.Instance.Capacity}";
            _inventoryFullRt.gameObject.SetActive(false);
            InventoryManager.Instance.OnItemCollected += CheckIfInventoryFull;
            InventoryManager.Instance.OnItemCollected += UpdateUI;
            GameInput.Instance.OnInventoryToggle += OnInventoryToggle;

        }
        
        private void OnDestroy()
        {
            InventoryManager.Instance.OnItemCollected -= CheckIfInventoryFull;
            InventoryManager.Instance.OnItemCollected -= UpdateUI;
            GameInput.Instance.OnInventoryToggle -= OnInventoryToggle;
        }

        private void OnInventoryToggle(object sender, InputAction.CallbackContext e)
        {
            if (ShippingBinUI.Instance != null && ShippingBinUI.Instance.ConsumeInventoryToggleBlock())
            {
                return;
            }

            _inventoryOpen = !_inventoryOpen;
            
            if (_inventoryOpen)
            {
                ShowInventoryMenu();
            }
            else
            {
                HideInventoryMenu();
            }
        }

        private void UpdateUI(CollectableData data)
        {
            if (_inventoryItemTextHolder == null || _itemTextUIPrefab == null || InventoryManager.Instance == null)
            {
                return;
            }

            for (int i = _inventoryItemTextHolder.childCount - 1; i >= 0; i--)
            {
                Destroy(_inventoryItemTextHolder.GetChild(i).gameObject);
            }

            Dictionary<CollectableData, int> itemCounts = new();

            foreach (CollectableData item in InventoryManager.Instance.Items)
            {
                if (item == null)
                {
                    continue;
                }

                if (itemCounts.ContainsKey(item))
                {
                    itemCounts[item]++;
                    continue;
                }

                itemCounts[item] = 1;
            }

            foreach (KeyValuePair<CollectableData, int> itemCount in itemCounts)
            {
                ItemTextUI itemTextUI = Instantiate(_itemTextUIPrefab, _inventoryItemTextHolder);
                itemTextUI.Initialize(itemCount.Key, itemCount.Value);
            }

            _inventoryCapacityText.text = $"Total: {InventoryManager.Instance.Count} / {InventoryManager.Instance.Capacity}";
        }

        private void CheckIfInventoryFull(CollectableData data)
        {
            if(_inventoryFullRt.gameObject.activeInHierarchy || InventoryManager.Instance.RemainingCapacity > 0) return;
        
            StartCoroutine(DisplayInventoryFullMessage());
        }
        
        private IEnumerator DisplayInventoryFullMessage()
        {
            _inventoryFullRt.gameObject.SetActive(true);
            yield return new WaitForSeconds(_displayLength);
            _inventoryFullRt.gameObject.SetActive(false);
        }
        
        private void ShowInventoryMenu()
        {
            _inventoryMenuRt.gameObject.SetActive(true);
            Time.timeScale = 0f;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        private void HideInventoryMenu()
        {
            _inventoryMenuRt.gameObject.SetActive(false);
            Time.timeScale = 1f;
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }
    }
}
