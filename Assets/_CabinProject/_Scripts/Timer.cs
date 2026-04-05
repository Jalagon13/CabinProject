using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;


namespace CabinProject
{
    public class Timer
    {
        public event EventHandler OnTimerEnd;
        public bool IsPaused = false;

        private float _remainingSeconds;
        private readonly float _duration;

        public float PercentRemaining => _duration > 0 ? 1 - (_remainingSeconds / _duration) : 0;

        public float RemainingSeconds
        {
            get { return _remainingSeconds; }
            set
            {
                value = Mathf.Max(value, 0f);
                _remainingSeconds = value;
            }
        }

        public float Duration
        {
            get { return _duration; }
        }

        public void AddTime(float time)
        {
            _remainingSeconds += time;
        }

        public void SubtractTime(float time)
        {
            _remainingSeconds = Mathf.Max(0f, _remainingSeconds - time);
            CheckForTimerEnd();
        }

        public void Reset()
        {
            _remainingSeconds = _duration;
        }

        public Timer(float duration)
        {
            _duration = duration;
            _remainingSeconds = duration;
            IsPaused = false;
        }

        public void Tick(float deltaTime)
        {
            if (_remainingSeconds <= 0f || IsPaused) return;

            _remainingSeconds -= deltaTime;

            CheckForTimerEnd();
        }

        private void CheckForTimerEnd()
        {
            if (_remainingSeconds > 0f) return;

            _remainingSeconds = 0f;

            OnTimerEnd?.Invoke(this, EventArgs.Empty);
        }

        public float GetPercentComplete()
        {
            if (_duration <= 0) return 0f;
            return 1f - (_remainingSeconds / _duration);
        }
    }
}