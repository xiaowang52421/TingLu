# 进度复核报告（2026-10-13 之后）

本报告由一次独立复核产生，用于修正 `STATUS.md` 与磁盘实际状态的偏差。所有结论均标注证据来源；未运行的检查一律写"未运行"，不以源码存在代替验收。

## 1. 文档与磁盘的偏差

`docs/STATUS.md` 更新时间停在 **11:31**，但源码修改持续到 **20:49**。以 11:31 的 `output/听路_本轮保存清单_2026-10-13.json`（135 项）比对：

- 清单文件缺失：**0**（快照完整）
- 11:31 之后被改动或新增：**20 个文件**，含 `Contracts/*`、`Audio/AcousticModel.cs`、`Audio/UnityAudioPlayback.cs`、`Levels/FirstStep*`、`Perception/PerceptionService.cs`、`Persistence/LocalSaveService.cs`、`Player/PlayerController.cs`、`World/WorldRegistry.cs`、`Presentation/*`、`docs/CONTRACTS.md`、`docs/DECISIONS.md`、`docs/handoffs/AGENT_01.md`、`docs/handoffs/AGENT_05.md`

契约实际版本已是 **0.2.0**（`ContractVersion.Current`），不再是 STATUS 所写的 0.1.1。

## 2. 环境变更（影响验证可复现性）

| 项 | 11:31 记录 | 当前实测 |
| --- | --- | --- |
| PowerShell 7 (`pwsh`) | 7.6.5 可用 | 复核时**不存在**；本次已重新安装 **7.6.6**（MSIX，`%LOCALAPPDATA%\Microsoft\WindowsApps\pwsh.exe`） |
| .NET SDK | 未检出 | 仍未检出 |
| Roslyn `csc` | — | 独立不存在；仅 ASP.NET Framework 4.0 自带的 C# 5 编译器（`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`） |
| MSBuild | — | 仅 4.8.9037.0（`Framework64`），不支持 SDK 风格工程 |
| Unity | 未检出 | 仍未检出（PATH、`C:\Program Files\Unity\Hub\Editor`、常见 Hub 路径均无） |

**结论**：`pwsh` 缺失期间，两个验证脚本**都跑不动**（代码含 18 处 C# 7 `?? throw`，旧 csc 报"无效的表达式项 throw"）。`docs/qa/core-results.json` 里 `runtime: "7.6.5"` 的那次 PASS 当时**无法复现**。重装后已恢复。

## 3. 本次实际运行的验证

### 3.1 `tools/Verify-Core.ps1` — PASS

命令：`pwsh -NoProfile -File tools/Verify-Core.ps1`
结果：**10/10 通过**，退出码 0。C01–C10 逐项 PASS，`core-results.json` 已刷新（`runtime: 7.6.6`，`unity: NOT_RUN`，`devices: NOT_RUN`）。

### 3.2 纯 C# 程序集联合编译 — PASS

22 个无 Unity 依赖的源文件 + 1 个纯检查文件一次性编译通过，产生 124 个类型。17 个关键实现全部存在（`PlayerController`、`WorldRegistry`、`PerceptionService`、`StaticMemoryStore`、`LocalSaveService`、`CheckpointService`、`AttemptTracker`、`FailureRecovery`、`FirstStepFlow/Level/WorldFactory`、`AcousticModel`、`SpeechPriorityQueue`、`UnifiedInputSource`、`ThreatReceiptGate`、`PresentationFrameComposer`）。

参与编译：Contracts 3、Config 1、Bootstrap Core 3、Input 1、Player 2、World 1、Levels 4、Persistence 1、Memory 1、Perception 2、Audio 2、Presentation 1。

### 3.3 `PerceptionPresentationPureChecks.Run()` — PASS 5 项

`PassiveSoundDoesNotReadWorldIdentity`、`UnknownSoundHasNeutralLocator`、`SemanticStyleAndPaletteAreExplicit`、`MissingBoundaryQueryDoesNotClearOrConfirm`、`NarrowOccluderClipsContourAndCoverage`。

### 3.4 `tools/Verify-AudioUi.ps1` — FAIL（脚本缺陷 + 一项断言不一致）

原脚本只编译 Contracts + `AccessibleMenu.cs`，**漏掉 `SpeechPriorityQueue.cs` 与 `AudioChecks.cs`**，所以 `AudioChecks` 从未真正运行过。本次已补齐编译清单，但暴露：

`MenuChecks` 在"暂停后选择继续"处失败：

- 实现：`AccessibleMenu.Activate(MenuOption.Resume)` → `Invoke(callbacks.Resume)` + `SetPlaying()`，即 `Screen` 变为 `Playing`
- 测试断言（`Tests/Accessibility/MenuChecks.cs:35`）：`resumes == 1 && menu.Screen == MenuScreen.Main`

两者对"继续之后应停在哪个界面"的预期相反。按玩法语义，从暂停返回游戏应是 `Playing`，因此倾向**测试断言写错**，但需 06 负责人确认后二选一修改；在确认前该项**记为失败**。

## 4. 仍未解决的结构性缺口

1. **总装层缺失**：全库检索 `new WorldRegistry(` / `new FirstStepFlow(` / `new UnifiedInputSource(` / `new PlayerController(` 等构造调用，**在 `Assets/TingLu` 生产代码中为 0 处**，仅存在于测试源码。`Bootstrap/Runtime/BootstrapStatus.cs` 只有 12 行，输出 "Gameplay integration pending"。约 2961 行业务代码当前无人实例化。
2. **程序集引用未接**：`TingLu.Bootstrap.asmdef` 仅引用 `TingLu.Contracts / TingLu.Config / TingLu.Bootstrap.Core`，未引用任何业务模块程序集，总装代码即使写出也无法编译。
3. **首关场景不存在**：`Assets/TingLu/Scenes/Levels/` 为空目录，无 `FirstStep.unity`、无 Prefab。
4. **`.meta` 缺失 4 个源文件**：`Audio/SpeechPriorityQueue.cs`、`Audio/UnityAudioPlayback.cs`、`Perception/PerceptionService.cs`、`Tests/EditMode/PerceptionMemory/PerceptionMemoryRulesTests.cs`、`Tests/EditMode/Presentation/PresentationRulesTests.cs`（共 5 个）。
5. **测试不会被 Unity 收集**：`Assets/TingLu/Tests/` 下无 asmdef，未引用 `UnityEngine.TestRunner`/`nunit.framework`；`Tests/PlayMode/` 四个子目录全空。
6. **`TingLu.Accessibility` 程序集无任何 `.cs`**，06 的菜单代码实际放在 `TingLu.UI`，与交接文档归属不一致。
7. **`TingLu.Diagnostics`（07）为空**，07 从未派发。

## 5. 里程碑判定

| 关卡 | 判定 | 依据 |
| --- | --- | --- |
| G0 契约/工程基线 | 基本达成 | 10 项契约检查实测通过；契约 0.2.0；18 个 asmdef 建档 |
| G1 模块实现 | 约 75% | 01/02/03/04/05 源码可编译；06 部分；07 空；08 未启动 |
| G2 首关闭环 | 约 10% | 关卡数据齐备，但无装配、无场景，物理上不可运行 |
| G3 三模式/危险/集成验证 | 0% | 无从验证 |
| G4 真人听辨/设备/HRTF | 0% | 环境不具备 |

**综合**：代码资产约 45%–50%，可验收里程碑约 15%–20%。

## 6. 证据等级声明

- 已实际运行：契约 10 项、纯 C# 联合编译、感知/表现纯检查 5 项。
- 未运行：Unity 导入与编译、PlayMode、任何场景渲染、音频设备与四朝向听辨、Windows 语音提供程序、存档真实 IO、HRTF/Wwise、移动真机、系统读屏、真人测试。
- 01/05 交接声称的"16 项 + 8 项断言"由当次会话的临时脚本产生，脚本未落盘，**本次无法复现**；本报告不采信该结论，需以落盘脚本重跑后才计入证据。
