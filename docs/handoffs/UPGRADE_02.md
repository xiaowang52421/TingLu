# UPGRADE_02 交接

status: PARTIAL

## 2026-10-15 17:30 后真实失败修复

- 00随后发现并交回修复的生产风险：普通碰撞眩晕原先直接早退，漏掉眩晕期间驶过角色的车辆。现眩晕只冻结玩家动作，仍以当前位置查询世界车辆扫掠并沿原唯一车辆失败路径处理；暂停早退不变，不查询碰撞。恢复后 Active 但眩晕计时仍有效的状态也遵守此规则。
- 新增普通碰撞后眩晕 fixture 的运行/暂停两个case和真实 StreetWorld 车辆从两侧扫过眩晕角色的测试；断言无移动、无转向、无探测、车辆立即failure且不额外扣勇气。现18个测试case。
- 最新源码再次 Mono 编译成功，真实 StreetWorld 眩晕扫掠测试 source-only 诊断PASS；仍不是 Unity 验证。已停写 `READY_FOR_UNITY`。

- `output/unity-verification/20261015-171729-08ca496d/editmode.xml` 当前 Role02 为 12/13，普通接触分离重入边界失败，原证据保留。
- 确定生产根因：模拟时间 double 的 `1.6 - 1.5` 小于 float 配置 `contactExitSeconds=0.1f`，数学上恰满0.1秒仍被拒绝。接触退出/退避时间比较加入 `1e-7` 秒容差，远小于模拟步长，不改变持续接触去重。
- 保留原失败测试全部断言，新增参数化阈值用例：0.099秒重入不得扣值，0.1秒边界重入必须再次扣值。现15个测试case。
- Source-only Mono 编译成功（仅 TestWorld 未赋值字段警告），在临时 ASCII 路径运行原失败方法诊断通过；这不是 Unity 测试证据。Unicode 路径直接启动 Mono 的编码失败同样不代表业务失败。
- 状态 `READY_FOR_UNITY`：修复后未启动 Unity，需00正式重测。

role/model/effort: role02；任务目标为 gpt-6-luna/xhigh，实际 provider/model 元数据在当前执行环境不可独立核实。

contract_version: StreetContract 1.0.0

## completed / changed_files

- 新增 `TingLu.Player.StreetPlayerController : IPlayerStateReader`，将玩家移动、转向、专注、盲杖探测、碰撞效果、失败和恢复规则放在模拟时间驱动的控制器中。
- 新增 `Assets/TingLu/Player/StreetPlayerController.cs` 及 `.meta`。
- 更新 `Assets/TingLu/Tests/Upgrade/Role02/StreetPlayerControllerTests.cs` 及既有 `.meta`：新增普通接触耗尽勇气的失败语义和同步 `Restore` 后不被旧 Tick 覆盖的用例。总计 13 项，覆盖移动/转向、探测、专注、混淆、普通接触/勇气失败、车辆失败、同步恢复和活动行人。
- 本交接文件为额外授权交付：`docs/handoffs/UPGRADE_02.md`。
- 未修改共享契约、Config、Bootstrap、asmdef 或其他角色目录。

## integration

- 00 在入口使用正式 `StreetConfig`、`IGameClock`、`IStreetWorld` 和初始 `PlayerState` 构造控制器。每个模拟步先推进世界，再调用 `Tick(StreetInputFrame, delta)`；暂停或输入暂停会跳过玩家更新。
- 移动固定使用世界坐标基向量，朝向单独按 `TurnDegrees` 旋转。待机按配置恢复专注，移动按配置消耗专注；混淆期间移动速度乘配置倍率。无有效材质时不发送脚步事件。
- 监听 `Probed` 并将不可变的 `StreetProbeRequest` 接入探测；监听 `Contacted` 处理碰撞反馈；监听 `Failed` 处理 `Vehicle` / `Courage` 失败。普通接触应用接触自带或配置回退伤害、眩晕值，并按接触状态、离开时长和退避时间去重；车辆接触立即失败且不扣勇气。
- 失败回调是同步的，若其调用 `Restore`，当前 Tick 会立即退出，避免覆盖恢复后状态。检查点恢复时调用 `Restore(state, capturedAt, now)`，控制器会重设活动计时器并清空接触、敲杖历史和步行累积量；关卡结束调用 `SetFinished()`。
- 接触在同帧敲杖之前处理，因此失败或眩晕会抑制该帧探测。`Restore` 后若仍需处理该帧输入，由 00 的恢复流程在之后的新模拟步重新读取输入。

## verification

- 角色02 NUnit 测试现有 13 项，当前修改版本状态 `NOT_RUN`。本角色未启动 Unity，也未运行编译或测试。历史 `20261015-150041-b5472da8` 过滤范围不含Role02；该次Unity脚本编译日志早于当前 `StreetPlayerController.cs` 与Role02测试文件修改时间，不能证明本版本已编译。
- 建议定向命令：`pwsh -NoProfile -File tools/Verify-Unity.ps1 -Stage EditMode -TestFilter TingLu.Tests.Upgrade.Role02`
- 未完成完整运行时接线验证、实际游戏碰撞/探测体验或真人手感验收。

## limitations / contract_requests / next_dependencies

- 没有共享契约变更请求。
- 新增用例确认普通接触耗尽勇气产生 `Courage` 失败且抑制同帧探测，并覆盖该失败回调同步恢复；完整运行时恢复代际与检查点接线仍需00结合05/存档链路验收。
- 下一步由00在独占Unity窗口导入当前源码并运行 `TingLu.Tests.Upgrade.Role02` EditMode；若导入、编译或测试失败，按新证据修复，再与03世界、01输入和05检查点恢复联调。
