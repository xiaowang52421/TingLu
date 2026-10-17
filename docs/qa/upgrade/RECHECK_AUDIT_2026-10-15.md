# 运行链独立复核（2026-10-15）

## 范围与结论

只读核对了 `docs/upgrade/01_需求与规则.md`、`IMPLEMENTATION_CONTRACT.md`、`04_验收清单.md`、`docs/qa/upgrade/ACCEPTANCE.md`，以及街区运行链的 `StreetEntry`、`StreetSession`、输入、角色、世界、感知、记忆、存档、HUD、RenderPlan 和对应自动化测试。没有运行 Unity，也没有改生产代码、测试或现有状态报告。

在这次复核的代码与证据中，**未找到足以确认的新增运行时阻断或实质 bug**。恢复/暂停/续玩/终点主链有直接实现和测试支撑；未覆盖的人工体验和 A26 完整记忆续玩仍维持 `NOT_RUN`，不能据此宣称已经验收。

## 定向核查

- **车辆恢复事务：**`StreetPlayerController.HandleVehicleContact` 同步发出失败事件，并在事件后立即结束当前 Tick；`StreetSession.Tick` 以 Generation 变化检测恢复并退出子步循环。恢复会暂停时钟与音频、递增 Generation、恢复检查点交通与角色、将记忆标旧、清空临时感知后重采声音。实现位置：`Assets/TingLu/Player/StreetPlayerController.cs:227`、`Assets/TingLu/Bootstrap/Integration/StreetSession.cs:102`、`Assets/TingLu/Bootstrap/Integration/StreetSession.cs:128`。现有 A30 路线用例断言一次真实车辆碰撞后返回东侧检查点；EditMode 113/113 也包含同步恢复单测。它没有覆盖无检查点和连续恢复，验收报告相应仍为 `NOT_RUN`。
- **暂停与继续：**暂停回调在打开暂停菜单前冻结模拟时钟及播放，并保存当前快照；恢复时先恢复模拟时钟、刷新听觉/感知、通知输入清理，再解除音频暂停。菜单切换后 Entry 再启用游戏输入。对应 `StreetSession.cs:152`、`StreetSession.cs:158`、`StreetEntry.cs:65`。`A25_PauseFreezesTheWholeSessionAndResumeSamplesTheRestoredTimeline` 对时间、交通、玩家状态、探测冷却、感知采样时刻和音频暂停做了断言；Unity结果为 `output/unity-verification/20261015-232743-a02fe7f5/results.json`（EditMode 113/113）。真实窗口焦点、鼠标释放/重新捕获仍未实测。
- **Continue 与 Generation：**继续时会重新建立世界/角色/记忆和音频控制器，校验关卡 ID，导入交通并按存档模拟时刻恢复角色；存档 Generation 取 `max(本次启动代数, 存档代数 + 1)`，随后重置播放后端，首个声学同步使用同一代数。`StreetSession.cs:57`、`StreetSession.cs:75`、`StreetSession.cs:82`、`StreetSession.cs:150`。现有测试证明可从菜单 Continue 加载同一运行位置；没有发现代数倒退或旧声源续留的可达路径。
- **Finished 存档：**继续已完成的存档会先重建运行数据，再将玩家置为 Finished、暂停时钟并打开结果页；该分支不解除此前设置的播放暂停。`StreetSession.cs:87`。结果页仍可重试或返回首页；没有发现这条分支会重新进入移动状态的代码路径。现有测试未单独验证“完成后退出再 Continue”，所以这项属于未覆盖场景，不报成已复现 bug。
- **启动异常与返回首页：**`Start` 的异常处理先冻结时钟/音频并将 `Running=false`，再调用菜单返回；由此 `ReturnToMenu` 的 `if (Running) Save()` 不会把半初始化对象再次写入存档。正常返回首页则在清除运行状态前保存。`StreetSession.cs:91`、`StreetSession.cs:170`。未发现“启动异常导致二次保存”的现有可达问题。
- **未知信息边界：**RenderPlan 只遍历 `StreetMemoryFrame` 的表面/障碍，以及 `PerceptionFrame` 的有限期声音定位和许可局部几何；它不接收 World 或 Level。`StreetRenderPlan.cs:60`、`StreetRenderPlan.cs:72`、`StreetRenderPlan.cs:91`；Entry 传入的是 Session 的记忆/感知帧，`StreetEntry.cs:76`。静态依赖方向符合契约，源代码复核未发现从真值场景直接绘制未知地形的路径。画面/字幕/设备的实际泄露检查仍须人工完成。
- **终点与清理：**玩家进入配置终点半径时设置 `Finished`、冻结时钟/音频、保存并打开结果页；`Tick` 随即停止后续子步。`StreetSession.cs:106`、`StreetSession.cs:120`。`StreetEntry` 在退出时保存，销毁 Session 时走 `ReturnToMenu`/Dispose（`StreetEntry.cs:87`）。未发现结果页后继续模拟或销毁时丢失正常运行存档的路径。现有 A30 用例走到终点并断言 Finished，但完成存档的二次启动未单测。

## 证据边界与待补验收

- 最新现场 EditMode 结果为 113/113 通过；此项不等于最新 Player 构建、视觉或真人验收。复核期间未运行 Unity。
- `ACCEPTANCE.md` 记录 A26 为 `NOT_RUN`：现有续玩用例只断言 Continue 恢复位置与感知时刻，没有断言碰撞恢复后永久探索保留、临时物体记录标旧、活动声源从当前世界重算且不从存档恢复真值。建议将这些作为 A26 的独立可复现断言补齐后再关闭。
- A24 的无检查点恢复/连续接触，A28 的 Alt+Tab 和指针生命周期，以及 A29 的正式画面信息泄露仍属 `NOT_RUN`；代码审查不能替代这些实测。
- 现有 `ACCEPTANCE.md` 的画面结论引用 20:25/20:26 的截图；目录中另有 22:17 的 Player probe 截图，使用的仍是 22:10 构建。阶段报告尚未纳入该较新截图。它不构成新构建或人工验收证据，后续渲染修复完成后应以同版 Player 重新截图、复核并更新结论。
- **2026-10-16 补充：**按主控后续授权，已在 `Assets/TingLu/Tests/Upgrade/Integration/StreetSessionIntegrationTests.cs` 增加 `ContinueRestoresStaleExplorationAndResamplesLiveSourcesFromWorld`：生产 Session 从出生点探测后，沿现有关卡路线绕行、到检查点并进入车辆路线等待一次真实碰撞恢复；断言永久地面未回滚、临时自行车局部轮廓仍保留并标旧。随后写入临时隔离存档，由新 Session 执行 Continue，断言记忆面积/轮廓及 `CurrentUntil=0`，并断言活动声学按恢复后的世界对象和模拟时刻重算。该测试未在本次复核中运行；它只补一个 A26 自动化切片，不能据此把 A26 整项改为 PASS。

