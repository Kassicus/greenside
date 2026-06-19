using UnityEngine;

namespace Greenside
{
    /// <summary>A short-game shot preset (Full / Pitch / Chip / Flop): adjusts loft,
    /// power range, backspin, and how hard the ball checks on landing.</summary>
    [System.Serializable]
    public struct ShotModifier
    {
        public string name;
        [Tooltip("Degrees added to (or subtracted from) the club loft.")]
        public float loftDelta;
        [Tooltip("Multiplier on the club's power range — finesse shots are softer.")]
        public float powerScale;
        [Tooltip("Backspin (rad/s) for Magnus lift — higher floats it and holds the green.")]
        public float backspin;
        [Tooltip("Fraction of forward speed killed on the first bounce (check). 0 = runs out.")]
        [Range(0f, 1f)] public float checkOnLanding;
    }

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

        [Header("Shot types (1-4 selects Full / Pitch / Chip / Flop)")]
        public ShotModifier[] shotTypes = DefaultShotTypes();

        private static ShotModifier[] DefaultShotTypes() => new[]
        {
            new ShotModifier { name = "Full",  loftDelta = 0f,   powerScale = 1.00f, backspin = 8f,  checkOnLanding = 0.10f },
            new ShotModifier { name = "Pitch", loftDelta = 8f,   powerScale = 0.60f, backspin = 22f, checkOnLanding = 0.45f },
            new ShotModifier { name = "Chip",  loftDelta = -5f,  powerScale = 0.45f, backspin = 4f,  checkOnLanding = 0.05f },
            new ShotModifier { name = "Flop",  loftDelta = 20f,  powerScale = 0.55f, backspin = 35f, checkOnLanding = 0.75f },
        };

        // Existing assets keep an empty array after a script change, so this fills them.
        [ContextMenu("Reset Shot Types to Defaults")]
        private void ResetShotTypes() => shotTypes = DefaultShotTypes();
    }
}
