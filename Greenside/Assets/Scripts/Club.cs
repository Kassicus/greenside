using UnityEngine;

namespace Greenside
{
    public enum ClubType { Wood, Iron, Hybrid, Wedge, Putter }

    /// <summary>
    /// A single club, authored as a .asset. The catalog of clubs lives as
    /// ScriptableObject assets; the player's Bag is a list of (up to 14) of them.
    /// Per-club launch behaviour lives here; global swing/physics knobs live on
    /// SwingTuning.
    ///
    /// Create one via Assets > Create > Greenside > Club, or generate the whole
    /// starter set via the "Greenside > Build Starter Club Catalog" menu.
    /// </summary>
    [CreateAssetMenu(fileName = "Club", menuName = "Greenside/Club")]
    public class Club : ScriptableObject
    {
        [Header("Identity")]
        public string displayName = "New Club";
        public ClubType type = ClubType.Iron;

        [Header("Launch")]
        [Tooltip("Launch elevation in degrees. Driver low (~10), wedges high (~55), putter 0.")]
        public float loftDegrees = 30f;
        [Tooltip("Launch speed (m/s) on a full-power swing.")]
        public float maxLaunchSpeed = 55f;
        [Tooltip("Launch speed (m/s) on the weakest registered swing.")]
        public float minLaunchSpeed = 14f;

        [Header("Spin")]
        [Tooltip("Multiplier on the global sidespin. Woods < irons < wedges; putter 0 (no curve).")]
        public float spinFactor = 1f;
    }
}
