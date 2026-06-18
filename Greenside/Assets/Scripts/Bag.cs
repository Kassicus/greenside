using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Greenside
{
    /// <summary>
    /// The player's set of clubs and the currently selected one. For Phase 3 the
    /// clubs are assigned in the Inspector (drag the catalog assets in); the
    /// "pick 14 from the catalog" bag-builder UI comes in Phase 9.
    ///
    /// [ / ] cycle the selected club (wraps around).
    /// </summary>
    public class Bag : MonoBehaviour
    {
        [Tooltip("The clubs in the bag (up to 14). Drag Club assets here.")]
        public List<Club> clubs = new List<Club>();

        [SerializeField] private int currentIndex = 0;

        public Club Current =>
            (clubs != null && clubs.Count > 0)
                ? clubs[Mathf.Clamp(currentIndex, 0, clubs.Count - 1)]
                : null;

        public int CurrentIndex => clubs != null && clubs.Count > 0
            ? Mathf.Clamp(currentIndex, 0, clubs.Count - 1)
            : 0;

        public void Next() => Select(currentIndex + 1);
        public void Previous() => Select(currentIndex - 1);

        public void Select(int index)
        {
            if (clubs == null || clubs.Count == 0) return;
            // Wrap around so cycling never goes out of range.
            currentIndex = ((index % clubs.Count) + clubs.Count) % clubs.Count;
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.rightBracketKey.wasPressedThisFrame) Next();
            if (kb.leftBracketKey.wasPressedThisFrame) Previous();
        }
    }
}
