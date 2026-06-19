using UnityEngine;
using UnityEngine.InputSystem;

namespace Greenside
{
    /// <summary>
    /// Phase 2-4 glue + minimal aim. Holds a heading (yaw), pivots the camera to it,
    /// feeds swings from SwingInput into the BallController using the Bag's selected
    /// Club, counts strokes, and draws a lightweight prototype HUD.
    ///
    /// Play-it-where-it-lies: after a shot the ball stays at its resting spot and the
    /// next swing plays from there. R restarts the hole from the tee.
    ///
    /// Prototype controls:
    ///   Left / Right arrow : rotate aim (the camera orbits the ball)
    ///   [ / ]              : change club (handled by the Bag component)
    ///   R                  : restart the hole from the tee
    ///   Swipe down-then-up : swing (mouse drag works in the Editor)
    /// </summary>
    public class AimController : MonoBehaviour
    {
        public BallController ball;
        public SwingInput input;
        public SwingTuning tuning;
        public Bag bag;
        public HoleGenerator hole;

        [Tooltip("Pivot the camera follows: sits at the ball and turns to the aim heading, " +
                 "so the camera orbits the ball to face where you're aiming.")]
        public Transform cameraPivot;

        private float _headingDeg;
        private int _strokes;
        private bool _holed;
        private float _selectedCarryYards = -1f;
        private float _lastPower;
        private float _lastCurve;
        private float _lastCarryYards = -1f;
        private float _lastTotalYards = -1f;

        public Vector3 AimDirection => Quaternion.Euler(0f, _headingDeg, 0f) * Vector3.forward;

        private void OnEnable()
        {
            if (input != null) input.OnSwing += HandleSwing;
            if (ball != null)
            {
                ball.OnRest += HandleRest;
                ball.OnHoled += HandleHoled;
            }
        }

        private void OnDisable()
        {
            if (input != null) input.OnSwing -= HandleSwing;
            if (ball != null)
            {
                ball.OnRest -= HandleRest;
                ball.OnHoled -= HandleHoled;
            }
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                float dir = (kb.rightArrowKey.isPressed ? 1f : 0f) - (kb.leftArrowKey.isPressed ? 1f : 0f);
                _headingDeg += dir * tuning.aimRotateSpeed * Time.deltaTime;
                if (kb.rKey.wasPressedThisFrame) RestartHole();
            }

            // Full-power carry of the selected club, computed from the live tuning.
            Club current = bag != null ? bag.Current : null;
            _selectedCarryYards = current != null ? BallController.EstimateFlatCarryYards(current, tuning) : -1f;

            if (ball != null)
                Debug.DrawRay(ball.transform.position, AimDirection * 5f, Color.cyan);
        }

        private void LateUpdate()
        {
            // Keep the camera pivot on the ball and turned to the aim heading. In
            // LateUpdate so it reads the ball's final (interpolated) position for the
            // frame; the Cinemachine camera follows it from behind.
            if (cameraPivot != null && ball != null)
            {
                cameraPivot.position = ball.transform.position;
                cameraPivot.rotation = Quaternion.Euler(0f, _headingDeg, 0f);
            }
        }

        private void HandleSwing(float power01, float signedCurve)
        {
            // Only swing when teed up or sitting at rest — never mid-flight or holed.
            if (ball == null) return;
            if (ball.State != BallController.BallState.Teed && ball.State != BallController.BallState.Resting) return;

            Club club = bag != null ? bag.Current : null;
            if (club == null)
            {
                Debug.LogWarning("[AimController] No club selected — assign clubs to the Bag component.");
                return;
            }

            _strokes++;
            _lastPower = power01;
            _lastCurve = signedCurve;
            _lastCarryYards = -1f;
            _lastTotalYards = -1f;
            float powerMultiplier = hole != null ? hole.PowerMultiplierAt(ball.transform.position) : 1f;
            ball.Launch(power01, signedCurve, AimDirection, club, powerMultiplier);
        }

        private void HandleRest(float totalYards)
        {
            _lastTotalYards = totalYards;
            _lastCarryYards = ball != null ? ball.CarryYards : -1f;
        }

        private void HandleHoled()
        {
            _holed = true;
        }

        private void RestartHole()
        {
            _strokes = 0;
            _holed = false;
            _lastCarryYards = -1f;
            _lastTotalYards = -1f;
            if (ball != null) ball.ResetToTee();
        }

        private void OnGUI()
        {
            const float pad = 12f;
            const float w = 304f;
            GUI.Box(new Rect(pad, pad, w, 210f), "Greenside");

            float x = pad + 14f;
            float y = pad + 30f;
            float lw = w - 28f;

            Club club = bag != null ? bag.Current : null;

            // Selected club.
            string clubName = club != null ? $"{club.displayName}  ({club.loftDegrees:0}°)" : "—";
            GUI.Label(new Rect(x, y, lw, 20f), $"Club:    {clubName}    ([ ])"); y += 22f;

            // That club's full-power carry (computed from the physics tuning).
            string carry = _selectedCarryYards >= 1f ? $"{_selectedCarryYards:0} yd" : "—";
            GUI.Label(new Rect(x, y, lw, 20f), $"Carry:   {carry}"); y += 22f;

            // Distance to the hole.
            float toPin = ball != null ? ball.DistanceToPinYards : -1f;
            GUI.Label(new Rect(x, y, lw, 20f), $"To pin:  {(toPin >= 0f ? $"{toPin:0} yd" : "—")}"); y += 28f;

            // Live power meter — fills with the power being set while swinging, then
            // holds the last shot's power.
            bool swinging = input != null && input.IsSwiping;
            float power = swinging ? input.LivePower : _lastPower;
            GUI.Label(new Rect(x, y, 48f, 20f), "Power");
            float barX = x + 50f;
            float barW = lw - 50f - 48f;
            GUI.Box(new Rect(barX, y, barW, 18f), GUIContent.none);
            GUI.Box(new Rect(barX, y, barW * Mathf.Clamp01(power), 18f), GUIContent.none);
            GUI.Label(new Rect(barX + barW + 6f, y, 48f, 20f), $"{power * 100f:0}%"); y += 30f;

            // Secondary debug info.
            string lie = (hole != null && ball != null) ? hole.SurfaceAtWorld(ball.transform.position).ToString() : "—";
            GUI.Label(new Rect(x, y, lw, 20f), $"Strokes: {_strokes}      Lie: {lie}"); y += 22f;

            if (_holed)
            {
                GUI.Label(new Rect(x, y, lw, 20f), $"HOLED in {_strokes}!"); y += 22f;
            }
            else if (_lastTotalYards >= 0f)
            {
                string shape = Mathf.Abs(_lastCurve) < 0.01f ? "straight"
                             : _lastCurve > 0f ? $"slice {_lastCurve * 100f:0}%"
                             : $"hook {-_lastCurve * 100f:0}%";
                string c = _lastCarryYards >= 0f ? $"{_lastCarryYards:0}" : "—";
                GUI.Label(new Rect(x, y, lw, 20f), $"Last: carry {c} / total {_lastTotalYards:0} yd, {shape}"); y += 22f;
            }
            else y += 22f;

            GUI.Label(new Rect(x, y, lw, 20f), "Down = power, up = curve    ←/→ aim    R restart");
        }
    }
}
