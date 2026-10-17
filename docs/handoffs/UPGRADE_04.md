# UPGRADE_04：扇形探测与感知

status: PARTIAL

role/model/effort: role04；目标为 gpt-6-luna/xhigh；本次执行环境未提供可核实的实际 model、provider 或 effort 元数据，不能确认是否与目标相符。

contract_version: StreetContract 1.0.0

## completed

- 新增 `StreetPerception`，以冻结的 `StreetProbeRequest` 和同采样时刻 `StreetWorldFrame` 生成扇形覆盖；对阻挡多边形顶点及其与探测半径的交点加入角度事件，并按中点边界偏差细分，避免薄墙、窄缝及半径交界漏采。
- 覆盖仅由扇形内可见区域构成；永久地面按 `StreetSurface.Priority` 从高到低裁成交叠不重复的局部片段；永久/临时对象只输出实际覆盖到的轮廓片段，活动对象不产生几何记忆。
- 身体接触仅裁接触点 `ContactRadius` 内的对象局部轮廓，不生成地面覆盖；车辆接触及活动对象不输出几何。
- 声学投影只接受活动、可辨且未过期的 06 候选；不重算距离或遮挡，不推断位置/类别，不生成覆盖或轮廓。投影帧由 `Current` 暴露，`Clear()` 会发布新的空帧。
- 已编写 12 项 Role04 NUnit 测试，覆盖扇形边界、薄墙、窄缝、局部墙体轮廓、材质优先级、接触局部性、车辆接触、同帧约束、声学投影、清空及共享街道 fixture。

## changed_files

- `Assets/TingLu/Perception/StreetPerception.cs`
- `Assets/TingLu/Tests/Upgrade/Role04/StreetPerceptionTests.cs`
- `Assets/TingLu/Tests/Upgrade/Role04/StreetPerceptionTests.cs.meta`
- `docs/handoffs/UPGRADE_04.md`

本轮改动：`BuildKnownSurfaces` 将优先级裁差片段按 `ProbeCell` 隔离。覆盖扇区由互不重叠的角向 cell 划分，因此避免将已在其他 cell 输出的凸片反复拿来做差集；同一 cell 内高优先级面仍先输出、低优先级面仍做精确局部差集。扇形生成、遮挡裁剪、连续几何和许可规则未改。

## integration

- 02/00 在成功探测时传入探测时刻冻结的 `StreetProbeRequest` 与同一采样时刻的 `StreetWorldFrame`，将结果交给 05 的记忆入口；覆盖、地面片段和对象轮廓均保留自身采样时间与有效期。
- 02/00 在身体接触时传入匹配世界快照的 `StreetContact`；车辆接触返回空几何。
- 06 提供冻结的 `AcousticQueryResult` 候选；00 将 `ProjectAcoustics` 返回的 `PerceptionFrame` 交给 05/08。声音位置来自候选快照，声音不确认地面或完整对象几何。
- 尚未修改 Bootstrap 或其他角色文件，运行时接线待 00 集成。

## tests

- `StreetPerceptionTests`：12 项 NUnit 已编写。本轮没有启动 Unity；本轮改动编译与测试均为 `NOT_RUN`。
- `Assets/TingLu/Perception/StreetPerception.cs.meta` 当前已存在，GUID 为 `7bcac160616e45a1b8380270e141cb7c`；本轮未修改该 meta。旧 handoff 对其“待补”的记录已过时。
- 历史证据 `output/unity-verification/20261015-145448-79fd6c2c/results.json` 记录 EditMode 过滤运行超时 300 秒；日志确认程序集编译成功并进入 Test Runner，但没有 XML 或用例级进度，故具体卡住用例和超时根因仍未确认。后续排除 Role04 的运行不能单独证明根因。
- READY_FOR_UNITY 定向过滤建议：先单跑 `TingLu.Tests.Upgrade.Role04.StreetPerceptionTests.HigherPrioritySurfaceOverlaysRoadWithoutOverlappingOutputAreas` 验证按 cell 裁差及性能；随后单跑 `TingLu.Tests.Upgrade.Role04.StreetPerceptionTests.AngularEventsPreserveARealNarrowGapBetweenOpaqueWalls`、`TingLu.Tests.Upgrade.Role04.StreetPerceptionTests.PartiallyCoveredWallEmitsOnlyItsVisibleContour` 与 `TingLu.Tests.Upgrade.Role04.StreetPerceptionTests.SharedRoadFixtureCanBeProbedAndItsActiveObjectHasNoGeometryMemory`。仍慢时按单用例过滤定位，避免先跑全量。
- 静态风险仍在：`AddAdaptiveCell` 在偏差超过容差时递归细分，最多深度 18；异常边界事件可能导致 cell 数量增长。当前日志无法证实它是否触发。若优先级面测试不再卡顿而其他几何项仍超时，应由 00 用逐项过滤或性能采样确认，不应移除连续几何/遮挡断言。
- 扇形覆盖、实际画面及 05/08 联调：`NOT_RUN`，无运行或画面证据。

## limitations

- 本轮性能调整是基于可见的跨 cell 重复凸片差集热点作的定向优化，不证明其为 300 秒超时的唯一原因；需 Unity 定向运行确认。
- 声学候选正确性与实际可听性由 06 验证；渲染泄漏由 08 验证；本交接不代表 A 项整体验收通过。
- 实际 model/provider/effort 不可核实。

## contract_requests

- 无。未修改共享契约、配置或 asmdef。

## next_dependencies

- 00 在统一 Unity 窗口先运行上述定向用例并记录单项时长；结果通过且时间合理后，再运行 Role04 程序集并检查 Console/import errors。
- 00 集成 02/03/05/06/08 后复核采样时刻、遮挡覆盖、永久材质优先级、接触及声学帧的消费边界。
