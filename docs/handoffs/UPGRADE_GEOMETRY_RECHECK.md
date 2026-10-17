# 几何渲染回查交接

- `status`: `READY_FOR_UNITY_REVIEW`；本次源码已冻结，等待总控统一运行 Unity。
- `model/effort`: 请求配置为 `gpt-6-luna/xhigh`；当前执行工具没有可独立核验的实际 provider 元数据，不能将请求配置记作已确认的实际模型。
- `contract_version`: `StreetContract 1.0.0`。

## completed

- `StreetRenderer.Triangulate` 改为沿原多边形边界进行扫描线剖分，并在非相邻边相交处加入扫描切分点。凹口和裁剪后自接触的边界按 even-odd 规则配对；三角形仍受输入多边形约束，不扩张 Perception coverage，也不改 Memory、遮挡或未知区域规则。
- Role08 增加凹形未知凹口不被填充、Memory 合并产生的自接触 asphalt 面完整渲染的回归；长共线裁剪顶点用面积与边界样本断言验证。
- 使用正式地图出生点路径检查 `StreetLevelFactory -> StreetPerception.Probe -> StreetMemory.Apply -> StreetRenderer.Triangulate`。探针输入所用 Player 侧地图/Perception/Memory 程序集来自 `output/unity-verification/20261015-221003-16aa10b1`；Presentation 使用本次源码的离线 Mono 编译结果。这里是几何探针，不是 Unity NUnit、当前源码 Player 构建或画面验收。

## changed_files

- `Assets/TingLu/Presentation/StreetRenderer.cs`
- `Assets/TingLu/Tests/Upgrade/Role08/StreetRendererTests.cs`
- `Assets/TingLu/Memory/StreetMemory.cs`
- `Assets/TingLu/Tests/Upgrade/Role05/StreetMemoryTests.cs`
- `docs/handoffs/UPGRADE_GEOMETRY_RECHECK.md`

诊断辅助源码保留在 `output/pure-probe/ActualStreetProbeDiag.cs`、`IncomingBatchCoverageDiagnostic.cs`、`MemoryMergeBatchDiagnostic.cs` 和 `RunCurrentMemoryRoute.ps1`；凹口、自接触面和缩减真实 tactile 面片 fixture 已包含在 Role08 测试中。没有修改 `StreetPerception.cs`、共享契约/配置或其他角色文件。

## integration

不需改接线或公共接口。总控应在当前源码冻结状态下验证新版 Player：出生视角的径向黑色楔形缺片应消失，凹形已知面外的未知凹口仍须保持黑色。旧 `20261015-221003` Player 只作为真实上游输入程序集来源，不是本修复的构建或画面证据。

## tests

- 正式出生点 Perception：1281 个输入面，1229 个生成网格；其余为退化小碎片，面积合计约 `0.00001135 m²`。生成网格最大面积误差约 `2.65e-6 m²`，面积超差 0，三角形边界样本越界 0。
- 对应 Memory：323 个输入面，291 个生成网格；退化小碎片面积合计约 `0.00000610 m²`。生成网格最大面积误差约 `3.42e-6 m²`，面积超差 0，边界样本越界 0。
- 合成凹口 fixture 面积误差 0；真实坐标自接触 asphalt fixture 面积误差约 `2.71e-6 m²`；缩减的真实 tactile fixture 面积误差约 `3.43e-7 m²`。以上 fixture 的边界样本越界均为 0。
- 探针和 fixture 数值是离线几何检查；样本检查不等同于完整形式化几何证明。
- Unity EditMode / PlayMode、构建以及实际 Player 画面检查：`NOT_RUN`。旧有测试或构建结果均不作为本次源码验证证据。

## limitations

- 仍需总控检查新构建画面，确认径向楔形缺片消失且未知凹口保持黑色；离线三角化指标无法代替该视觉检查。
- 少量退化输入面仍返回空网格；其上游面积总量如上，当前未发现大块丢面。Unity 运行性能尚未测量，任何性能扩展后置。
- 本交接只覆盖渲染三角化修复与局部回归，不代表 A01–A31、A32–A34 或街区整体验收通过。

## contract_requests

- 无。

## next_dependencies

- 总控接管源码冻结状态，统一运行 Role08 及必要的 Role04 / 集成 Unity 检查。
- 总控用当前源码重建 Player 并检查正式出生画面；记录 Unity 日志、构建结果及截图。若 Unity 测试失败或实际画面仍有缺片，再解冻并交回精确失败证据。

## 2026-10-16 degenerate polygon route follow-up

- `status`: `READY_FOR_UNITY_REVIEW`; Assets frozen for unified verification.
- The A25/A30 PlayMode failure stack pointed from `StreetGeometry.CopyPolygon` through `StreetKnownSurface` and `StreetMemory.MergeTouchedSurfaces`. The numerical cause was float shoelace cancellation at translated coordinates: a small polygon could pass the absolute-area guard, then have its sign change or collapse when reversed. `StreetGeometry.Area` now accumulates in double precision relative to a local vertex while retaining its float return type and existing threshold. Role00 contains translated winding/copy and truly-collinear regressions.
- The same real-route replay exposed a separate runtime cost that blocked reaching the failure site promptly. At t=8.28, Perception emitted 19,502 surface pieces and `Memory.Apply` did not finish. `BuildKnownSurfaces` now skips subtraction when the clipped candidate and prior claim have no valid-area intersection, and stores the original convex source piece as the claim mask. Since each candidate is already inside its sector cell, subtracting the original source piece is equivalent to subtracting its cell-clipped intersection; this avoids repeated radial clip seams without changing priority order or the minimum-area policy.
- Role04 adds disjoint-priority regressions for per-cell fragment growth and lower-priority material area preservation. The existing priority/coverage-area test remains in place.
- Pure-source route replay after the Perception change: at t=8.28, 1,766 emitted surfaces; total `Probe+Apply` elapsed 472 ms; the route reached its end in about 2 seconds. The preceding replay with the Area correction but before the Perception optimization emitted 19,502 surfaces and stalled in `Memory.Apply`. These are offline source-harness measurements, not Unity test evidence.
- The 20261016-021201 PlayMode run was stopped after prolonged first-screenshot stall and did not produce a completed XML result. Current Role00/Role04 tests and the A25/A30 PlayMode acceptance test still require the controller's unified Unity run; do not report the earlier run as a pass.

## 2026-10-16 memory merge protection follow-up

- `status`: `READY_FOR_UNITY_REVIEW`; Assets are frozen for the controller's unified Unity sample run. This status means handoff-ready, not a performance acceptance.
- `StreetMemory.MergeTouchedSurfaces` keeps failed edge-pair results so an unchanged candidate is not retried, rejects a candidate pair with multiple shared boundary segments, and rejects a merged contour that repeats a non-adjacent vertex. `Apply` builds the next surface/object/frame state before committing it, so an exception does not partially update published memory.
- The earlier `472 ms` figure above was elapsed time from the probe callback through both `StreetPerception.Probe` and `StreetMemory.Apply` (`Probe+Apply`), not `Apply` alone.
- Updated pure-source regression diagnostics pass: an 8×8 adjacent batch preserves area 64 and deterministic output; overlapping old/new observations preserve total area 96 and old/new metadata; a local re-probe preserves area 96 and updates only its local metadata; and polygons with two shared collinear edge segments remain two disjoint area-4 pieces without repeated non-adjacent vertices.
- Current-source pure route replay reaches t=19.34. At t=8.28, 1,766 emitted surfaces, Perception 21 ms, total `Probe+Apply` 457 ms, memory 355 surfaces. At t=12.42, 908 emitted surfaces, Perception 15 ms, total 170 ms, memory 787. At t=16.56, 1,631 emitted surfaces, Perception 16 ms, total 135 ms, memory 1,259. These are one local offline sample, not a hard latency guarantee or Unity evidence; t=8.28 remains expensive and needs Unity timing before any performance conclusion.
- `RunRole05MemoryTests.ps1` could not compile tests in this PowerShell host because its explicit framework references fail to resolve forwarded `System.Private.CoreLib` types. The equivalent geometry cases were exercised by the pure-source diagnostics; this tool issue is not a source failure. Role05 Unity tests remain for the controller's run.
- The 13-sample Unity verification, Player build/visual review, and actual Unity tail-latency measurement remain pending. If runtime evidence shows unacceptable cost, revisit these merge checks with the measured profile while preserving geometry correctness.
