using System.Collections.Generic;
using UnityEngine;

namespace CabinProject
{
    public class ExcavationDebrisPiece : MonoBehaviour
    {
        private const string ImpactStrengthParameter = "ImpactStrength";
        private const string DebrisSizeParameter = "DebrisSize";
        private static float _lastGlobalAudioPlayTime = float.NegativeInfinity;

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
        private bool _collisionAudioEnabled;
        private float _audioSpawnGraceTime;
        private float _audioMinimumImpactSpeed;
        private float _audioMinimumImpactImpulse;
        private float _audioHeavyImpactImpulse;
        private float _audioInitialSilenceDuration;
        private float _audioRetriggerCooldown;
        private float _audioGlobalRetriggerCooldown;
        private float _audioStrongerHitRetriggerMargin;
        private float _audioMaxVolumeMultiplier;
        private float _audioDebrisSizeNormalized;
        private float _lastImpactStrengthNormalized;
        private float _lastAudioPlayTime = float.NegativeInfinity;
        private bool _settled;
        private readonly List<(string Name, float Value)> _audioParameters = new List<(string Name, float Value)>(2);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticAudioState()
        {
            _lastGlobalAudioPlayTime = float.NegativeInfinity;
        }

        public void Initialize(
            float lifetime,
            float shrinkStartNormalized,
            Rigidbody rigidbody,
            float settleDelay,
            float settleLinearVelocityThreshold,
            float settleAngularVelocityThreshold,
            float settleCheckDuration,
            bool collisionAudioEnabled,
            float audioSpawnGraceTime,
            float audioMinimumImpactSpeed,
            float audioMinimumImpactImpulse,
            float audioHeavyImpactImpulse,
            float audioInitialSilenceDuration,
            float audioRetriggerCooldown,
            float audioGlobalRetriggerCooldown,
            float audioStrongerHitRetriggerMargin,
            float audioMaxVolumeMultiplier,
            float audioDebrisSizeNormalized)
        {
            _lifetime = Mathf.Max(0.01f, lifetime);
            _shrinkStartNormalized = Mathf.Clamp01(shrinkStartNormalized);
            _initialScale = transform.localScale;
            _rigidbody = rigidbody;
            _settleDelay = Mathf.Max(0f, settleDelay);
            _settleLinearVelocityThreshold = Mathf.Max(0f, settleLinearVelocityThreshold);
            _settleAngularVelocityThreshold = Mathf.Max(0f, settleAngularVelocityThreshold);
            _settleCheckDuration = Mathf.Max(0.01f, settleCheckDuration);
            _collisionAudioEnabled = collisionAudioEnabled;
            _audioSpawnGraceTime = Mathf.Max(0f, audioSpawnGraceTime);
            _audioMinimumImpactSpeed = Mathf.Max(0f, audioMinimumImpactSpeed);
            _audioMinimumImpactImpulse = Mathf.Max(0f, audioMinimumImpactImpulse);
            _audioHeavyImpactImpulse = Mathf.Max(_audioMinimumImpactImpulse + 0.0001f, audioHeavyImpactImpulse);
            _audioInitialSilenceDuration = Mathf.Max(0f, audioInitialSilenceDuration);
            _audioRetriggerCooldown = Mathf.Max(0f, audioRetriggerCooldown);
            _audioGlobalRetriggerCooldown = Mathf.Max(0f, audioGlobalRetriggerCooldown);
            _audioStrongerHitRetriggerMargin = Mathf.Max(0f, audioStrongerHitRetriggerMargin);
            _audioMaxVolumeMultiplier = Mathf.Max(0f, audioMaxVolumeMultiplier);
            _audioDebrisSizeNormalized = Mathf.Clamp01(audioDebrisSizeNormalized);
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

        private void OnCollisionEnter(Collision collision)
        {
            if (!_collisionAudioEnabled || _settled || _elapsed < _audioSpawnGraceTime || _elapsed < _audioInitialSilenceDuration)
            {
                return;
            }

            float relativeSpeed = collision.relativeVelocity.magnitude;
            if (relativeSpeed < _audioMinimumImpactSpeed)
            {
                return;
            }

            int contactCount = collision.contactCount;
            float strongestNormalImpulse = contactCount > 0
                ? collision.impulse.magnitude / contactCount
                : collision.impulse.magnitude;

            if (strongestNormalImpulse < _audioMinimumImpactImpulse && relativeSpeed < _audioMinimumImpactSpeed)
            {
                return;
            }

            float impactStrengthNormalized = strongestNormalImpulse >= _audioMinimumImpactImpulse
                ? Mathf.InverseLerp(_audioMinimumImpactImpulse, _audioHeavyImpactImpulse, strongestNormalImpulse)
                : Mathf.InverseLerp(_audioMinimumImpactSpeed, _audioMinimumImpactSpeed * 3f, relativeSpeed);
            impactStrengthNormalized = Mathf.Clamp01(impactStrengthNormalized);

            if (impactStrengthNormalized <= 0f)
            {
                return;
            }

            float timeSinceLastPlay = Time.time - _lastAudioPlayTime;
            if (timeSinceLastPlay < _audioRetriggerCooldown
                && impactStrengthNormalized < (_lastImpactStrengthNormalized + _audioStrongerHitRetriggerMargin))
            {
                return;
            }

            float timeSinceGlobalPlay = Time.time - _lastGlobalAudioPlayTime;
            if (Time.time < _lastGlobalAudioPlayTime)
            {
                _lastGlobalAudioPlayTime = float.NegativeInfinity;
                timeSinceGlobalPlay = float.PositiveInfinity;
            }

            if (timeSinceGlobalPlay < _audioGlobalRetriggerCooldown)
            {
                return;
            }

            ContactPoint primaryContact = contactCount > 0 ? collision.GetContact(0) : default;
            Vector3 playPosition = contactCount > 0 ? primaryContact.point : transform.position;
            float volume = Mathf.Lerp(0.2f, _audioMaxVolumeMultiplier, impactStrengthNormalized);

            _audioParameters.Clear();
            _audioParameters.Add((ImpactStrengthParameter, impactStrengthNormalized));
            _audioParameters.Add((DebrisSizeParameter, _audioDebrisSizeNormalized));

            AudioManager.Instance.PlayOneShotWithParameters(
                FMODEvents.Instance.DebrisCollisionSFX,
                playPosition,
                _audioParameters,
                volume);

            _lastAudioPlayTime = Time.time;
            _lastGlobalAudioPlayTime = Time.time;
            _lastImpactStrengthNormalized = impactStrengthNormalized;
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
