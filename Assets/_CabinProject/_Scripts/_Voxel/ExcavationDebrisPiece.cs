using UnityEngine;

namespace CabinProject
{
    public class ExcavationDebrisPiece : MonoBehaviour
    {
        private float _lifetime;
        private float _shrinkStartNormalized;
        private float _elapsed;
        private Vector3 _initialScale = Vector3.one;
        private Rigidbody _rigidbody;
        private float _settleDelay;
        private float _settleLinearVelocityThreshold;
        private float _settleAngularVelocityThreshold;
        private float _settleCheckDuration;
        private float _settleTimer;
        private bool _settled;

        public void Initialize(
            float lifetime,
            float shrinkStartNormalized,
            Rigidbody rigidbody,
            float settleDelay,
            float settleLinearVelocityThreshold,
            float settleAngularVelocityThreshold,
            float settleCheckDuration)
        {
            _lifetime = Mathf.Max(0.01f, lifetime);
            _shrinkStartNormalized = Mathf.Clamp01(shrinkStartNormalized);
            _initialScale = transform.localScale;
            _rigidbody = rigidbody;
            _settleDelay = Mathf.Max(0f, settleDelay);
            _settleLinearVelocityThreshold = Mathf.Max(0f, settleLinearVelocityThreshold);
            _settleAngularVelocityThreshold = Mathf.Max(0f, settleAngularVelocityThreshold);
            _settleCheckDuration = Mathf.Max(0.01f, settleCheckDuration);
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            if (_elapsed >= _lifetime)
            {
                Destroy(gameObject);
                return;
            }

            TrySettleRigidBody();

            float shrinkStartTime = _lifetime * _shrinkStartNormalized;
            if (_elapsed < shrinkStartTime)
            {
                return;
            }

            float shrinkDuration = Mathf.Max(0.0001f, _lifetime - shrinkStartTime);
            float shrinkT = Mathf.Clamp01((_elapsed - shrinkStartTime) / shrinkDuration);
            transform.localScale = Vector3.Lerp(_initialScale, Vector3.zero, shrinkT);
        }

        private void TrySettleRigidBody()
        {
            if (_settled || _rigidbody == null || _elapsed < _settleDelay)
            {
                return;
            }

            bool belowLinearThreshold = _rigidbody.linearVelocity.sqrMagnitude <= (_settleLinearVelocityThreshold * _settleLinearVelocityThreshold);
            bool belowAngularThreshold = _rigidbody.angularVelocity.sqrMagnitude <= (_settleAngularVelocityThreshold * _settleAngularVelocityThreshold);

            if (belowLinearThreshold && belowAngularThreshold)
            {
                _settleTimer += Time.deltaTime;
            }
            else
            {
                _settleTimer = 0f;
            }

            if (_settleTimer < _settleCheckDuration)
            {
                return;
            }

            _rigidbody.linearVelocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
            _rigidbody.Sleep();
            _settled = true;
        }

        private void OnDestroy()
        {
            MeshFilter meshFilter = GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                Destroy(meshFilter.sharedMesh);
            }
        }
    }
}
