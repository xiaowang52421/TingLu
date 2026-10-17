# R15 灯态专项交接

日期：2026-10-17

本次专项只修改了四个测试文件，未修改生产源码、其他 Role08 文件或共享状态文档：

- `Assets/TingLu/Tests/Upgrade/Role03/StreetWorldTests.cs`
- `Assets/TingLu/Tests/Upgrade/Role04/StreetPerceptionTests.cs`
- `Assets/TingLu/Tests/Upgrade/Role08/StreetRenderPlanTests.cs`
- `Assets/TingLu/Tests/Upgrade/Integration/StreetSessionIntegrationTests.cs`

## 新增覆盖

- Role03 使用 `StreetLevelFactory.Create()` 的真实十字路口，断言 NS 绿、NS→clearance、EW 绿、EW→clearance 和完整周期边界的实际灯态；另断言 `ExportTraffic`/`ImportTraffic` 在 EW 绿时重新派生灯态，不保留初始 NS 绿。
- Role04 使用真实地图的两个灯具，断言未探明方向不返回灯具和灯态；灯具在无遮挡扇区内时返回正确对象、位置、当前颜色及 0.8 秒短期过期时间；插入测试遮挡体后灯具对象和灯态均不泄漏；在 t=0、t=14 clearance、t=17 EW 绿重新探测并检查颜色。
- Role08 使用真实地图的 EW 灯具轮廓，断言新鲜 EW 绿观察渲染绿色轮廓，观察过期后不再渲染红/绿灯态；恢复交通状态后没有新探测时不复活旧颜色，重新探测 clearance 后渲染红色。
- Integration 使用 `Player.Restore` 将玩家定位到真实灯具附近，作为信号生命周期定位 fixture（不作为实走路线证据），通过正常 `Session.Tick(probe)` 产生 NS 观察，等待相位过期后断言不经 Probe 不重建，再经 Retry 和新 Session Continue 断言缓存灯态均为空。

## 待主控运行

Unity 未由本角色启动。主控需要先为测试程序集补齐真实地图依赖：Role03/Role04 增加 `TingLu.Levels`，Role08 增加 `TingLu.Levels` 和 `TingLu.World`，然后定向运行：

- `TingLu.Tests.Upgrade.Role03.StreetWorldTests.RealCrossroadsSignalsFollowBothGreenRedAndClearanceBoundaries`
- `TingLu.Tests.Upgrade.Role03.StreetWorldTests.RealCrossroadsTrafficRestoreRecomputesLampStateInsteadOfKeepingOldPhase`
- `TingLu.Tests.Upgrade.Role04.StreetPerceptionTests.RealCrossroadsLampIsObservedOnlyWhenItsHousingIsInsideUnoccludedProbeCoverage`
- `TingLu.Tests.Upgrade.Role04.StreetPerceptionTests.RealCrossroadsProbeReportsCurrentRedAndGreenAtSignalPhaseChanges`
- `TingLu.Tests.Upgrade.Role08.StreetRenderPlanTests.RealCrossroadsLampShowsOnlyFreshSignalInputAndDropsItAfterExpiryOrRestore`
- `TingLu.Tests.Upgrade.Integration.StreetSessionIntegrationTests.SignalObservationsExpireAndRetryThenContinueNeverResurrectAnOldLampState`

预期：以上 6 项均 Passed，且没有编译、导入、`NullReferenceException`、`MissingReferenceException` 或 shader 错误。若真实地图探测几何的实际边界与测试假设不符，应保留失败证据并由主控审核是否调整断言或生产实现。

## 未覆盖边界

Integration 用 Retry 覆盖 Session 清缓存，并经存档 Continue 验证旧灯态不复活；没有用真实车辆碰撞触发该恢复路径，也没有把本专项定位 fixture 当作斑马线实走验收。
