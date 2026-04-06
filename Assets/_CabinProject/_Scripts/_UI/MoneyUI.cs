using System;
using TMPro;
using UnityEngine;

namespace CabinProject
{
    public class MoneyUI : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI _moneyText;
        
        private void Start()
        {
            MoneyManager.Instance.OnMoneyUpdated += OnMoneyUpdated;
        }
        
        private void OnDestroy()
        {
            MoneyManager.Instance.OnMoneyUpdated -= OnMoneyUpdated;
        }

        private void OnMoneyUpdated(int currentMoney)
        {
            _moneyText.text = $"${currentMoney}";
        }
    }
}
