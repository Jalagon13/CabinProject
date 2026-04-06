using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CabinProject
{
    public class ShippingBinUI : MonoBehaviour
    {
        [SerializeField] private RectTransform _shippingBinMenuRt;
        [SerializeField] private RectTransform _itemTextHolder;
        [SerializeField] private SellItemTextUI _sellItemTextUIPrefab;

        private bool _isOpen;
        public bool IsOpen => _isOpen;
        private bool _blockNextInventoryToggle;
        private int _totalSellValue;

        private void Awake()
        {
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

            GameInput.Instance.OnInventoryToggle += OnInventoryToggle;
            CrosshairManager.Instance.OnShippingBinInteracted += OnShippingBinInteracted;
            InventoryManager.Instance.OnItemCollected += UpdateUI;
            InventoryManager.Instance.OnInventoryCleared += UpdateUI;
        }

        private void OnDestroy()
        {
            InventoryManager.Instance.OnItemCollected -= UpdateUI;
            InventoryManager.Instance.OnInventoryCleared -= UpdateUI;
            GameInput.Instance.OnInventoryToggle -= OnInventoryToggle;
            CrosshairManager.Instance.OnShippingBinInteracted -= OnShippingBinInteracted;
        }
        
        public void OnSellButtonPressed() // Connected through the button
        {
            if(_totalSellValue <= 0)
            {
                return;
            }
        
            MoneyManager.Instance.AddMoney(_totalSellValue);
            InventoryManager.Instance.ClearInventory();

            HideShippingBinMenu();
        }

        private void OnShippingBinInteracted(ShippingBin shippingBin)
        {
            if (_isOpen)
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

        private void UpdateUI(CollectableData data)
        {
            _totalSellValue = 0;

            for (int i = _itemTextHolder.childCount - 1; i >= 0; i--)
            {
                Destroy(_itemTextHolder.GetChild(i).gameObject);
            }
            
            int totalSellValue = 0;

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
                SellItemTextUI sellItemTextUI = Instantiate(_sellItemTextUIPrefab, _itemTextHolder);
                sellItemTextUI.InitializeAsSellRow(itemCount.Key, itemCount.Value);
                totalSellValue += itemCount.Key.SellValue * itemCount.Value;
            }

            SellItemTextUI totalTextUI = Instantiate(_sellItemTextUIPrefab, _itemTextHolder);
            totalTextUI.InitializeAsTotalRow(totalSellValue);
            
            _totalSellValue = totalSellValue;
        }

        private void UpdateUI()
        {
            UpdateUI(null);
        }
    }
}
