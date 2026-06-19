using UnityEngine;
using UnityEngine.InputSystem;

namespace Greenside
{
    /// <summary>
    /// Swing/aim glue + swing HUD. Holds a heading (yaw), pivots the camera to it,
    /// feeds swings from SwingInput into the BallController using the Bag's selected
    /// Club, and draws the swing HUD. Round flow and scoring live in RoundManager.
    ///
    /// Controls:
    ///   Left / Right arrow : rotate aim (the camera orbits the ball)
    ///   [ / ]              : change club (handled by the Bag component)
    ///   Drag down then up  : swing — down-stroke sets power, up-stroke sets curve
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
        private int _shotTypeIndex;
        private float _selectedCarryYards = -1f;
        private float _lastPower;
        private float _lastCurve;
        private float _lastCarryYards = -1f;
        private float _lastTotalYards = -1f;

        public Vector3 AimDirection => Quaternion.Euler(0f, _headingDeg, 0f) * Vector3.forward;

        /// <summary>Reset aim, shot type and shot readouts for the start of a hole.</summary>
        public void ResetForNewHole()
        {
            _headingDeg = 0f;
            _shotTypeIndex = 0;
            _lastPower = 0f;
            _lastCurve = 0f;
            _lastCarryYards = -1f;
            _lastTotalYards = -1f;
        }

        private ShotModifier CurrentShot()
        {
            var shots = tuning != null ? tuning.shotTypes : null;
            if (shots != null && shots.Length > 0)
                return shots[Mathf.Clamp(_shotTypeIndex, 0, shots.Length - 1)];
            return new ShotModifier { name = "Full", powerScale = 1f };
        }

        private void SetShot(int index)
        {
            int n = (tuning != null && tuning.shotTypes != null) ? tuning.shotTypes.Length : 0;
            if (index >= 0 && index < n) _shotTypeIndex = index;
        }

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

                if (kb.digit1Key.wasPressedThisFrame) SetShot(0);
                if (kb.digit2Key.wasPressedThisFrame) SetShot(1);
                if (kb.digit3Key.wasPressedThisFrame) SetShot(2);
                if (kb.digit4Key.wasPressedThisFrame) SetShot(3);
            }

            // Full-power carry of the selected club + shot type, from the live tuning.
            Club current = bag != null ? bag.Current : null;
            if (current != null)
            {
                ShotModifier shot = CurrentShot();
                float scale = shot.powerScale <= 0f ? 1f : shot.powerScale;
                _selectedCarryYards = BallController.EstimateFlatCarryYards(
                    current.maxLaunchSpeed * scale, current.loftDegrees + shot.loftDelta, tuning);
            }
            else _selectedCarryYards = -1f;

            if (ball != null)
                Debug.DrawRay(ball.transform.position, AimDirection * 5f, Color.cyan);
        }

        private void LateUpdate()
        {
            // Keep the camera pivot on the ball and turned to the aim heading (read in
            // LateUpdate for the ball's final interpolated position this frame).
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

            _lastPower = power01;
            _lastCurve = signedCurve;
            _lastCarryYards = -1f;
            _lastTotalYards = -1f;
            float powerMultiplier = hole != null ? hole.PowerMultiplierAt(ball.transform.position) : 1f;
            ball.Launch(power01, signedCurve, AimDirection, club, powerMultiplier, CurrentShot());
        }

        private void HandleRest(float totalYards)
        {
            _lastTotalYards = totalYards;
            _lastCarryYards = ball != null ? ball.CarryYards : -1f;
        }

        private void OnGUI()
        {
            const float pad = 12f;
            const float w = 304f;
            GUI.Box(new Rect(pad, pad, w, 238f), "Greenside");

            float x = pad + 14f;
            float y = pad + 30f;
            float lw = w - 28f;

            Club club = bag != null ? bag.Current : null;

            // Selected club.
            string clubName = club != null ? $"{club.displayName}  ({club.loftDegrees:0}°)" : "—";
            GUI.Label(new Rect(x, y, lw, 20f), $"Club:    {clubName}    ([ ])"); y += 22f;

            // Shot-type selector (press 1-4 or click).
            var shots = tuning != null ? tuning.shotTypes : null;
            if (shots != null && shots.Length > 0)
            {
                float bw = lw / shots.Length;
                for (int i = 0; i < shots.Length; i++)
                {
                    Color prevBg = GUI.backgroundColor;
                    if (i == _shotTypeIndex) GUI.backgroundColor = Color.yellow;
                    if (GUI.Button(new Rect(x + i * bw, y, bw - 3f, 22f), shots[i].name)) SetShot(i);
                    GUI.backgroundColor = prevBg;
                }
                y += 26f;
            }

            // That club's full-power carry (computed from the physics tuning).
            string carry = _selectedCarryYards >= 1f ? $"{_selectedCarryYards:0} yd" : "—";
            GUI.Label(new Rect(x, y, lw, 20f), $"Carry:   {carry}"); y += 22f;

            // Distance to the hole.
            float toPin = ball != null ? ball.DistanceToPinYards : -1f;
            GUI.Label(new Rect(x, y, lw, 20f), $"To pin:  {(toPin >= 0f ? $"{toPin:0} yd" : "—")}"); y += 28f;

            // Live power meter — fills while pulling down (backswing), locks at reversal.
            bool swinging = input != null && input.IsSwiping;
            float power = swinging ? input.LivePower : _lastPower;
            GUI.Label(new Rect(x, y, 48f, 20f), "Power");
            float barX = x + 50f;
            float barW = lw - 50f - 48f;
            GUI.Box(new Rect(barX, y, barW, 18f), GUIContent.none);
            GUI.Box(new Rect(barX, y, barW * Mathf.Clamp01(power), 18f), GUIContent.none);
            GUI.Label(new Rect(barX + barW + 6f, y, 48f, 20f), $"{power * 100f:0}%"); y += 30f;

            string lie = (hole != null && ball != null) ? hole.SurfaceAtWorld(ball.transform.position).ToString() : "—";
            GUI.Label(new Rect(x, y, lw, 20f), $"Lie: {lie}"); y += 22f;

            string shape = Mathf.Abs(_lastCurve) < 0.01f ? "straight"
                         : _lastCurve > 0f ? $"slice {_lastCurve * 100f:0}%"
                         : $"hook {-_lastCurve * 100f:0}%";
            string lastTxt = _lastTotalYards >= 0f
                ? $"Last: {(_lastCarryYards >= 0f ? $"{_lastCarryYards:0}" : "—")} / {_lastTotalYards:0} yd, {shape}"
                : "Last: —";
            GUI.Label(new Rect(x, y, lw, 20f), lastTxt); y += 22f;

            GUI.Label(new Rect(x, y, lw, 20f), "Down = power, up = curve    ←/→ aim");
        }
    }
}
