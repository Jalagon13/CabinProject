using FMODUnity;
using UnityEngine;

namespace CabinProject
{
    public class FMODEvents : MonoBehaviour
    {
        public static FMODEvents Instance { get; private set; }

        [field: Header("Player SFX")]
        [field: SerializeField] public EventReference JumpSFX { get; private set; }
        [field: SerializeField] public EventReference LandingSFX { get; private set; }
        [field: SerializeField] public EventReference StepsSFX { get; private set; } 
        
        [field: Header("Item SFX")]
        [field: SerializeField] public EventReference ItemPickupSFX { get; private set; }

        [field: Header("Mining SFX")]
        [field: SerializeField] public EventReference StoneHitSFX { get; private set; }
        [field: SerializeField] public EventReference DebrisCollisionSFX { get; private set; }
        
        

        private void Awake()
        {
            Instance = this;
        }
    }
}
