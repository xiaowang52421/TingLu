# Role 11 Handoff

status: READY_FOR_UNITY
role/model/effort: Role 11; requested gpt-6-luna/xhigh; actual provider/model/effort cannot be independently verified from the available tool metadata.
contract_version: 1.0.0

## completed

- Added `StreetHudPresenter : MonoBehaviour, IStreetHud` with the agreed `Configure(AccessibleMenu)` and `Present(PlayerState, UserSettings, string, bool)` API.
- The presenter creates or reuses `AccessibleMenuPanel`. During gameplay it draws only Courage, Focus, and the enabled subtitle. Meter text uses measured style widths, and subtitle height follows wrapped content within available screen space. It creates no operation buttons and does not read world, surface, object, or perception truth.
- The menu panel wraps long item labels and places menu content and feedback in a scroll view. It resets scroll position when changing menu screens; the bottom keyboard hint was removed to leave space for menu content.
- Added `MenuOption.Quit` and `MenuCallbacks.Quit`; the main and pause menus now provide an accessible exit item while keeping start, continue, resume, retry, settings, and return-to-menu actions.
- Added `AccessibleMenu.SetCanContinue(bool)`. It refreshes the main menu row and retains the focused action when still usable.
- Replaced the generic bus-stop goal fallback with `路线说明暂不可用。`.
- Added Role11 tests for main/pause actions and quit, continue availability refresh, display/subtitle behavior, neutral goal fallback, and presenter/menu-shell setup.

## changed_files

- `Assets/TingLu/UI/AccessibleMenu.cs`
- `Assets/TingLu/UI/StreetHudPresenter.cs` and `.meta`
- `Assets/TingLu/Tests/Upgrade/Role11/AccessibleMenuTests.cs` and `.meta`
- `docs/handoffs/UPGRADE_11.md`

## integration

- `StreetHudPresenter` implements the existing `IStreetHud` in `Contracts/StreetContracts.cs`; no shared contract or assembly definition change was needed.
- The current Bootstrap wiring uses `MenuCallbacks.Quit`, subscribes the entry to the session quit request, calls `SetCanContinue(true)` after a successful save, and configures/presents the HUD.
- The new-game operation speech remains supplied by `StreetSession`; the presenter displays its subtitle only in Standard mode when subtitles are enabled.
- Presenter rendering is suppressed outside `MenuScreen.Playing`, with no player state, or when the display mode is NoUI/AudioOnly. Menus retain the existing keyboard, speech, and Standard-mode visible shell behavior.

## tests

- Added five NUnit EditMode cases under `Assets/TingLu/Tests/Upgrade/Role11`.
- Unity compile/import and test run: NOT_RUN. Per task assignment, Role 11 did not start Unity; 00 owns the exclusive Unity verification window.
- Static inspection confirmed the new gameplay presenter uses labels and texture drawing only; it adds no button controls.

## limitations

- HUD and menu layout, font fallback, subtitle wrapping, and visual contrast have not been checked in a Unity Player or on a touch-sized viewport.
- No human accessibility, device, or audio listening acceptance was performed.

## contract_requests

- None. The implemented menu APIs match the agreed names `Quit` and `SetCanContinue(bool)`.

## next_dependencies

- 00 should compile/import the integrated entry and run the Role11 suite in its exclusive Unity window, then visually inspect a gameplay frame and each display mode.
- 12 should independently verify that gameplay has no virtual operation controls or foot-material text, menu input does not leak into play, and quit/continue behavior is connected in the built entry.
