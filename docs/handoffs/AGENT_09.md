# AGENT 09 — 零代码图形化关卡编辑器

- **status**: UNITY_API_VALIDATED（真实 Unity 编译及 20/20 EditMode 通过，其中 9 项编辑器专项；人工 GUI 操作仍待验收）
- **contract_version**: 0.2.0，未修改公共契约或玩法配置。
- **date**: 2026-10-14

## completed

版本化 JSON 内容 DTO、稳定标识、全关及单对象复制引用重映射、草稿安全保存、备份、未来/损坏版本覆盖保护；中文几何/引用/素材/声音/合法触发检查和运行生成上限；导入真实绕杆首关模板与独立第二关示例。

Unity EditorWindow 菜单 `TingLu/关卡编辑器`，中文三栏工作台、首次引导、素材拖放、选择/移动/旋转/删除、矩形区域、连续盲道及节点插入/删除、平移缩放、全图、吸附、对象列表定位、表单、受控声音选择、有限触发与动作、撤销重做/快捷键、错误定位、未保存状态及保存/放弃/取消流程。Editor 代码位于独立 Editor-only asmdef。

正式 DemoSession 支持编译后的只读关卡定义，注入出生/朝向、对象/地面/盲道、对白、声音、检查点、碎片和终点；自定义关卡使用自身 ID 和前置引用，不使用默认门卫约束或首关行人坐标。动态人物继续保持 Never memory。默认首关构造与 9 项装配测试保持兼容。

试玩将当前 JSON 序列化进专用临时场景 DemoEntry 实例，经正式游戏装配运行；使用独立临时目录的 LocalSaveService，隔离 Settings/Progress/Resume 的加载、更新、结算、返回菜单与 Dispose。结束 Play 后恢复原 playModeStartScene，删除临时场景并打开关卡工作台；存档调试证据保留。

## changed_files

- `Assets/TingLu/Levels/LevelDocument.cs` / `.meta`：数据、素材目录与 JSON 安全读写。
- `Assets/TingLu/Levels/LevelValidator.cs` / `.meta`：中文错误/警告，几何启发式与 5000 运行分段上限。
- `Assets/TingLu/Levels/LevelCompiler.cs` / `.meta`：内容数据编译为独立只读世界/流程数据。
- `Assets/TingLu/Levels/FirstStepTemplate.cs` / `.meta`：真实绕杆模板、独立标识与空白模板。
- `Assets/TingLu/Levels/FirstStepLevel.cs`、`FirstStepFlow.cs`：兼容运行定义和配置流程。
- `Assets/TingLu/Levels/Editor/LevelEditorWindow.cs`、`LevelEditBuffer.cs`、`LevelPreviewHost.cs`、`TingLu.LevelEditor.Editor.asmdef` 及各 `.meta`。
- `Assets/TingLu/Levels/Data/FirstStepTemplate.json`、`SecondLevelExample.json` 及 `.meta`，Editor/Data 目录 `.meta`。
- `Assets/TingLu/Bootstrap/Integration/DemoSession.cs`：正式运行注入与音效请求映射。
- `Assets/TingLu/Bootstrap/Runtime/DemoEntry.cs`、`TingLu.Bootstrap.asmdef`：TextAsset/序列化试玩配置与隔离存档入口；增加 Levels 依赖，无循环。
- `Tests/LevelEditor/LevelEditorChecks.cs`、`tools/Verify-LevelEditor.ps1`。
- `docs/LEVEL_EDITOR_GUIDE.md`、本交接与 `docs/qa/level-editor-*` 证据。

## integration

`LevelDocument → LevelValidator → LevelCompiler → FirstStepLevel（只读运行定义）→ DemoSession`。DTO 改动不会修改已编译定义；业务仍复用 WorldRegistry、PlayerController、PerceptionService、StaticMemoryStore、CheckpointService、LocalSaveService 和 AccessibleMenu。

默认 DemoEntry 未指定 Level Asset 时保持原首关。关卡编辑器通过 LevelPreviewHost 生成专属临时场景，在 DemoEntry.Start 前序列化 JSON 与随机临时存档目录；不依赖会因 Unity 域重载丢失的静态变量。世界对象对白强制合法感知；独立 story 可作为区域自语或教学；碎片自身必须合法感知。终点安排在同次 Tick 的普通事件/收集之后，前置 ID 由 UI 控制并检查无循环。

`Save(requireValid:false)` 用于可重开的草稿；`Compile` 始终 RequireValid。普通保存保持 ID，“复制关卡”及不同路径的另存为生成新身份并重映射引用。未知版本/未知素材/空节点/不可绘制几何在读取时拒绝，不破坏原文件。

## tests

2026-10-14 最终冻结候选：`pwsh -NoProfile -File tools/Verify-LevelEditorUnity.ps1` 在 C 盘中文空格目录的独立工程副本执行，启动前搬迁整个关卡模块及声纹材质，启动 cwd 位于工程外。Unity 2022.3.62f1c1 正常退出（exitCode 0），20/20 PASS、0 FAIL、0 SKIP，其中 9 项编辑器专项。覆盖真实窗口、Undo/Redo、安全保存及重开、JSON 移动后 GUID 跟踪保存、配置路线引用与撤销、试玩场景序列化、真正进入/退出 Play Mode、隔离存档、原启动场景及 clean 未命名场景恢复。报告 `docs/qa/level-editor-unity-results.json`；运行前源码快照 `output/level-editor-source-20261014-120451.zip`。工作工程不参与资产搬迁测试。

最终纯 C# 验证 `pwsh -NoProfile -File tools/Verify-LevelEditor.ps1 -ReportPath docs/qa/level-editor-path-regression.json` 为 9 组 PASS；`Tests/Tools/PortabilityChecks.ps1` 为 6 项 PASS。安装二进制改目录后实际产品版本读取、错误版本拒绝及环境变量发现通过，见 `docs/qa/level-editor-install-paths.json`；该安装文件副本未启动 Unity。路径设计见 `docs/handoffs/EDITOR_PATHS.md`。以下是初版历史证据，不能代替上述最新结果。

以下命令均通过，使用 PowerShell 7.6.5；记录播放请求的音频替身用于装配验证，实际 Unity 后端/设备没有运行。

| 命令 | 结果 | 证据 |
| --- | --- | --- |
| `pwsh -NoProfile -File tools/Verify-LevelEditor.ps1` | 7 组 PASS：安全绕杆模板、复制/重映射、安全保存重载、ID、无效几何/引用/披露拒绝、未来保护、草稿、坏 UI 数据、无声音、正式注入、终点顺序与隔离 | `docs/qa/level-editor-results.json`，内含实写临时数据目录 |
| `pwsh -NoProfile -File tools/Verify-Demo.ps1 -ReportPath docs/qa/level-editor-demo.json` | 9 PASS：默认首关、恢复、续玩、完整输入路线三星与三模式回归 | `docs/qa/level-editor-demo.json` |
| `pwsh -NoProfile -File tools/Verify-Project.ps1` | 5 PASS：meta/GUID、程序集单向无环、资源引用与 IMGUI | `docs/qa/level-editor-project.json`（从既有脚本输出保留本轮副本） |
| `pwsh -NoProfile -File Tests/Integration/Acceptance/Run-Acceptance.ps1` | 20 PASS，0 FAIL | `docs/qa/level-editor-acceptance.json`（本轮副本） |
| `pwsh -NoProfile -File Assets/TingLu/Tests/EditMode/PerceptionMemory/Run-PureChecks.ps1` | 10 PASS，动态不入永久记忆、感知/遮挡/盲道/纯音频边界 | `docs/qa/level-editor-perception-memory.txt` |
| `pwsh -NoProfile -File tools/Verify-AudioUi.ps1` | 7 PASS；语音仅 WAV 合成证据，无真实播放 | `docs/qa/level-editor-audio-ui.json`（本轮副本） |

Verify-LevelEditor 同时用 Roslyn 对 Editor 和 DemoEntry 做 C# **语法解析**；这只能确认语法，没有 Unity 程序集，不是 Unity 编译验收。纯 C# 注入验收使用修改到 (50,60) 的出生、90°朝向、新坐标障碍/盲道/门卫/对白/声音/检查点/碎片/终点，保存重载后走生产 DemoSession，证明实际世界及流程读取修改后的内容。

独立只读复核 Agent 再运行 Verify-LevelEditor，7 组通过，证据 `docs/qa/level-editor-independent-review.json`。审查确认命中层次、节点选择、合法感知默认值、独立撤销缓冲 MonoScript、延迟数字输入、ExitGUIException 传播、坏文件保护与运行分段预算均已修正；未报告剩余明确高风险阻断项。该复核同样没有 Unity GUI 运行证据。

## limitations

此前“未检出 Unity”的结论已过时。本机实际运行 Unity 2022.3.62f1c1，并通过工程导入/程序集编译。编辑器自动化 API 专项结果见 `docs/qa/level-editor-unity-results.json`；工作台显示、鼠标拖放/旋转、文字快捷键、未保存关闭弹窗与人工截图仍须按 `docs/qa/LEVEL_EDITOR_ACCEPTANCE.md` 验收，自动测试不能代替它们。

没有独立编辑器 EXE。人物路线已配置化：工作台支持节点绘制、Shift 拖动、坐标/时间/朝向表单、整条路线随人物移动、复制与删除引用及撤销；无路线人物停在所摆位置。正式首关已绑定 FirstStep.json，保留旧存档所用标识。音效仅已注册合成占位，未做编辑器试听或正式声音导入。车辆危险、天气、脚本节点均未展示为可用功能。

盲道限单连续折线、宽度 0.2–0.8 米，与现有脚下感知范围一致；矩形几何检查为启发式，未证明任意地图可达或视障玩家纯音频通关。临时试玩存档保留在操作系统临时目录作为调试证据。

## contract_requests

无。使用现有 0.2.0 契约完成注入；新增类型均关卡内部内容模型。

## next_dependencies

使用已经安装的 Unity 执行人工清单：打开工作台 → 复制并拖放 → 节点绘制与撤销 → 对白/相对绑定 → 保存关闭重开 → 试玩域重载/退出 → 核对正式存档未变化。编辑器专项命令为 `pwsh -NoProfile -File tools/Verify-LevelEditorUnity.ps1`，在独立搬迁项目副本执行真实 Unity API 测试。真实音频听感和纯音频通关须单独验收。只有人工操作证据完成后才能将状态升级为 GUI_VALIDATED。
