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
        [Header("Launch (m/s) — full vs. weakest swing")]
        [Tooltip("Launch speed at full power (power01 = 1).")]
        public float maxLaunchSpeed = 60f;
        [Tooltip("Launch speed at the weakest registered swing (power01 = 0).")]
        public float minLaunchSpeed = 8f;
        [Tooltip("Prototype launch elevation in degrees. Moves to the Club SO in Phase 3.")]
        public float defaultLoftDegrees = 16f;

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
        [Tooltip("Up-stroke speed in screen-heights/sec that maps to full power.")]
        public float referenceSwipeSpeed = 3.0f;
        [Tooltip("Up-stroke shorter than this fraction of screen height is ignored as a tap.")]
        [Range(0f, 0.5f)] public float minUpStrokeFraction = 0.06f;

        [Header("Ball physics")]
        [Tooltip("Golf ball mass in kg (regulation ~0.0459 kg).")]
        public float mass = 0.0459f;
        [Tooltip("Linear damping (crude air drag).")]
        public float linearDamping = 0.05f;
        [Tooltip("Angular damping so sidespin bleeds off over the flight.")]
        public float angularDamping = 0.4f;

        [Header("Rest detection")]
        [Tooltip("Ball is considered stopped below this speed (m/s)...")]
        public float restSpeed = 0.3f;
        [Tooltip("...once it has stayed below restSpeed for this long (seconds).")]
        public float restTime = 0.6f;

        [Header("Aim")]
        [Tooltip("Aim rotation speed in degrees/sec (left/right arrows in the prototype).")]
        public float aimRotateSpeed = 60f;

        [Header("Display")]
        [Tooltip("Meters-to-yards factor for on-screen distances (we display yards).")]
        public float yardsPerMeter = 1.09361f;
    }
}
