using UnityEngine;

namespace CabinProject
{
    public class ExcavationDebrisPiece : MonoBehaviour
    {
        private float _lifetime;
        private float _shrinkStartNormalized;
        private float _elapsed;
        private Vector3 _initialScale = Vector3.one;

        public void Initialize(float lifetime, float shrinkStartNormalized)
        {
            _lifetime = Mathf.Max(0.01f, lifetime);
            _shrinkStartNormalized = Mathf.Clamp01(shrinkStartNormalized);
            _initialScale = transform.localScale;
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            if (_elapsed >= _lifetime)
            {
                Destroy(gameObject);
                return;
            }

            float shrinkStartTime = _lifetime * _shrinkStartNormalized;
            if (_elapsed < shrinkStartTime)
            {
                return;
            }

            float shrinkDuration = Mathf.Max(0.0001f, _lifetime - shrinkStartTime);
            float shrinkT = Mathf.Clamp01((_elapsed - shrinkStartTime) / shrinkDuration);
            transform.localScale = Vector3.Lerp(_initialScale, Vector3.zero, shrinkT);
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
