using UnityEngine;

namespace Greenside
{
    /// <summary>
    /// Data-driven tuning knobs for the swing prototype (Phase 2).
    /// Per the project rules, tuning values live in a ScriptableObject rather
    /// than being hard-coded. In Phase 3 the per-club values (loft, base power)
    /// migrate into a Club ScriptableObject; the global physics knobs stay here.
    ///
    /// Create one via: Assets > Create > Greenside > Swing Tuning, then assign it
    /// to BallController, SwingInput and AimController.
    /// </summary>
    [CreateAssetMenu(fileName = "SwingTuning", menuName = "Greenside/Swing Tuning")]
    public class SwingTuning : ScriptableObject
    {
        // Per-club launch values (loft, full/weak launch speed, spin factor) now
        // live on the Club ScriptableObject (Assets > Create > Greenside > Club).
        // This asset holds only the GLOBAL knobs shared across every club.

        [Header("Curve / sidespin")]
        [Tooltip("Sidespin (rad/s about the up axis) applied at full curve.")]
        public float maxSidespin = 30f;
        [Tooltip("How strongly horizontal drift maps to curve. Higher = more sensitive.")]
        public float curveSensitivity = 8f;
        [Tooltip("Curve magnitude below this (0..1) is treated as a pure, straight shot.")]
        [Range(0f, 0.5f)] public float curveDeadzone = 0.06f;
        [Tooltip("Magnus lateral acceleration coefficient (force ~ spin x velocity). Mass-independent.")]
        public float magnusCoefficient = 0.0015f;

        [Header("Swing gesture (screen-space, resolution independent)")]
        [Tooltip("Down-stroke (backswing) length as a fraction of screen height that maps to full power. Longer pull-back = more power.")]
        [Range(0.1f, 1f)] public float referenceBackswing = 0.4f;
        [Tooltip("Down-stroke shorter than this fraction of screen height is ignored (not a swing).")]
        [Range(0f, 0.5f)] public float minBackswingFraction = 0.05f;
        [Tooltip("Up-stroke shorter than this fraction of screen height won't fire the shot (incomplete through-swing).")]
        [Range(0f, 0.5f)] public float minUpStrokeFraction = 0.04f;

        [Header("Ball physics")]
        [Tooltip("Golf ball mass in kg (regulation ~0.0459 kg).")]
        public float mass = 0.0459f;
        [Tooltip("Linear damping (crude air drag).")]
        public float linearDamping = 0.05f;
        [Tooltip("Angular damping so sidespin bleeds off over the flight.")]
        public float angularDamping = 0.4f;

        [Header("Ground roll-out (interim — per-surface physics comes in Phase 6)")]
        [Tooltip("Per-second fractional damping on the ball's horizontal speed and spin " +
                 "while it is on the ground. Higher = less roll. Does not affect carry.")]
        public float groundRollDrag = 2.5f;

        [Header("Rest detection")]
        [Tooltip("Ball is considered stopped below this speed (m/s)...")]
        public float restSpeed = 0.3f;
        [Tooltip("...once it has stayed below restSpeed for this long (seconds).")]
        public float restTime = 0.6f;

        [Header("Hole")]
        [Tooltip("Max ball speed (m/s) at which it drops into the cup instead of rolling over.")]
        public float holeCaptureSpeed = 3.5f;

        [Header("Aim")]
        [Tooltip("Aim rotation speed in degrees/sec (left/right arrows in the prototype).")]
        public float aimRotateSpeed = 60f;

        [Header("Display")]
        [Tooltip("Meters-to-yards factor for on-screen distances (we display yards).")]
        public float yardsPerMeter = 1.09361f;
    }
}
