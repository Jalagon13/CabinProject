using UnityEngine;
using System.Collections.Generic;

namespace CabinProject
{
    public class InventoryManager : MonoBehaviour
    {
        public static InventoryManager Instance { get; private set; }

        [Header("Inventory Settings")]
        [SerializeField] private int _capacity = 3;

        private readonly List<CollectableData> _items = new();

        public int Capacity => _capacity;
        public int Count => _items.Count;
        public int RemainingCapacity => Mathf.Max(0, _capacity - _items.Count);
        public IReadOnlyList<CollectableData> Items => _items;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public bool TryAddCollectable(CollectableData collectableData)
        {
            if (collectableData == null)
            {
                Debug.LogWarning("InventoryManager received a null collectable.");
                return false;
            }

            if (_items.Count >= _capacity)
            {
                return false;
            }
            
            _items.Add(collectableData);

            Debug.Log($"Adding {collectableData.ItemName} to inventory. Capacity remaining: {RemainingCapacity} / {_capacity}.");
            return true;
        }

        public void ExpandInventory(int additionalSlots)
        {
            if (additionalSlots <= 0)
            {
                Debug.LogWarning("ExpandInventory requires a value greater than 0.");
                return;
            }

            Debug.Log($"Expanding inventory by {additionalSlots} slots.");
            _capacity += additionalSlots;
        }
    }
}
