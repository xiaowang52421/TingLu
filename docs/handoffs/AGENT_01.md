status: PARTIAL
contract_version: 0.2.0

completed:
- Fixed `PlayerController.NotifyCollision` to store reduced courage and `Restore` to restore focus/courage into the correct fields while remapping effect times.
- Listening while moving now gains +8 focus/second. Footstep distance accumulates from resolved displacement.
- Player movement delegates to `IWorldMovement` when available; otherwise it uses a local static-contour sweep fallback. `WorldRegistry.ResolveMovement` sweeps the player's 0.18m body radius against static contours and stops before contact.
- `WorldRegistry` implements `IWorldOcclusionQuery`, including object contour edges and explicit occluders within a query radius. Collinear disjoint segments no longer report occlusion.
- Footstep surface lookup now queries explicit ground polygons, choosing the smallest containing region; no nearest-object material guess remains. Default is sidewalk when no registered surface contains the point.
- Added `UnifiedInputSource` in the Input assembly. Keyboard, gamepad and touch callbacks feed the same gameplay frame; stick deadzone, latest-device movement/listening, merged one-shot actions and edge clearing are implemented.
- Added `ThreatReceiptGate`: Ready does not start a threat; Started reports its receipt time; failure/timeout/recovery-generation reset cancels safely.
- Added deterministic first-level world data: pole, slanted parked bicycle, bird, fragment, walker and gatekeeper plus ground surface definitions. Actor definitions mark people and interactions for perception semantics; host provides animation/rendering.

changed_files:
- `Assets/TingLu/Player/PlayerController.cs`
- `Assets/TingLu/Player/ThreatReceiptGate.cs` (+ meta)
- `Assets/TingLu/World/WorldRegistry.cs`
- `Assets/TingLu/Input/UnifiedInputSource.cs` (+ meta)
- `Assets/TingLu/Levels/FirstStepWorldFactory.cs` (+ meta)

integration:
- Construct `UnifiedInputSource(GameplayConfig)`. Input System actions call `SubmitKeyboard`, `SubmitGamepad`, or `SubmitTouch`; then total controller passes `Read()` to player each frame. This assembly intentionally has no Unity Input System reference; add no `using UnityEngine.InputSystem` here unless 00 changes the owned asmdef references.
- Inject `WorldRegistry` as both `IWorldQuery` and `IWorldLifecycle`; `PlayerController` auto-detects `IWorldMovement` from its existing `IWorldQuery` argument. Register polygons through `AddGroundSurface(materialId, points)` and static outlines through object definitions. Query occlusion through the new 0.2 interface.
- `FirstStepWorldFactory.Objects()` and `.Ground()` return immutable scene data. Host registers each object and ground region into the common `WorldRegistry`; movement/animation/rendering remain host responsibilities.
- `ThreatReceiptGate` is a standalone gate. 00/03 should marshal audio receipts onto the main thread, call `Receive`, call `Tick` on active simulation time, and use `OnStarted(receiptTime)` to schedule the danger. `Reset(newGeneration)` cancels outstanding old-generation requests.
- Static contours block movement without automatically damaging the player. The Unity collision adapter should call `NotifyCollision` with configured damage/stun only when the real collision rule intends a hit.

tests:
- PowerShell `Add-Type` compiled Contracts, Config, Bootstrap Core, Input, Player, World, Levels, Persistence together.
- 16 direct checks passed: ground polygon material, +8 listen movement, contour blocking and no repeated footfalls at a wall, courage collision, focus/courage/cooldown restore, common input frame and one-shot tap, disjoint collinear/crossing lines, object-edge occlusion query, save/write/backup fallback, future version load classification and overwrite protection.
- 8 additional checks passed: Ready versus Started, receipt timestamp scheduling, timeout cancellation, stale-generation receipt rejection, resume marker/checkpoint/finished roundtrip, new settings roundtrip.
- Unity Editor, package resolution, scene rendering, Input System device binding, PlayMode and hardware audio were not run.

limitations:
- `UnifiedInputSource` is the platform-neutral InputAction callback bridge, not a Unity Input System MonoBehaviour. Total controller needs to bind actual InputActions.
- World actor definitions and ground regions are scene data only; no Unity scene/prefab/renderer/animation wiring exists in this module.
- Static blocking is implemented; actual damage/stun and dynamic-actor collision policy still belong to the Unity collision adapter.

2026-10-13 集成复核增补：新增跨模块纯 C# Acceptance 汇总编译，`DemoSession.cs` 已纳入候选源码，不再因 Flow API 排除；19 项纯 C# 检查全部通过。涵盖碰撞后勇气降低、移动聆听专注恢复、角色恢复字段、墙面阻挡脚步、连续接触去重、听见后才触发鸟/行人/碎片/门卫台词、盲道须脚下材质、失败2秒呼吸/检查点恢复且本次碰撞统计保留。窄遮挡物开放方向记忆刷新检查通过（Perception 实现由对应模块负责）。Unity Editor、PlayMode、音频硬件与真人盲测仍 NOT RUN。
