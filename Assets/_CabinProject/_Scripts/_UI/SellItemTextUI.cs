using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CabinProject
{
    public class SellItemTextUI : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI _nameText;
        [SerializeField] private TextMeshProUGUI _amountText;
        [SerializeField] private TextMeshProUGUI _xText;
        [SerializeField] private TextMeshProUGUI _sellValueText;
        [SerializeField] private TextMeshProUGUI _equalsText;
        [SerializeField] private TextMeshProUGUI _stackSellValueText;
        [SerializeField] private Image _seperatorImage;
        
        public void InitializeAsSellRow(CollectableData data, int amount)
        {
            _nameText.text = data.ItemName;
            _amountText.text = $"{amount}";
            _sellValueText.text = $"${data.SellValue}";
            _stackSellValueText.text = $"${data.SellValue * amount}";
            _seperatorImage.enabled = false;
        }
        
        public void InitializeAsTotalRow(int totalSellValue)
        {
            _nameText.text = $"Total";
            _amountText.text = "";
            _xText.text = "";
            _sellValueText.text = "";
            _stackSellValueText.text = $"${totalSellValue}";
            _seperatorImage.enabled = true;
        }
    }
}
