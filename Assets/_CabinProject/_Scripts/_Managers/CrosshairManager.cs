using UnityEngine;

namespace CabinProject
{
    public class CrosshairManager : MonoBehaviour
    {
        public static CrosshairManager Instance { get; private set; }

        [Header("Attack Settings")]
        [SerializeField] private float _interactRange = 3.5f;
        [SerializeField] private float _attackCooldown = 0.2f;
        
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

            if (Physics.Raycast(ray, out RaycastHit hit, _interactRange))
            {
                MarchingCubesVoxelDestruction voxelTarget = hit.collider.GetComponentInParent<MarchingCubesVoxelDestruction>();
                if (voxelTarget != null)
                {
                    voxelTarget.Excavate(hit.point);
                    Debug.Log($"Excavated {voxelTarget.name} at {hit.point}.");
                }
                else
                {
                    Debug.Log($"Attack raycast hit {hit.collider.gameObject.name} at distance {hit.distance:F2}.");
                }
            }
            else
            {
                Debug.Log("Attack raycast did not hit anything.");
            }

            _attackTimer?.Reset();
        }

    }
}
