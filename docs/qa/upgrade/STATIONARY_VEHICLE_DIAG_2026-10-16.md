# 静止玩家车辆接触边界诊断（2026-10-16）

## 结论

已定位并修复车辆首触偶发漏报。根因在 `StreetWorld.VehicleContacts`：端点重叠判断容许 `radius + DistanceEpsilon`，而扫掠求交按 `radius` 判断。车辆移动的首个子步可能使端点刚好落入重叠容差、扫掠时间却略超出本步，形成 `hit == false`、`overlappingNow` 已包含车辆的状态。代码随后把该车辆写入 `activeVehicleContacts`，下一子步虽然扫掠命中，却因“已接触”被抑制；车辆离开前都不会再产生首次事故事件。

这解释了静止玩家穿车：玩家是否移动不是决定因素，车辆在边界子步的浮点舍入就足以吞掉接触。玩家正常活动状态每个模拟子步都会查询车辆；暂停与恢复仍沿用既有抑制规则。

## 修复

当端点被既有重叠谓词接受、但该步扫掠没有给出 hit 时，将其视为本次首次 hit，并用端点最近多边形点作为接触位置。该处理同时应用于有路线和无路线车辆；接触半径、持续接触去重、同 tick 去重、暂停及 ImportTraffic 重置行为保持既有实现。

## 证据与回归

- `output/pure-probe/RootVehicleEdgeDiagnostic-before.txt`：精确边界场景在修复前 `first=0, next-deeper=0`。
- `output/pure-probe/RootVehicleEdgeDiagnostic-after.txt`：相同纯源码诊断在修复后 `first=1, next-deeper=0`。
- 新增 `VehicleBoundaryOverlapIsReportedOnceThenCanBeReentered`：使用真实移动路线，覆盖容差边界首触、同 tick 重复查询、后续深入不重复、分离后重入，以及暂停和 ImportTraffic 重置语义。
- Unity/EditMode 未在此诊断任务中运行；新增用例保持 NOT_RUN，需由主线程统一 Unity 验证。

## 最小变更位置

- `Assets/TingLu/World/StreetWorld.cs`：端点有效重叠与首触报告同步。
- `Assets/TingLu/Tests/Upgrade/Role03/StreetWorldTests.cs`：增加边界与生命周期回归用例。
