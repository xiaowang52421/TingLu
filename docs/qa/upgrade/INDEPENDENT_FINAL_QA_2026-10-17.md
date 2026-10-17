# 独立最终 QA（2026-10-17）

## 结论

当前冻结版本是**可运行的交付检查点，自动化与确定性 Player 证据闭环，但不是所有需求都已由机器或真人完成验收**。本报告只读核对主控提供的最终产物；除本文件外未修改 Assets、tools、共享验收文档、原项目或原存档，也未启动 Unity 或 Player。

最新统一链使用源码指纹 `C22DCAC72C9FEFE366AB61E4A6A845CFDFED9285F54BC5957BE1520269D27EE2`：

| 阶段 | 结果 | 证据 |
|---|---|---|
| EditMode | PASS，184/184 | `output/unity-verification/20261017-012625-474ab9b5/results.json` |
| PlayMode | PASS，2/2 | `output/unity-verification/20261017-012658-aed888bb/results.json` |
| Windows 构建 | PASS | `output/unity-verification/20261017-012714-c16f466d/results.json` |
| Player 探针 | PASS（两分辨率） | `output/street-verification/20261017-012750-ee20f67a/results.json` |

三个 Unity 阶段均为 `sourceUnchangedDuringStage=true`。三份 `source-manifest.json` 相互一致，均含 546 项；按 manifest 对当前树逐项复算，缺失 0、SHA-256 不匹配 0。阶段日志和 Player 日志中 `Shader error in`、`Unsupported shader`、`NullReferenceException`、`MissingReferenceException`、C# 编译错误均为 0。

## 构建、包和原件保护

交付目录为 [听路试玩_2026-10-17_街区交付复核版](../../../output/听路试玩_2026-10-17_街区交付复核版/)。`package-manifest.json` 报告 235 个构建文件、源构建与复制目录逐项匹配，清单 SHA-256 为 `F58082522A03CA07A96C38FE94FD05AE1450149D7A451D7C3211E497D90E4F20`；本地复算无缺失、无不匹配。交付 `TingLu.exe` SHA-256 为 `CC858EB9A7E2DB2D414632600041100BD4840DD2F016593A242B145AFE6C5941`。

[output/recheck-original-protection-final.json](../../../output/recheck-original-protection-final.json) 报告原项目 `D:\听路` 的 246 个源文件、7 个存档文件和 464 个旧构建元数据全部保持不变。

## 自动化范围

EditMode 的 184 项包含并通过了与本轮缺口直接相关的断言，包括：

- `A25_PauseFreezesTheWholeSessionAndResumeSamplesTheRestoredTimeline`、真实红绿灯相位/灯态过期与遮挡许可；
- `EightRampsConnectRoadAndSidewalkAtTheCorrectPublicEdges`、`EveryCrosswalkDirectionIsWalkableWithAContinuousEffectiveHeightProfile`、坡道高程存档往返；
- `KnownHeightRaisesContourAndLocalEdgesWithoutExtrudingKnownGround`、玩家箭头和灯色轮廓高程、色盲红绿状态清理；
- 右键短按边沿、按住/释放、失焦清输入；
- `RealPoolResetCancelsBackoffAndRejectsOldGenerationBeforeCurrentGenerationStarts`、`RealPoolRetainsCriticalThreatAndStartsAmbienceAfterReleaseAndSimulationRetry`、`RealUnityPoolFullSpeechRetriesAfterReleaseWhilePausedAndTimeScaleIsZero`。

PlayMode 的 2 项中，`A25AndA30_RealSessionPausesRecoversContinuesAndReachesGoal` 与 `StreetCrossroadsLoadsOneEntryCameraAndListener` 均通过。它们证明同一生产 Session 的暂停冻结、恢复、碰撞/路线和到达目标的自动化行为，不能替代真人键鼠操作。

## Player metadata 和截图核对

Player 报告的范围明确为 `Rendered development Player; deterministic production probe, not human input or listening`。主分辨率为 1280×720，备用分辨率为 960×640；两档均为全屏窗口、场景 `StreetCrossroads`、关卡 ID `street.crossroads.v1`，并生成菜单、探索、暂停、恢复、NS 灯态/坡道、东侧落点六类 PNG。两档尺寸和非黑内容检查均通过。

两分辨率的 metadata 都记录：

- `menuPlayingBeforePause=true`、`menuPaused=true`、`menuPlayingAfterResume=true`；
- `pauseFrozen=true`、`resumeSnapshotPreserved=true`、`resumeAdvanced=true`；
- `routePassed=true`，经过 `checkpoint.west.approach` 和 `checkpoint.east.landing`；
- `nsSignalLive=true`，探针记录 `street.signal.ns`、`signalPhase=ew`、当前 NS 状态为红；
- 单 Camera、单 AudioListener、18 个场景对象，软件语音状态为 `STARTED`。

暂停探针使用的是报告所述的生产 `StreetSession.Tick(StreetInputFrame.PausePressed)` 路径；它没有注入物理键盘，所以不能证明真实按键手感、焦点变化或指针锁定。

最终图像逐张读取结果：

- 1280×720 与 960×640 的主菜单按钮完整；最终版本的暂停标题“先歇一会儿”和字幕“接着走”字形完整，没有第一候选中的纵向裁切。
- 探索/恢复图中玩家箭头可见，已知道路、人行道、盲道/斑马线格纹连续，未探明区域保持黑色；未见品红材质或截图级 shader 异常。
- NS 灯态/坡道图中能看到当前视野内的道路、斑马线、坡道几何和一处红色信号灯轮廓；东侧落点图中能看到落点及相邻过街区域。两分辨率画面没有裁切出菜单区域。
- `explored.png` 与 `resumed.png` 在两档分别使用相同 SHA-256（1280×720：`C21401A6…0F6F`；960×640：`E8BC6C79…B7D57`）。这是同一确定性路线的相同画面；恢复推进由状态 metadata 证明，不能把这两张图算作独立视觉恢复证据。

## R15、D12 和验收项边界

R15 的十字路口设施、相位和路线有生产实现、EditMode/PlayMode 自动化及局部 Player 画面。D12 的八片坡道公共端、连续有效高程、轴向和存档字段有 EditMode 断言；当前 Player 图也能看到部分坡道和路沿。依此可判定为**自动化通过、视觉局部通过**，不能扩大为八片坡道逐向人工视觉签收或所有 NS/EW/清空相位的独立画面通过。

A25/A30 在本次 PlayMode 范围内通过；R15/A20、A21、D12 仍是部分闭环，因为完整设施巡检、所有灯态视觉覆盖和真人通行体验未由本证据替代。软件 `STARTED` 只表示提交播放，不能证明耳机/扬声器实际出声、物理延迟或人是否听见提示。

## 保留事项

- 物理出声延迟、设备听辨、真人无障碍、真实鼠标手感、Alt+Tab、触屏连接未运行；
- 启动失败故障注入页面的独立 Player 交互尚未运行验证；
- 当前实走画面覆盖 NS 灯和部分坡道，不覆盖八片坡道逐向截图、NS/EW/清空期全部相位截图；
- 确定性 Player 探针不是完整真人路线试玩，不能把自动化 PASS 改写为所有 R/D/A 项均已真人验收。

因此，本次交付可作为当前源码、构建和试玩包的可运行检查点；`requirementsComplete` 应保持 `false`，直到上述人工和完整视觉范围另行取得证据。

