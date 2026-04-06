using System;
using UnityEngine;

namespace CabinProject
{
    public class MoneyManager : MonoBehaviour
    {
        public static MoneyManager Instance { get; private set; }

        public event Action<int> OnMoneyUpdated;
        
        private int _currentMoney;
        public int CurrentMoney => _currentMoney;
        

        private void Awake()
        {
            Instance = this;
        }
        
        public void AddMoney(int amount)
        {
            _currentMoney += amount;
            OnMoneyUpdated?.Invoke(amount);
        }
        
        public void SubtractMoney(int amount)
        {
            _currentMoney -= amount;
            if (_currentMoney < 0)
            {
                _currentMoney = 0;
            }
            
            OnMoneyUpdated?.Invoke(-amount);
        }
        
        public void SetMoney(int amount)
        {
            _currentMoney = amount;
            if (_currentMoney < 0)
            {
                _currentMoney = 0;
            }
            
            OnMoneyUpdated?.Invoke(amount);
        }
        
    }
}
