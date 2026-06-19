# Project Brief — Greenside (Low-Poly Arcade Golf)

> Hand this to Claude Code at the start of the project. It defines the vision, scope, tech
> constraints, and a phased build order. Treat the "Scope — Out" and "Engineering Constraints"
> sections as hard rules. When something is ambiguous or underspecified, **ask before guessing** —
> see "Open Decisions" at the end.

---

## 1. Vision

A relaxing, fun, **low-poly arcade golf game** for iPhone. Pick your clubs, aim, and swing with a
single intuitive finger gesture. No menus full of currency, no unlocks, no online competition —
just a satisfying, good-looking golfing experience you can pick up and play.

Success = the swing *feels* good, the holes are varied and fair, and a full 9 or 18-hole round
flows smoothly on a real iPhone at a stable frame rate.

---

## 2. Platform & Tech Stack

- **Engine:** Unity 6.5 (Editor 6000.5.x), C#.
- **Render pipeline:** **URP (Universal Render Pipeline)** — create the project from a URP / 3D
  Mobile template. Do **not** use the Built-In Render Pipeline; it is deprecated as of Unity 6.5.
  Do not use HDRP (desktop/high-end, wrong fit for this).
- **Physics:** Unity's built-in PhysX (`Rigidbody` + colliders). 3D.
- **Input:** the **Input System** package with **Enhanced Touch** (`UnityEngine.InputSystem.EnhancedTouch`).
  Do not build on the legacy Input Manager.
- **Cameras:** **Cinemachine** (core package) for follow / aim / hole-preview cameras.
- **Splines:** the **Unity Splines** package (`com.unity.splines`) for the hole centerline.
- **Target:** iPhone (iOS). Build target iOS, **scripting backend IL2CPP, ARM64**. Unity emits an
  Xcode project; build/archive in Xcode. A paid Apple Developer account is available, so
  TestFlight/device deployment is in scope.
- **Source control:** Git/GitHub (repo already created). Use **Git LFS** for large binary assets
  (models, textures, audio) — Unity projects benefit significantly from LFS.

### iOS / IL2CPP constraints (important)
- iOS requires **IL2CPP** (Mono is not supported on iOS). IL2CPP performs **managed code stripping**,
  which can remove code only reached via reflection.
- For save/load, use Unity's built-in **`JsonUtility`** (stripping-safe) written to
  `Application.persistentDataPath`. If any reflection-based serialization is ever introduced,
  preserve the affected types with `[Preserve]` or a `link.xml`.
- Be aware of the iOS build-size floor (a basic Unity app is tens of MB); keep assets lean.
- Develop and test in the Editor (Game view + Device Simulator) first; only iterate through Xcode
  when needed.

---

## 3. Scope

### In scope
- Single swing-gesture control with cut/slice based on finger-path straightness.
- Club selection: build a bag of **14 clubs** chosen from a full catalog (woods, irons, hybrids,
  wedges, putter).
- Aim by rotating, then swing.
- Full **9 and 18-hole** rounds with **procedurally generated** holes.
- Local persistence: scorecards, round history, and a handicap.
- Low-poly flat-shaded visuals.

### Out of scope (do NOT build)
- No online anything: no leaderboards, no multiplayer, no accounts, no networking, no Unity
  Gaming Services / cloud features.
- No progression systems: no XP, no levels, no unlocks, no currency, no shop.
- No microtransactions or ads.
- Keep everything offline and on-device.

---

## 4. Core Gameplay

### 4.1 Swing input
The signature mechanic. The player swipes a finger **down and then up** to swing.
- Using Enhanced Touch, capture touch positions (with timestamps) into a buffer from touch-begin,
  through the reversal, to touch-end.
- **Power** is derived from the **length of the down-stroke** (the "backswing" — how far back the
  club is brought). Using length, not speed, makes a given power easy to repeat — important for
  controlled partial shots (e.g. wedges near the green). Locks at the reversal.
- **Curve (cut/slice/draw/fade)** is derived from the **lateral straightness of the up-stroke**
  (the through-swing path): measure signed horizontal deviation from a straight vertical line
  (e.g. signed drift or signed area). Dead-straight = pure shot; drift one way = slice/fade, the
  other = hook/draw. Respect handedness (make it a setting; default right-handed).
- Map curve to ball **sidespin**.
- Provide light visual feedback during the swing (power meter / path trail) so the gesture is learnable.

### 4.2 Ball physics
- Ball = `Rigidbody` + `SphereCollider`.
- Set the ball's **collision detection to Continuous (or Continuous Dynamic)** — it moves fast and
  will tunnel through terrain with discrete detection.
- On release: `AddForce(..., ForceMode.Impulse)` with a heading from aim and an elevation from the
  selected club's loft; apply sidespin via `AddTorque`, plus a small Magnus-style lateral force each
  `FixedUpdate` (proportional to spin × forward speed). Let bounce/roll emerge from PhysX.
- Start with this full-physics approach. If club distances prove too inconsistent to balance, fall
  back to a hybrid (analytic parabola for carry, hand off to the Rigidbody at landing) — only if needed.
- Use per-surface **Physic Materials** (friction/bounciness): green (low friction, putts run),
  fairway (moderate), rough (high friction), sand (very high friction, low bounce), water (hazard).

### 4.3 Aiming & camera
- Cinemachine camera following the ball.
- A distinct aim mode: dragging rotates the shot heading; show a target arrow/indicator.
- Nice-to-have: a Cinemachine hole-preview camera that blends out to reveal the hole at the start
  of each hole, then blends back to the tee.

### 4.4 Round flow & scoring
- Standard stroke play. Track strokes per hole, par, and total vs par.
- Hole ends when the ball is holed; advance to next tee.
- Handle out-of-bounds and water hazards with a sensible penalty + drop.
- Scorecard visible during and after the round.

---

## 5. Procedural Hole Generation

The core design challenge: holes must be **varied but always playable and fair**.

Suggested approach:
- Define the hole centerline as a **Spline** (Unity Splines) from tee to pin, with random bends for
  doglegs.
- Build a grid **Mesh** procedurally (set `vertices`/`triangles`/`normals` via `MeshFilter`/
  `MeshRenderer`). Drive base undulation with `Mathf.PerlinNoise`.
- For each vertex, use distance-to-spline to assign a surface: inside fairway radius → blend toward
  a flattened profile; near the pin → flatten harder and tag as **green**; outside → **rough**.
- Carve **bunkers** as depressions near the fairway (tag sand). Place **water** as a flat
  translucent plane; terrain below it is a hazard.
- **Collision:** put a `MeshCollider` on the generated terrain (static, non-convex is fine).
  Alternatively, Unity Terrain + `TerrainCollider` if a heightmap workflow is preferred — but the
  custom mesh route gives the faceted low-poly look more directly.
- **Seed the RNG per hole** (`System.Random` / `Unity.Mathematics` with an explicit seed) so courses
  are reproducible — effectively shareable course seeds, even with no online layer.
- Generate hole length/par variety (mix of par 3/4/5). Ensure the green is reachable and the start
  area is clear.

Guardrails: validate generated holes (reachable green, no impossible geometry, hazards not blocking
the only path). Regenerate or adjust if validation fails.

---

## 6. Data Model

### 6.1 Club (data-driven)
A `Club` **ScriptableObject** authored as `.asset` files:
- `name`, `type` (wood / iron / hybrid / wedge / putter)
- `loft` / launch angle
- `basePower` (carry distance)
- `spinTendency` and any other tuning knobs
- The full catalog lives as ScriptableObject assets; the player's **bag is a list of 14** selected clubs.

### 6.2 Save data / profile (persistence)
- Serialize with `JsonUtility` to a file under `Application.persistentDataPath` (IL2CPP-safe — see §2).
- Persist: rounds played, per-round scorecards, computed **handicap**, chosen bag, and settings
  (handedness, units, etc.).
- `PlayerPrefs` is acceptable only for trivial settings, not for structured round/scorecard data.
- Handicap: a simplified arcade formula is fine (e.g. average of best N recent differentials). Don't
  implement true USGA rules unless later requested.

---

## 7. Visual Style

- Low-poly, **flat-shaded** (faceted) look: generate meshes with **split (non-shared) vertices** so
  each triangle carries its own face normal, or use a flat-shading **Shader Graph** (normals from
  derivatives).
- Paint surface types with **vertex colors** + a simple URP shader (Shader Graph reading vertex
  color) rather than textures — right look, cheap on mobile.
- One **Directional Light** + a skybox + light fog (URP Volume / Lighting settings). Keep a URP
  **Volume** for cheap post (subtle bloom / color grading) tuned for mobile.
- Keep polygon counts modest; favor battery life and a steady frame rate over fidelity.
- The Unity Asset Store has low-poly golf/terrain/foliage packs — fine to use for props to save time.

---

## 8. Suggested Project Structure

```
Assets/
  Scenes/                 Main.unity (managers) — or split as sensible
  Prefabs/                Ball, hole elements, UI prefabs
  Scripts/
    SwingInput.cs         touch buffer -> power + signed curve
    BallController.cs     impulse, spin, Magnus force
    AimController.cs
    HoleGenerator.cs      spline + Perlin -> mesh + MeshCollider
    RoundManager.cs       round/hole state, scoring
    SaveManager.cs        persistentDataPath + JsonUtility
    Handicap.cs
  ScriptableObjects/
    Clubs/                *.asset catalog
  Materials/              flat-shaded materials, Physic Materials
  Shaders/                flat-shading / vertex-color Shader Graph
  Settings/               URP asset + renderer settings
```

(Adjust as sensible — this is a starting point, not a mandate.)

---

## 9. Build Order (phased — don't boil the ocean)

1. **Project setup:** Unity 6.5 URP (3D Mobile) project; enable the Input System; set iOS build
   target with IL2CPP/ARM64; add Cinemachine + Splines packages; `.gitignore` + Git LFS. **Verify a
   trivial scene builds to a real iPhone via Xcode early** to de-risk the pipeline.
2. **Swing prototype on a flat plane:** ball Rigidbody, swing-gesture input, power + curve, sidespin.
   Make it *feel* good before anything else.
3. **Clubs:** Club ScriptableObject, a starter catalog, club selection affecting launch/distance.
4. **One procedural hole:** spline + mesh + surfaces + MeshCollider + pin + hole-out detection.
5. **Round loop:** tee → green → next hole, scoring, scorecard, 9 and 18-hole rounds.
6. **Hazards & rules:** bunkers, water, OB, penalties/drops.
7. **Persistence:** save profile, scorecards, handicap.
8. **Visual polish:** flat-shading pass, lighting, camera feel (Cinemachine), HUD polish.
9. **Bag builder UI:** pick 14 from the catalog.
10. **iPhone pass:** performance/thermals/build-size tuning, touch ergonomics, TestFlight build.

Get each phase working and committed before moving on.

---

## 10. Engineering Constraints & Conventions

- IL2CPP-safe code on the iOS path (no reflection-dependent serialization; use `JsonUtility`; if
  unavoidable, preserve types via `[Preserve]`/`link.xml`). See §2.
- No networking / no external services of any kind.
- Use the Input System (Enhanced Touch), not the legacy Input Manager.
- Ball Rigidbody uses **Continuous** collision detection.
- URP, mobile-tuned (modest quality settings, cheap post).
- Prefer data-driven design (ScriptableObjects) over hard-coded values for clubs and tuning.
- Commit in small, working increments with clear messages; keep binaries in Git LFS.

---

## 11. Open Decisions (ask the human before assuming)

### Resolved (2026-06-18)
- **Game title:** **Greenside.**
- **Handedness:** **right-handed only** — do not build a left-handed setting.
- **Units:** **yards only** — display distances in yards.

### Still open (ask before assuming)
- Exact handicap formula details.
- Aim control scheme specifics (drag-to-rotate vs on-screen arrows/buttons).
- Art direction specifics (color palette, time-of-day, course theme variety).
- Whether course seeds should be visible/shareable to the player.
