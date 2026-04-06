using TMPro;
using UnityEngine;

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
        
        public void InitializeAsSellRow()
        {
            
        }
        
        public void InitializeAsTotalRow()
        {
            
        }
    }
}
