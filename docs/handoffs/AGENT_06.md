status: IMPLEMENTED_SOURCE_NOT_UNITY_VERIFIED
contract_version: 0.2.0

completed:
- Added a pure C# menu state controller for first launch, start/continue, pause/resume, retry, results, settings, goal replay, and return to the main menu. It takes semantic text, speech, save callbacks, and game-state callbacks from Runtime; it does not query world state.
- Focus movement skips disabled continue; menu announcements use a stable `menu-focus` speech supersession key and carry the supplied recovery generation. `PresentKnownText` accepts only already-routed text and can send it to captions and speech.
- Explicit `Increase`/`Decrease` actions adjust the focused setting. Refreshing a setting keeps focus on that item and announces its updated label once. Turning subtitles off only hides captions; routed semantic speech continues.
- Start, continue, and retry callbacks leave the displayed screen under Runtime ownership. The menu no longer forces `Playing` after a `void` callback, so failed starts can remain reportable by the Runtime owner.
- Settings edit the shared `UserSettings` contract: display mode, output mode, master volume, subtitles, haptics, color-blind palette, font scale, and voiceprint intensity. Every edit returns a complete settings value to Runtime's save callback.
- Added a Unity IMGUI panel with mouse focus/submit and scalable captions. Runtime passes `InputFrame` to `AccessibleMenu.HandleInput` and routes consumed menu input away from the player. No direct `UnityEngine.Input` use or system-world queries are present.

changed_files:
- `Assets/TingLu/UI/AccessibleMenu.cs` (+ `.meta`)
- `Assets/TingLu/UI/AccessibleMenuPanel.cs` (+ `.meta`)
- `Tests/Accessibility/MenuChecks.cs`
- `tools/Verify-AudioUi.ps1`
- `docs/qa/audio-ui-results.json`
- `docs/handoffs/AGENT_06.md`

integration API:
- Construct `AccessibleMenu`, then call `Initialize(UserSettings settings, bool hasSavedRun, string knownGoalText, MenuCallbacks actions, IAudioSpeech speechService, int recoveryGeneration)`.
- `MenuCallbacks` accepts `Start`, `Continue`, `Pause`, `Resume`, `Retry`, `ReturnToMenu`, `ReplayGoal`, and `SaveOptions(UserSettings)`. Runtime supplies all state changes, save results, and trusted goal wording. `OpenResults(summary)` displays the result passed by Runtime.
- Runtime calls `HandleInput(InputFrame)` before player control. `true` means the menu consumed the input. `Playing` consumes only pause/query-goal edges; gameplay input can pass through otherwise. Call `OpenPause`, `OpenResults(summary)`, or `SetPlaying` on known Runtime state changes.
- Bind `AccessibleMenuPanel.Initialize(menu)` for the Unity visual shell. Its public `HandleInput(InputFrame)` also returns consumption status when Runtime uses the view as the routing entry point.
- Use `ReplayGoal` callback to invoke existing semantic feedback; if omitted, the controller speaks the trusted `knownGoalText` itself. `OpenResults(summary)` supplies a concise default when Runtime has no result sentence.

verification:
- `tools/Verify-AudioUi.ps1` passed menu focus/disabled continue, pause/resume, retry/results, goal query, all three display modes, subtitles-off speech, settings persistence, and source audits for backend pause/stop/volume behavior.
- Unity import, mouse/controller play, system-reader integration, screen reader, touch, and accessibility/device tests were not run. HRTF remains unfinished.
- Unity import, mouse/controller play, system-reader integration, screen reader, touch, subtitle visibility, and accessibility/device tests were not run. HRTF remains unfinished.

remaining integration:
- The existing `LocalSaveService` DTO currently decodes only the 0.1.1 settings fields. Its owner must serialize/restore `Palette`, `FontScale`, and `VoiceprintIntensity` before claiming those three values persist across restart.
- Runtime should apply `MasterVolume` and `VoiceprintIntensity` to `UnityAudioPlayback`, route OutputMode to a defined device policy, and provide already-known semantic captions through `PresentKnownText`. Current backend does not change speaker/headphone routing based on OutputMode.
- This is source functionality; there is no Unity build or complete first-level playthrough evidence yet.

contract_requests: none for 0.2.0.
