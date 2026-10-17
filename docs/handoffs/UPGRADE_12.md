# Role 12 Handoff

status: READY_FOR_UNITY
role/model/effort: Role 12 independent acceptance; requested gpt-6-luna/xhigh; actual provider/model/effort cannot be independently verified from available tool metadata.
contract_version: StreetContract 1.0.0

## completed

- Added a real `StreetWorld -> StreetPlayerController -> StreetPerception -> StreetMemory -> StreetSaveService` integration fixture. It checks permitted area geometry is persisted stale, an active source is not saved as geometry, and a moving-vehicle contact can drive a single checkpoint restoration through the failure callback.
- Added integrated-session fixtures for A25 pause/resume timing and first-save/Continue behavior. These are currently wrapped in `TINGLU_STREET_INTEGRATED`; 00 must remove or otherwise resolve this temporary compile guard when the production entry is ready.
- Added a PlayMode fixture that loads the actual `StreetCrossroads` scene and checks its enabled entry, integrated session, one orthographic camera, and one AudioListener.
- Every fixture uses a randomized system temporary directory where it writes save data; no real save slot is read or changed.

## changed_files

- `Assets/TingLu/Tests/Upgrade/Integration/StreetPipelineIntegrationTests.cs` and `.meta`
- `Assets/TingLu/Tests/Upgrade/Integration/StreetSessionIntegrationTests.cs` and `.meta`
- `Assets/TingLu/Tests/Upgrade/Acceptance/StreetCrossroadsPlayModeTests.cs` and `.meta`
- `docs/handoffs/UPGRADE_12.md`

## integration

- The pipeline fixture consumes the shared contract and production module APIs; it does not add DTOs or production behavior.
- The session fixture targets the current `StreetSession` and `StreetLevelFactory` APIs. It is intentionally not compiled until 00 completes the integrated production entry and resolves `TINGLU_STREET_INTEGRATED`.
- The PlayMode fixture expects `StreetCrossroads` in the Player scene list and a working `StreetEntry` that creates the session and runtime camera/listener.
- Existing test assembly definitions were wired by 00; no shared asmdef was changed by Role 12.

## tests

- Source added: two pipeline integration tests, two integrated-session tests, and one PlayMode scene test.
- Unity import/compile, EditMode, PlayMode, Windows build, visible Player, and A01-A31 acceptance: NOT_RUN. No Unity instance was started by Role 12; 00 owns the exclusive project verification window.
- A25/A30 are not passed by these fixtures. A25 still requires the entire integrated runtime session, and A30 still requires a visible end-to-end playthrough including a genuine traffic collision, checkpoint recovery, save/exit/Continue, and reaching the finish.

## limitations

- The vehicle-failure pipeline test injects a synthetic controlled moving route and exercises the failure callback; it is not evidence of A30's genuine map collision, input path, or traffic timing.
- No screenshots, Player logs, real audio-device output, HRTF, human listening, or accessibility-device evidence has been collected.
- The map's real traffic route must be checked for a reproducible collision window before attempting A30; do not substitute `Player.Restore` or synthetic contact for that acceptance step.

## contract_requests

- None.

## next_dependencies

- Wait for 00's exclusive Unity import/compile and targeted test results; retain and report any failures to the relevant owner rather than changing production code.
- In the next coordinated write window, add the visible development Player probe only within `Assets/TingLu/Diagnostics/Upgrade` and `tools/upgrade`, then use it to collect reproducible startup, save-root, and scene evidence.
- After integration compiles, run the full A25 same-session pause/resume acceptance and A30 genuine end-to-end route on the same build/configuration. Produce `docs/qa/upgrade/ACCEPTANCE.md` and `COMPLIANCE.md` only as evidence is actually collected.
