using UnityEngine;

namespace CabinProject
{
    public class Collectable : MonoBehaviour
    {
        [SerializeField] private CollectableData _data;

        public CollectableData Data => _data;

        public bool CanBeCollected => _data != null;
    }
}
