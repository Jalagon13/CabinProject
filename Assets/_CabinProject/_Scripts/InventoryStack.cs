using System;
using CabinProject;

namespace CabinProject
{
    [Serializable]
    public class InventoryStack
    {
        public CollectableData Collectable;
        public int Quantity;
        public bool HasItem => Collectable != null;

        public InventoryStack()
        {
            Collectable = null;
            Quantity = 0;
        }

        public InventoryStack(CollectableData itemSO, int quantity)
        {
            Collectable = itemSO;

            if (Collectable != null)
            {
                Quantity = quantity;
            }
        }

        public InventoryStack CreateIndependentCopy(int quantity)
        {
            InventoryStack copy = new InventoryStack(Collectable, quantity);

            return copy;
        }
    }
}