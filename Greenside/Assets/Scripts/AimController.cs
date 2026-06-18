using UnityEngine;
using UnityEngine.InputSystem;

namespace Greenside
{
    /// <summary>
    /// Phase 2/3 glue + minimal aim. Holds a heading (yaw), feeds swings from
    /// SwingInput into the BallController using the Bag's currently selected Club,
    /// and draws a lightweight prototype HUD.
    ///
    /// Prototype controls:
    ///   Left / Right arrow : rotate aim
    ///   [ / ]              : change club (handled by the Bag component)
    ///   R                  : reset ball to the tee
    ///   Swipe down-then-up : swing (mouse drag works in the Editor)
    ///
    /// Full drag-to-aim and a proper uGUI HUD come in later phases.
    /// </summary>
    public class AimController : MonoBehaviour
    {
        public BallController ball;
        public SwingInput input;
        public SwingTuning tuning;
        public Bag bag;

        [Tooltip("Auto-return the ball to the tee this many seconds after it rests (0 = wait for R).")]
        public float autoResetDelay = 1.5f;

        private float _headingDeg;
        private float _lastPower;
        private float _lastCurve;
        private float _lastCarryYards = -1f;
        private float _lastTotalYards = -1f;
        private float _resetAt = -1f;

        public Vector3 AimDirection => Quaternion.Euler(0f, _headingDeg, 0f) * Vector3.forward;

        private void OnEnable()
        {
            if (input != null) input.OnSwing += HandleSwing;
            if (ball != null) ball.OnRest += HandleRest;
        }

        private void OnDisable()
        {
            if (input != null) input.OnSwing -= HandleSwing;
            if (ball != null) ball.OnRest -= HandleRest;
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                float dir = (kb.rightArrowKey.isPressed ? 1f : 0f) - (kb.leftArrowKey.isPressed ? 1f : 0f);
                _headingDeg += dir * tuning.aimRotateSpeed * Time.deltaTime;
                if (kb.rKey.wasPressedThisFrame) ResetBall();
            }

            if (ball != null)
                Debug.DrawRay(ball.transform.position, AimDirection * 5f, Color.cyan);

            if (_resetAt > 0f && Time.time >= _resetAt)
                ResetBall();
        }

        private void HandleSwing(float power01, float signedCurve)
        {
            if (ball == null || ball.State == BallController.BallState.InPlay) return;

            Club club = bag != null ? bag.Current : null;
            if (club == null)
            {
                Debug.LogWarning("[AimController] No club selected — assign clubs to the Bag component.");
                return;
            }

            _lastPower = power01;
            _lastCurve = signedCurve;
            _lastCarryYards = -1f;
            _lastTotalYards = -1f;
            ball.Launch(power01, signedCurve, AimDirection, club);
        }

        private void HandleRest(float totalYards)
        {
            _lastTotalYards = totalYards;
            _lastCarryYards = ball != null ? ball.CarryYards : -1f;
            if (autoResetDelay > 0f) _resetAt = Time.time + autoResetDelay;
        }

        private void ResetBall()
        {
            _resetAt = -1f;
            if (ball != null) ball.ResetToTee();
        }

        private void OnGUI()
        {
            const float pad = 12f;
            GUI.Box(new Rect(pad, pad, 290f, 250f), "Greenside — Swing Prototype");

            float x = pad + 12f;
            float y = pad + 30f;

            Club club = bag != null ? bag.Current : null;
            string clubLabel = club != null ? $"{club.displayName}  ({club.loftDegrees:0}°)" : "— none —";
            GUI.Label(new Rect(x, y, 270f, 20f), $"Club: {clubLabel}   ([ ] to change)"); y += 22f;
            GUI.Label(new Rect(x, y, 270f, 20f), $"Aim heading: {_headingDeg:0}°   (←/→ to aim)"); y += 22f;
            GUI.Label(new Rect(x, y, 270f, 20f), "Swipe down then up to swing"); y += 22f;
            GUI.Label(new Rect(x, y, 270f, 20f), "R: reset to tee"); y += 26f;

            // Live power meter while swinging.
            float fill = input != null ? input.LiveUpStroke : 0f;
            GUI.Label(new Rect(x, y, 80f, 20f), "Power");
            GUI.Box(new Rect(x + 60f, y, 180f, 16f), GUIContent.none);
            GUI.Box(new Rect(x + 60f, y, 180f * Mathf.Clamp01(fill), 16f), GUIContent.none);
            y += 26f;

            // Distance readout. CARRY is the primary tuning number — it locks in the
            // instant the ball first lands. Total adds roll-out. Values are live while
            // the ball is in play, then the last shot's numbers persist until the next.
            bool inPlay = ball != null && ball.State == BallController.BallState.InPlay;
            float carryYds = inPlay ? ball.CarryYards : _lastCarryYards;
            float totalYds = inPlay ? ball.DistanceYards : _lastTotalYards;

            GUI.Label(new Rect(x, y, 270f, 22f), $"CARRY:  {(carryYds >= 0f ? $"{carryYds:0} yd" : "—")}"); y += 24f;
            GUI.Label(new Rect(x, y, 270f, 20f), $"Total:  {(totalYds >= 0f ? $"{totalYds:0} yd" : "—")}"); y += 24f;

            if (_lastPower > 0f || _lastCurve != 0f)
            {
                string shape = Mathf.Abs(_lastCurve) < 0.01f ? "straight"
                             : _lastCurve > 0f ? $"slice {_lastCurve * 100f:0}%"
                             : $"hook {-_lastCurve * 100f:0}%";
                GUI.Label(new Rect(x, y, 270f, 20f), $"Last shot: power {_lastPower * 100f:0}%, {shape}");
            }
        }
    }
}
