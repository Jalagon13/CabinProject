using UnityEngine;

namespace CabinProject
{
    [CreateAssetMenu(fileName = "Collectable_", menuName = "Cabin Project/Collectable")]
    public class CollectableData : ScriptableObject
    {
        [SerializeField] private string _itemName;
        [SerializeField] private int _sellValue;
        [SerializeField] private Sprite _itemIcon;

        public string ItemName => _itemName;
        public int SellValue => _sellValue;
        public Sprite ItemIcon => _itemIcon;
    }
}
