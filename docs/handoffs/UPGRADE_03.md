# UPGRADE_03 交接

status: PARTIAL

role/model/effort: role03；目标 gpt-6-luna/xhigh；当前执行环境未提供可核实的实际 model、provider 或 effort 元数据。

contract_version: StreetContract 1.0.0

## completed / changed_files

- 新增 `TingLu.World.StreetWorld : IStreetWorld`，实现确定性街区模拟：不可变世界快照、材质区域优先级、静态墙体扫掠碰撞、临时对象移动/移除、车辆路线与相对运动碰撞、接触去重、信号灯相位/停车线，以及交通状态导出/导入。
- `StreetWorldFrame` 的对象副本保留多边形、高度、`BlocksProbe`、`AcousticLoss`、语义及接触伤害/眩晕值，供感知与声音遮挡链路读取。
- 路线 `Offset` 按初始累计距离（米）处理。信号控制路线必须配置 `StopLineDistance`；信号声源 ID `street.signal.ns` 和 `street.signal.ew` 随当前信号相位激活。
- 新增 8 个 NUnit 用例，覆盖材质区域、薄墙高速碰撞与接触效果、快照隔离、相对车辆扫掠和接触去重、红灯停车/状态恢复、信号声相位及闭合路线连续运动。

changed_files:
- `Assets/TingLu/World/StreetWorld.cs`
- `Assets/TingLu/World/StreetWorld.cs.meta`
- `Assets/TingLu/Tests/Upgrade/Role03/StreetWorldTests.cs`
- `Assets/TingLu/Tests/Upgrade/Role03/StreetWorldTests.cs.meta`
- `docs/handoffs/UPGRADE_03.md`

本轮改动：仅调整 `VehicleContactEmitsOnceUntilThePlayerSeparates` 的模拟时序，在每个独立世界步前调用 `Tick(0.02, false)`。生产端每 tick 去重保持不变。

## integration

- 由 00 在唯一运行时入口用正式 `StreetConfig`、`StreetSurface`、`StreetObject` 和 `StreetRoute` 构造 `StreetWorld`，并注入为 `IStreetWorld`。每个模拟步调用 `Tick(delta, paused)`；暂停时内部时间和路线冻结，并抑制车辆碰撞回调。读取物理世界时使用 `Capture()` 的不可变帧。
- 玩家位移经 `Move(from, requested, radius)` 返回安全位置和静态接触效果；车辆接触使用 `VehicleContacts(previousPlayerPosition, currentPlayerPosition, radius)`，将角色和车辆同一模拟步内的移动一并扫掠。调用方按 `Damage` / `StunSeconds` 消费接触效果。
- 检查点需保存 `ExportTraffic()`，恢复时在同一组路线定义上调用 `ImportTraffic(state)`。`MaterialAt(position)` 在无覆盖表面处返回 `null`。
- 本角色遵照 00 指示未新增单独 query helper；遮挡查询可直接消费 `Capture()` 提供的对象几何和遮挡字段。

## verification

- `StreetWorldTests` 有 8 个 NUnit 用例。本轮没有启动 Unity；新测试时序尚未编译或执行，状态 `NOT_RUN`。
- 历史证据 `output/unity-verification/20261015-150041-b5472da8/editmode.xml` 中，Role03 为 7 过 1 失败；失败在原测试第 114 行，原因是该测试在同一 `Tick` 内多次调用 `VehicleContacts`，但契约限制一 tick 最多一次事故。此历史失败保留，不覆盖为通过。
- READY_FOR_UNITY 最短过滤：`TingLu.Tests.Upgrade.Role03.StreetWorldTests.VehicleContactEmitsOnceUntilThePlayerSeparates`；通过后运行 `TingLu.Tests.Upgrade.Role03`。
- 此处没有宣称车辆/信号表现已在实际游戏画面或完整运行时接线验证。

## limitations / contract_requests / next_dependencies

- `StreetRoute.Offset` 的含义已由本实现按初始累计距离解释；旧关卡若曾将其作为时间或相位偏移，须由 00 在组装路线数据时核对。
- 信号控制路线若缺少 `StopLineDistance` 会在构造世界时抛出参数异常；超出路线长度的停车线也会被拒绝。
- 无额外公共辅助类型或依赖变更请求。共享契约中的停车线、接触伤害/眩晕字段已由 00 提供，本角色仅消费这些字段。
- 下一步：00 在排他的统一 Unity 窗口编译并运行上述单测及 Role03 程序集；通过后再与 04 感知/遮挡、运行时路线配置和检查点恢复联调。
