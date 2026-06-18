# CLAUDE.md — Always-On Rules

Project: low-poly arcade golf game for iPhone. Full scope, design, and build order live in
`BRIEF.md` — read it for *what* to build. This file is the short list of rules to **never break**.
When a rule here and something elsewhere conflict, these rules win. If a task seems to require
breaking one, stop and ask.

## Engine & pipeline
- Unity 6.5 (Editor 6000.5.x), C# only.
- **URP** render pipeline. Never use the Built-In Render Pipeline (deprecated) or HDRP.
- Physics is PhysX (`Rigidbody` + colliders).

## iOS / build
- Target iOS, **scripting backend IL2CPP, ARM64**.
- IL2CPP strips code: keep everything **stripping-safe**. Use `JsonUtility` for save/load (no
  reflection-based serialization). If reflection is ever unavoidable, preserve types with
  `[Preserve]` / `link.xml`.
- Save data goes to `Application.persistentDataPath`. `PlayerPrefs` only for trivial settings.

## Code & systems
- Use the **Input System** package with **Enhanced Touch**. Never the legacy Input Manager.
- The ball `Rigidbody` uses **Continuous** collision detection (it's fast — discrete tunnels).
- Clubs and tuning values are **ScriptableObjects**, not hard-coded.
- Cameras use **Cinemachine**. Hole centerlines use the **Splines** package.

## Hard "do not build" list
- No networking, online services, multiplayer, accounts, leaderboards, or cloud features.
- No progression: no XP, levels, unlocks, currency, shop, microtransactions, or ads.
- Everything offline and on-device.

## Workflow
- Build in the phased order in `BRIEF.md`; finish and commit each phase before the next.
- Small, working commits with clear messages. Binary assets go in **Git LFS**.
- Test in the Editor first; only iterate through Xcode when needed.
- When a decision in the "Open Decisions" section of `BRIEF.md` comes up, **ask — don't guess.**
