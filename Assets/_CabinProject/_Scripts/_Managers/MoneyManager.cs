using UnityEngine;

namespace CabinProject
{
    public class MoneyManager : MonoBehaviour
    {
        public static MoneyManager Instance { get; private set; }
        
        private void Awake()
        {
            Instance = this;
        }
        
        
    }
}
