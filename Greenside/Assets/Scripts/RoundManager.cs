using UnityEngine;
using UnityEngine.InputSystem;

namespace Greenside
{
    /// <summary>
    /// Phase 5: sequences a round of holes. Builds each hole (seeded, with par/length
    /// variety), counts strokes, advances on hole-out, and tracks the scorecard for a
    /// 9- or 18-hole round. Same round seed -> same round.
    ///
    /// R restarts the current hole; N starts a new round once the round is complete.
    /// </summary>
    public class RoundManager : MonoBehaviour
    {
        [Header("References")]
        public HoleGenerator holeGenerator;
        public BallController ball;
        public AimController aim;

        [Header("Round")]
        [Tooltip("Holes in the round (9 or 18).")]
        public int numHoles = 9;
        public int roundSeed = 1;

        private int[] _pars;
        private float[] _lengths;
        private int[] _strokesPerHole;
        private int _currentHole;
        private int _currentStrokes;
        private bool _roundComplete;
        private bool _holeComplete;
        private bool _proceed;
        private GUIStyle _centerStyle;

        private void OnEnable()
        {
            if (ball != null)
            {
                ball.OnLaunched += HandleLaunched;
                ball.OnHoled += HandleHoled;
            }
        }

        private void OnDisable()
        {
            if (ball != null)
            {
                ball.OnLaunched -= HandleLaunched;
                ball.OnHoled -= HandleHoled;
            }
        }

        private void Start()
        {
            BuildPlan();
            StartHole(0);
        }

        private void Update()
        {
            var kb = Keyboard.current;

            // From the hole-complete summary, advance on Space (or the Next button).
            if (_holeComplete)
            {
                if (kb != null && kb.spaceKey.wasPressedThisFrame) _proceed = true;
                if (_proceed) { _proceed = false; ProceedToNextHole(); }
                return;
            }

            if (kb == null) return;
            if (!_roundComplete && kb.rKey.wasPressedThisFrame) RestartHole();
            if (_roundComplete && kb.nKey.wasPressedThisFrame) NewRound();
        }

        private void BuildPlan()
        {
            numHoles = numHoles >= 18 ? 18 : 9;
            var rng = new System.Random(roundSeed);
            _pars = new int[numHoles];
            _lengths = new float[numHoles];
            _strokesPerHole = new int[numHoles];
            for (int i = 0; i < numHoles; i++)
            {
                double r = rng.NextDouble();
                int par = r < 0.25 ? 3 : (r < 0.75 ? 4 : 5);
                _pars[i] = par;
                _lengths[i] = LengthForPar(par, rng);
            }
        }

        private static float LengthForPar(int par, System.Random rng)
        {
            float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            switch (par)
            {
                case 3: return Range(130f, 200f);    // ~140-220 yd
                case 5: return Range(440f, 540f);    // ~480-590 yd
                default: return Range(270f, 410f);   // par 4, ~295-450 yd
            }
        }

        private void StartHole(int index)
        {
            _currentHole = index;
            _currentStrokes = 0;
            int holeSeed = roundSeed * 1000 + index;
            if (holeGenerator != null) holeGenerator.BuildHole(holeSeed, _lengths[index]);
            if (aim != null) aim.ResetForNewHole();
        }

        private void HandleLaunched()
        {
            if (!_roundComplete) _currentStrokes++;
        }

        private void HandleHoled()
        {
            if (_roundComplete || _holeComplete) return;
            _strokesPerHole[_currentHole] = _currentStrokes;
            _holeComplete = true;   // pause on the summary until the player proceeds
        }

        private void ProceedToNextHole()
        {
            _holeComplete = false;
            if (_currentHole + 1 < numHoles) StartHole(_currentHole + 1);
            else _roundComplete = true;
        }

        private static string ScoreName(int strokes, int par)
        {
            if (strokes == 1) return "Hole in one!";
            int d = strokes - par;
            if (d <= -3) return "Albatross!";
            if (d == -2) return "Eagle!";
            if (d == -1) return "Birdie";
            if (d == 0) return "Par";
            if (d == 1) return "Bogey";
            if (d == 2) return "Double bogey";
            return $"+{d}";
        }

        private void RestartHole()
        {
            if (_roundComplete) return;
            _currentStrokes = 0;
            if (ball != null) ball.ResetToTee();
            if (aim != null) aim.ResetForNewHole();
        }

        private void NewRound()
        {
            roundSeed++;
            _roundComplete = false;
            BuildPlan();
            StartHole(0);
        }

        private void OnGUI()
        {
            if (_pars == null) return;

            const float w = 250f;
            float x = Screen.width - w - 12f;
            float y = 12f;

            int completed = _roundComplete ? numHoles : _currentHole;
            int totalStrokes = 0, totalPar = 0;
            for (int i = 0; i < completed; i++) { totalStrokes += _strokesPerHole[i]; totalPar += _pars[i]; }
            int vsPar = totalStrokes - totalPar;
            string vs = vsPar == 0 ? "E" : (vsPar > 0 ? $"+{vsPar}" : vsPar.ToString());

            GUI.Box(new Rect(x, y, w, _roundComplete ? 92f : 134f), "Scorecard");
            float lx = x + 12f, ly = y + 28f, lw = w - 24f;

            if (_roundComplete)
            {
                GUI.Label(new Rect(lx, ly, lw, 20f), $"ROUND COMPLETE ({numHoles} holes)"); ly += 22f;
                GUI.Label(new Rect(lx, ly, lw, 20f), $"Total: {totalStrokes}   vs par: {vs}"); ly += 22f;
                GUI.Label(new Rect(lx, ly, lw, 20f), "N: new round");
                return;
            }

            GUI.Label(new Rect(lx, ly, lw, 20f), $"Hole {_currentHole + 1} / {numHoles}      Par {_pars[_currentHole]}"); ly += 22f;
            GUI.Label(new Rect(lx, ly, lw, 20f), $"Strokes: {_currentStrokes}"); ly += 22f;
            GUI.Label(new Rect(lx, ly, lw, 20f), $"Thru {completed}: {totalStrokes}   ({vs})"); ly += 24f;

            // Compact per-hole scores so the card builds as you play.
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < completed; i++) sb.Append(_strokesPerHole[i]).Append(' ');
            GUI.Label(new Rect(lx, ly, lw, 20f), $"Scores: {sb}"); ly += 22f;
            GUI.Label(new Rect(lx, ly, lw, 20f), "R: restart hole");

            // Hole-complete summary: score for the hole + proceed button.
            if (_holeComplete)
            {
                if (_centerStyle == null)
                    _centerStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 14 };

                const float pw = 320f, ph = 150f;
                float px = (Screen.width - pw) * 0.5f;
                float py = (Screen.height - ph) * 0.5f;
                GUI.Box(new Rect(px, py, pw, ph), "Hole Complete");

                int s = _strokesPerHole[_currentHole];
                int par = _pars[_currentHole];
                GUI.Label(new Rect(px, py + 38f, pw, 24f), ScoreName(s, par), _centerStyle);
                GUI.Label(new Rect(px, py + 66f, pw, 22f), $"Hole {_currentHole + 1}:  {s} strokes  (par {par})", _centerStyle);

                string label = _currentHole + 1 < numHoles ? "Next Hole ▶" : "Finish Round ▶";
                if (GUI.Button(new Rect(px + pw * 0.5f - 80f, py + ph - 44f, 160f, 30f), label))
                    _proceed = true;
            }
        }
    }
}
