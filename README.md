# ORDR Unity Mobile

Unity-based, standalone mobile recreation of the gameplay rules of ORDR S2 2.323[C].

## Priorities
1. Original gameplay rules and outcomes
2. Correct units, combat, skills, waves, crafting, economy and difficulty
3. Touch-based Android gameplay
4. New, minimal graphics; visual effects last

No Warcraft III runtime or JASS interpreter is planned. Original map/JASS data are reference material for extracting rules, not runtime dependencies.

## Status
**Foundation only.** No original-game data has been verified or imported yet. No playable build or APK exists.

## Structure
- `Assets/Scripts/Core`: engine-independent deterministic rule primitives
- `Assets/Tests/EditMode`: automated correctness tests
- `docs`: parity and development rules

## Development policy
- Do not invent unit stats, probabilities, recipes, enemy waves, or skill behavior.
- Mark unknown original behavior as unverified; do not silently substitute approximations.
- Verify rules with original source evidence and tests before claiming parity.
- Keep the default `main` branch and avoid unnecessary CI workflows.
- Do not claim an APK was built or tested without actual build and device evidence.

## Setup
Open with a compatible Unity editor after selecting and recording an actual editor version. The Unity version has not yet been pinned. Add the editor-generated project settings and packages once an editor build is validated.

## Legal
Only include assets and game data for which use and distribution rights have been confirmed.
