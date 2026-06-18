using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Greenside
{
    /// <summary>
    /// The signature mechanic. Captures a down-then-up swipe (positions + times)
    /// and converts it into:
    ///   power01    : 0..1 from the up-stroke speed.
    ///   signedCurve: -1..+1 from lateral straightness. + = drift right = slice,
    ///                - = drift left = hook (right-handed convention).
    ///
    /// Uses the Input System with Enhanced Touch on device, and a mouse-drag
    /// fallback so the gesture can be tested in the Editor Game view.
    /// </summary>
    public class SwingInput : MonoBehaviour
    {
        public SwingTuning tuning;

        /// <summary>Fired on release with (power01, signedCurve).</summary>
        public event Action<float, float> OnSwing;

        /// <summary>True while a swipe is in progress (for HUD feedback).</summary>
        public bool IsSwiping { get; private set; }

        /// <summary>0..1 length of the current up-stroke (live power-meter feedback).</summary>
        public float LiveUpStroke { get; private set; }

        private struct Sample
        {
            public Vector2 pos;
            public float time;
        }

        private readonly List<Sample> _buffer = new List<Sample>(256);
        private float _runningMinY;
        private bool _wasTouching;

        private void OnEnable()
        {
            EnhancedTouchSupport.Enable();
        }

        private void OnDisable()
        {
            EnhancedTouchSupport.Disable();
        }

        private void Update()
        {
            if (ReadTouch()) return;
            ReadMouse();
        }

        // Returns true if a touch was active or just ended this frame (so the
        // mouse path is skipped). Tracks the active-touch count edge for release.
        private bool ReadTouch()
        {
            var touches = ETouch.activeTouches;
            if (touches.Count > 0)
            {
                Vector2 p = touches[0].screenPosition;
                if (!IsSwiping) Begin(p);
                else AddSample(p);
                _wasTouching = true;
                return true;
            }

            if (_wasTouching)
            {
                _wasTouching = false;
                if (IsSwiping) End();
                return true;
            }
            return false;
        }

        private void ReadMouse()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            if (mouse.leftButton.wasPressedThisFrame)
            {
                Begin(mouse.position.ReadValue());
            }
            else if (IsSwiping && mouse.leftButton.isPressed)
            {
                AddSample(mouse.position.ReadValue());
            }
            else if (IsSwiping && mouse.leftButton.wasReleasedThisFrame)
            {
                AddSample(mouse.position.ReadValue());
                End();
            }
        }

        private void Begin(Vector2 pos)
        {
            _buffer.Clear();
            _buffer.Add(new Sample { pos = pos, time = Time.unscaledTime });
            _runningMinY = pos.y;
            IsSwiping = true;
            LiveUpStroke = 0f;
        }

        private void AddSample(Vector2 pos)
        {
            _buffer.Add(new Sample { pos = pos, time = Time.unscaledTime });
            if (pos.y < _runningMinY) _runningMinY = pos.y;
            // Live up-stroke = how far back up we've come from the lowest point.
            LiveUpStroke = Mathf.Clamp01((pos.y - _runningMinY) / Screen.height
                                         / Mathf.Max(0.01f, tuning ? tuning.minUpStrokeFraction * 4f : 0.4f));
        }

        private void End()
        {
            IsSwiping = false;
            LiveUpStroke = 0f;

            if (_buffer.Count < 3) return;

            // Reversal = lowest point of the gesture; the up-stroke is everything after it.
            int reversal = 0;
            float minY = _buffer[0].pos.y;
            for (int i = 1; i < _buffer.Count; i++)
            {
                if (_buffer[i].pos.y < minY)
                {
                    minY = _buffer[i].pos.y;
                    reversal = i;
                }
            }

            int last = _buffer.Count - 1;
            if (last - reversal < 2) return; // no meaningful up-stroke

            Sample start = _buffer[reversal];
            Sample finish = _buffer[last];

            float vertical = finish.pos.y - start.pos.y;
            if (vertical < tuning.minUpStrokeFraction * Screen.height) return; // tap / flick

            // Power from up-stroke speed in screen-heights per second.
            float dt = Mathf.Max(1e-4f, finish.time - start.time);
            float speed = (vertical / Screen.height) / dt;
            float power01 = Mathf.Clamp01(speed / tuning.referenceSwipeSpeed);

            // Curve from mean signed horizontal offset of the up-stroke relative to
            // a vertical line through the reversal point. + = drifted right = slice.
            float meanOffset = 0f;
            int count = 0;
            for (int i = reversal; i <= last; i++)
            {
                meanOffset += (_buffer[i].pos.x - start.pos.x);
                count++;
            }
            meanOffset = (meanOffset / count) / Screen.height;

            float curve = Mathf.Clamp(meanOffset * tuning.curveSensitivity, -1f, 1f);
            if (Mathf.Abs(curve) < tuning.curveDeadzone) curve = 0f;

            OnSwing?.Invoke(power01, curve);
        }
    }
}
