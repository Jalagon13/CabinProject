using UnityEngine;
using System;

namespace CabinProject
{
    public class CrosshairManager : MonoBehaviour
    {
        public static CrosshairManager Instance { get; private set; }

        [Header("Attack Settings")]
        [SerializeField] private float _interactRange = 3.5f;
        [SerializeField] private float _attackCooldown = 0.2f;
        [SerializeField] private string _collectableLayerName = "Collectable";
        
        [Header("Destruction Settings")]
        [SerializeField] private float _excavationRadius = 0.5f;
        public float ExcavationRadius => _excavationRadius;
        
        [SerializeField] private int _gridResolution = 32;
        public int GridResolution => _gridResolution;
        
        [SerializeField] private float _isoValue = 0.5f;
        public float IsoValue => _isoValue;
        
        [SerializeField] private ComputeShader _computeShader;
        public ComputeShader ComputeShader => _computeShader;

        
        private Timer _attackTimer;

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            _attackTimer = new Timer(_attackCooldown);
            _attackTimer.RemainingSeconds = 0f;
        }

        private void Update()
        {
            if (_attackTimer == null)
            {
                return;
            }

            _attackTimer.Tick(Time.deltaTime);

            if (GameInput.Instance == null || !GameInput.Instance.IsHoldingDownAttack)
            {
                return;
            }

            if (_attackTimer.RemainingSeconds <= 0f)
            {
                HandleAttack();
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void HandleAttack()
        {
            if (_attackTimer != null && _attackTimer.RemainingSeconds > 0f)
            {
                return;
            }

            Camera mainCamera = Camera.main;
            if (mainCamera == null)
            {
                Debug.LogWarning("CrosshairManager could not fire attack raycast because no Main Camera was found.");
                _attackTimer?.Reset();
                return;
            }

            Ray ray = new Ray(mainCamera.transform.position, mainCamera.transform.forward);

            RaycastHit[] hits = Physics.RaycastAll(ray, _interactRange, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

            if (hits.Length > 0)
            {
                foreach (RaycastHit hit in hits)
                {
                    if (TryCollect(hit))
                    {
                        _attackTimer?.Reset();
                        return;
                    }

                    MarchingCubesVoxelDestruction voxelTarget = hit.collider.GetComponentInParent<MarchingCubesVoxelDestruction>();
                    if (voxelTarget != null)
                    {
                        voxelTarget.Excavate(hit.point);
                        // Debug.Log($"Excavated {voxelTarget.name} at {hit.point}.");
                        _attackTimer?.Reset();
                        return;
                    }
                }

                // Debug.Log($"Attack raycast hit {hits[0].collider.gameObject.name} at distance {hits[0].distance:F2}.");
            }
            else
            {
                // Debug.Log("Attack raycast did not hit anything.");
            }

            _attackTimer?.Reset();
        }

        private bool TryCollect(RaycastHit hit)
        {
            if (!hit.collider.gameObject.layer.Equals(LayerMask.NameToLayer(_collectableLayerName)))
            {
                return false;
            }

            Collectable collectable = hit.collider.GetComponentInParent<Collectable>();
            if (collectable == null)
            {
                return false;
            }

            if (!collectable.CanBeCollected)
            {
                Debug.LogWarning($"Collectable on {collectable.name} is missing data.");
                return true;
            }

            if (!InventoryManager.Instance.TryAddCollectable(collectable.Data))
            {
                Debug.Log($"Inventory full. Could not collect {collectable.Data.ItemName}.");
                return true;
            }

            Debug.Log($"Collected {collectable.Data.ItemName}.");
            Destroy(collectable.gameObject);
            return true;
        }
    }
}
