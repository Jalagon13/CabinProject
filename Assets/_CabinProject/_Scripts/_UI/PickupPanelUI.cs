using System;
using TMPro;
using UnityEngine;
using DG.Tweening;
using UnityEngine.UI;

namespace CabinProject
{
    

    public class PickupPanelUI : MonoBehaviour
    {
        private static event Action<PickupPanelUI> OnPickupPanelCreated;
        private static event Action<PickupPanelUI> OnPickupPanelDestroyed;

        [Header("UI References")]
        [SerializeField] private TextMeshProUGUI _amountText;
        [SerializeField] private TextMeshProUGUI _nameText;
        [SerializeField] private Image _iconImage;
        [Header("Animation Settings")]
        [SerializeField] private float _disapearDelay = 3f;
        [SerializeField] private float _lerpDuration = 0.25f;
        [SerializeField] private float _fadeOutFraction = 0.25f;
        [Header("Custom Message Layout")]
        [SerializeField] private Vector2 _customNameAnchoredPosition = new Vector2(22f, 0f);

        private RectTransform _rectTransform;
        private RectTransform _nameRectTransform;
        private Tween _moveTween;
        private Tween _fadeTween;
        private float _currentTargetY;
        private CanvasGroup _canvasGroup;
        private Vector2 _defaultNameAnchoredPosition;

        public InventoryStack InventoryStack { get; private set; }

        private void InitializeVariables()
        {
            _rectTransform = GetComponent<RectTransform>();
            _canvasGroup = GetComponent<CanvasGroup>();
            _nameRectTransform = _nameText.rectTransform;
            _currentTargetY = _rectTransform.anchoredPosition.y;
            _defaultNameAnchoredPosition = _nameRectTransform.anchoredPosition;
            OnPickupPanelCreated += HandlePickupPanelCreated;
            OnPickupPanelDestroyed += HandlePickupPanelDestroyed;
        }

        private void OnDestroy()
        {
            OnPickupPanelDestroyed?.Invoke(this);
            _moveTween?.Kill();
            _fadeTween?.Kill();
            OnPickupPanelCreated -= HandlePickupPanelCreated;
            OnPickupPanelDestroyed -= HandlePickupPanelDestroyed;
        }

        public void Setup(InventoryStack item)
        {
            InventoryStack = item;
            InitializeVariables();
            ApplyStandardLayout();

            _iconImage.gameObject.SetActive(true);
            _iconImage.sprite = item.Collectable.ItemIcon;
            _nameText.text = item.Collectable.ItemName;
            _amountText.text = $"+{item.Quantity}";

            NotifyPanelCreated();
            StartLifetime();
        }

        public void SetupCustom(Sprite icon, string nameText, string amountText = "")
        {
            InventoryStack = null;
            InitializeVariables();
            ApplyCustomLayout();

            _iconImage.gameObject.SetActive(icon != null);
            _iconImage.sprite = icon;
            _nameText.text = nameText;
            _amountText.text = amountText;

            NotifyPanelCreated();
            StartLifetime();
        }

        private void StartLifetime()
        {
            float fadeDuration = _disapearDelay * _fadeOutFraction;
            float fadeStartTime = _disapearDelay - fadeDuration;

            _canvasGroup.alpha = 1f;

            _fadeTween = _canvasGroup
                .DOFade(0f, fadeDuration)
                .SetDelay(fadeStartTime)
                .SetEase(Ease.OutQuad)
                .SetLink(gameObject);

            Destroy(gameObject, _disapearDelay);
        }

        private void HandlePickupPanelCreated(PickupPanelUI createdPanel)
        {
            if (createdPanel == this)
            {
                return;
            }

            float newTargetY = _currentTargetY + _rectTransform.rect.height;
            MoveToY(newTargetY);
        }

        private void HandlePickupPanelDestroyed(PickupPanelUI destroyedPanel)
        {
            if (destroyedPanel == this || destroyedPanel == null) return;

            // If the destroyed panel was below us, we need to shift down to fill the gap.
            // Newer panels are at lower Y, older ones are at higher Y.
            if (destroyedPanel._currentTargetY < this._currentTargetY)
            {
                float newTargetY = _currentTargetY - destroyedPanel._rectTransform.rect.height;
                MoveToY(newTargetY);
            }
        }

        private void MoveToY(float newTargetY)
        {
            float remainingDuration = _lerpDuration;
            if (_moveTween != null && _moveTween.IsActive() && _moveTween.IsPlaying())
            {
                remainingDuration = Mathf.Max(0f, _lerpDuration - _moveTween.Elapsed());
                _moveTween.Kill();
            }

            _currentTargetY = newTargetY;
            _moveTween = _rectTransform
                .DOAnchorPosY(_currentTargetY, remainingDuration)
                .SetEase(Ease.OutQuad)
                .SetLink(gameObject);
        }

        private void NotifyPanelCreated()
        {
            OnPickupPanelCreated?.Invoke(this);
        }

        private void ApplyStandardLayout()
        {
            _amountText.gameObject.SetActive(true);
            _nameRectTransform.anchoredPosition = _defaultNameAnchoredPosition;
        }

        private void ApplyCustomLayout()
        {
            _amountText.gameObject.SetActive(false);
            _nameRectTransform.anchoredPosition = _customNameAnchoredPosition;
        }
    }
}
