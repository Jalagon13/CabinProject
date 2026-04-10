using UnityEngine;
using UnityEngine.InputSystem;

namespace CabinProject
{
    [DisallowMultipleComponent]
    public class DebrisPickupManager : MonoBehaviour
    {
        [Header("Input")]
        [SerializeField] private bool _useRightMouseHold = true;

        [Header("Anchor")]
        [SerializeField] private Camera _cameraOverride;
        [SerializeField] private float _anchorDistance = 1.65f;
        [SerializeField] private float _pickupRadius = 0.75f;

        [Header("Pull")]
        [SerializeField] private float _pullAcceleration = 55f;
        [SerializeField] private float _maxPullSpeed = 6.5f;
        [SerializeField] private float _velocityDamping = 8f;
        [SerializeField] private float _snapDistance = 0.1f;
        [SerializeField] private float _snapLerpSpeed = 14f;

        [Header("Filtering")]
        [SerializeField] private LayerMask _debrisLayers = ~0;
        [SerializeField] private QueryTriggerInteraction _triggerInteraction = QueryTriggerInteraction.Ignore;
        [SerializeField] private int _maxColliders = 256;

        private Collider[] _overlapResults;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureManagerInLocalManagers()
        {
            GameObject localManagers = GameObject.Find("LOCAL_MANAGERS");
            if (localManagers == null)
            {
                return;
            }

            if (localManagers.GetComponent<DebrisPickupManager>() == null)
            {
                localManagers.AddComponent<DebrisPickupManager>();
            }
        }

        private void Awake()
        {
            _anchorDistance = Mathf.Max(0.05f, _anchorDistance);
            _pickupRadius = Mathf.Max(0.05f, _pickupRadius);
            _pullAcceleration = Mathf.Max(0f, _pullAcceleration);
            _maxPullSpeed = Mathf.Max(0.1f, _maxPullSpeed);
            _velocityDamping = Mathf.Max(0f, _velocityDamping);
            _snapDistance = Mathf.Max(0.01f, _snapDistance);
            _snapLerpSpeed = Mathf.Max(0f, _snapLerpSpeed);
            _maxColliders = Mathf.Max(16, _maxColliders);
            _overlapResults = new Collider[_maxColliders];
        }

        private void FixedUpdate()
        {
            if (!IsPickupInputHeld())
            {
                return;
            }

            Camera activeCamera = _cameraOverride != null ? _cameraOverride : Camera.main;
            if (activeCamera == null)
            {
                return;
            }

            Vector3 anchorPoint = activeCamera.transform.position + (activeCamera.transform.forward * _anchorDistance);
            int hitCount = Physics.OverlapSphereNonAlloc(
                anchorPoint,
                _pickupRadius,
                _overlapResults,
                _debrisLayers,
                _triggerInteraction);

            for (int i = 0; i < hitCount; i++)
            {
                Collider overlap = _overlapResults[i];
                if (overlap == null)
                {
                    continue;
                }

                ExcavationDebrisPiece debrisPiece = overlap.GetComponentInParent<ExcavationDebrisPiece>();
                if (debrisPiece == null)
                {
                    continue;
                }

                Rigidbody rigidbody = overlap.attachedRigidbody;
                if (rigidbody == null)
                {
                    rigidbody = debrisPiece.GetComponent<Rigidbody>();
                }

                if (rigidbody == null)
                {
                    continue;
                }

                if (rigidbody.isKinematic)
                {
                    debrisPiece.Activate(Vector3.zero);
                }

                PullRigidbodyToAnchor(rigidbody, anchorPoint);
            }
        }

        private bool IsPickupInputHeld()
        {
            if (!_useRightMouseHold)
            {
                return false;
            }

            Mouse mouse = Mouse.current;
            return mouse != null && mouse.rightButton.isPressed;
        }

        private void PullRigidbodyToAnchor(Rigidbody rigidbody, Vector3 anchorPoint)
        {
            if (rigidbody == null || rigidbody.isKinematic)
            {
                return;
            }

            Vector3 toAnchor = anchorPoint - rigidbody.worldCenterOfMass;
            float distance = toAnchor.magnitude;
            if (distance <= 0.0001f)
            {
                return;
            }

            Vector3 direction = toAnchor / distance;
            float radius = Mathf.Max(_pickupRadius, 0.0001f);
            float falloff = Mathf.Clamp01(1f - (distance / radius));

            rigidbody.AddForce(direction * (_pullAcceleration * falloff), ForceMode.Acceleration);

            Vector3 desiredVelocity = direction * Mathf.Min(_maxPullSpeed, distance * _maxPullSpeed);
            float dampingT = 1f - Mathf.Exp(-_velocityDamping * Time.fixedDeltaTime);
            rigidbody.linearVelocity = Vector3.Lerp(rigidbody.linearVelocity, desiredVelocity, dampingT);

            if (distance <= _snapDistance)
            {
                float snapT = 1f - Mathf.Exp(-_snapLerpSpeed * Time.fixedDeltaTime);
                Vector3 snappedPosition = Vector3.Lerp(rigidbody.position, anchorPoint, snapT);
                rigidbody.MovePosition(snappedPosition);
            }
        }
    }
}
