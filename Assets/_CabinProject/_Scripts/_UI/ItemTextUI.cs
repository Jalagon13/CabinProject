using TMPro;
using UnityEngine;

namespace CabinProject
{
    public class ItemTextUI : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI _nameText;
        [SerializeField] private TextMeshProUGUI _xText;
        [SerializeField] private TextMeshProUGUI _amountText;
        
        public void Initialize(CollectableData data, int amount)
        {
            _nameText.text = data.ItemName;
            _amountText.text = $"{amount}";
        }
    }
}
