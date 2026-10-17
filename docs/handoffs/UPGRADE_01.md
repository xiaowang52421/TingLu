# UPGRADE_01 交接

status: PARTIAL

## 2026-10-15 17:30 后真实失败修复

- `output/unity-verification/20261015-171729-08ca496d/editmode.xml` 当前 Role01 为 5/9，4 项同更新短点击失败，原证据保留。
- 确定测试时序根因：Input System 1.7.0 的 `InputManager.defaultUpdateType` 在非播放 Editor 返回 Editor，`InputActionState.NotifyControlStateChanged` 对 Editor 更新直接返回，因此普通 EditMode `InputSystem.Update()` 不会派发 InputAction 回调。长按末态轮询成功不能证明短点击回调有效。
- Role01 测试改继承官方 `InputTestFixture`，用其隔离的玩家运行时推进输入，并在 finally 恢复原 Input System 状态；9 项关键断言不变。00已补对应测试框架 asmdef 引用。
- 生产 `Configure` 对启用组件显式确保右键 action 初始化，不依赖编辑器生命周期隐式运行；正式 Player 仍由 OnEnable/OnDisable 管理。
- 状态 `READY_FOR_UNITY`：本次修复未启动 Unity，需00定向或全量重测，不宣称通过。

role/model/effort: 01；任务配置目标为 gpt-6-luna/xhigh；实际 provider/model 元数据在当前执行环境不可独立核实。

contract_version: StreetContract 1.0.0

## completed / changed_files

- 新增真实 Unity 组件 `TingLu.Input.StreetInputSampler : MonoBehaviour, IStreetInput`，消费正式 `StreetConfig`、`StreetInputFrame`、`StreetCoordinates.ScreenMove`。
- 修复 `Assets/TingLu/Input/StreetInputSampler.cs`：InputAction started 回调锁存右键按下沿，Read 一次性消费；Clear、焦点丢失、无效/过渡读取丢弃尚未消费的边沿。
- 更新 `Assets/TingLu/Tests/Upgrade/Role01/StreetInputSamplerTests.cs`：菜单W键断言明确区分暂停帧/持续按住与释放后重新按下；新增Clear与失焦后短点击丢弃和恢复测试。
- 源文件与测试文件已有确定性 `.meta`；本次未新增 Unity 资产文件，无需新增 meta。
- 本交接文件为额外授权交付：`docs/handoffs/UPGRADE_01.md`。
- 没有修改共享契约、Config、asmdef、Bootstrap、其他角色目录或原项目。

## integration

00 在入口创建组件后调用 `Configure(StreetConfig)`，每帧读取 `Read()` 并将返回的 `StreetInputFrame` 交给正式玩家/菜单链路；菜单/暂停时调用 `SetGameplayActive(false)`，进入玩法时调用 `SetGameplayActive(true)`。`SetGameplayActive` 状态未改变时不会清空输入。

WASD/方向键只给 `Move`，通过正式 `StreetCoordinates.ScreenMove` 归一化。朝向只读相对 `Mouse.delta.x` 并乘共享灵敏度（不乘 `deltaTime`），反向读 `StreetConfig.InvertMouse`；垂直 delta 忽略。右键按下沿由 InputAction started 回调锁存，并保留状态轮询作兼容回退；每次 `Read()` 最多消费一次。菜单态右键/Enter/Space 只确认菜单，左键交给菜单 UI 自己处理。Esc 输出 `PausePressed`，G 保留 `QueryGoal`，空格和 Shift 不产生玩法动作。

玩法激活且应用聚焦时隐藏并锁定指针；菜单、Esc暂停、失焦、应用暂停、组件禁用和退出时释放。`Clear()` 丢弃下一次读取、相对位移沿及当前已按住控件，已按住的键/右键需先释放再重新按下，避免恢复后卡移动或误探测。重新聚焦时按当前玩法状态恢复锁定。

## tests

- 已编写 9 项 Input System NUnit 用例：四方向及斜向归一化；鼠标水平转向/灵敏度/反向/重复读取；右键边沿、长按、同一更新内按下并释放、Clear与焦点恢复丢弃旧点击；菜单确认隔离；暂停时按住的移动键不穿透到菜单、释放重按后菜单导航；空格/Shift不探测。
- 旧源码历史运行 `output/unity-verification/20261015-150041-b5472da8/editmode.xml` 中 Role01 为 4/7，三项失败已保留：暂停测试将W菜单导航期望写成None但后续同一用例又期望Previous；菜单同更新右键短点击未确认；同更新玩法短点击未探测。该运行早于本次源码和测试修改，不代表当前结果。
- 当前修改版本尚未运行（`NOT_RUN`）；由00在独占 Unity 窗口导入并运行 `TingLu.Tests.Upgrade.Role01` EditMode。
- 建议定向命令：`pwsh -NoProfile -File tools/Verify-Unity.ps1 -Stage EditMode -TestFilter TingLu.Tests.Upgrade.Role01`
- 没有做实际鼠标/Alt+Tab/分辨率或真人手感验证（`NOT_RUN`）。

## limitations / contract_requests / next_dependencies

- 没有契约变更请求。
- 组件本次只负责键鼠；旧 `UnifiedInputSource` 保持未改，游戏手柄接线若进入新版由 00 结合正式入口策略处理。不会自动创建触屏按钮。
- 下一步依赖 00 将该组件接入入口，并在获准的 Unity 窗口运行 `TingLu.Tests.Upgrade.Role01` EditMode 测试；随后将输入、菜单、暂停和聚焦行为纳入 A25/A28 联合验收。
