status: PARTIAL
contract_version: 0.2.0

completed:
- `FirstStepLevel` 配置了约 68 米路线 `(0,0)→(0,40)→(25,40)→(28,40)`、起点自语、鸟鸣、电线杆前教学、转角与末段检查点、展示行人、声景碎片、静态斜停自行车、门卫对白和终点。
- 新增检查点快照、单次尝试碰撞/收集统计、星级判断、历史最高星合并、失败呼吸计时恢复，以及本地三槽存档实现（校验和、备份回退、未来版本保护）。
- `FirstStepFlow` 提供位置半径触发、碰撞统计订阅、碎片幂等、结算与存档调用；展示/声音通过必须注入的回调提供。

changed_files:
- `Assets/TingLu/Levels/FirstStepLevel.cs`：首关路线和触发/文案数据。
- `Assets/TingLu/Levels/FirstStepProgression.cs`：检查点、尝试、结算、失败恢复。
- `Assets/TingLu/Levels/FirstStepFlow.cs`：关卡流程、事件订阅及生命周期。
- `Assets/TingLu/Persistence/LocalSaveService.cs`：JSON DTO 显式转换、三槽读写和备份。
- `Assets/TingLu/Scenes/Levels/`：尚无场景内容。

integration:
- 由 00 构造 `CheckpointService(levelId, clock, playerController, memoryCheckpoint, worldLifecycle, getSeed, setSeed)`；开始时 `Activate("start")`，安全位置分别调用 `Activate("turn")` / `Activate("end")`。当前实现的顺序为世界、角色、记忆，00 还需负责恢复事务中的感知重算、声音同步与冻结/开放输入。
- 构造 `FirstStepFlow(new FirstStepLevel(), playerState, eventBus, checkpoints, saveService, attempt, getProgress, setProgress, presentBeat, getSimulationTime)`，调用 `Start()`，每帧在模拟时钟更新后调用 `Tick()`，场景卸载时 `Dispose()`。
- `presentBeat(FirstStepBeat)` 必须接入实际 `IAudioSpeech`/03 音频及合法语义路由；soundId 有 bird-morning、walker-greeting、sparrow-fragment、gatekeeper-greeting、level-complete。不可用空回调作为交付。
- 创建 `FailureRecovery(playerState, checkpoints, getSimulationTime, blackoutCallback, flow.SaveResume, failureSeconds)` 后传给 `flow.AttachFailureRecovery(...)`。它要求总控逐帧 Tick flow，暂停时模拟时钟冻结。
- `LocalSaveService(Application.persistentDataPath)` 提供 `ISaveService`；需分别处理 LoadResume/LoadProgress/LoadSettings 的 Missing、Corrupt、FutureVersion、IoFailure 状态，并用 `AttemptTracker.Resume` 续用原尝试。
- 本轮没有生成 Unity 场景、Prefab 或 Unity 场景构建入口，故目前还不能从场景直接运行。

tests:
- 未运行。本地 C# 编译/用例未执行；Unity 未检出/未运行，不能声称 Unity 测试通过。

limitations:
- Unity 首关内容场景及玩家/检查点触发体未创建；鸟鸣、碎片、门卫语音均依赖外部音频资产和 03/00 接线。
- `LocalSaveService` 使用 `DataContractJsonSerializer`、`File.Replace` 及降级替换分支，尚未在 Unity 目标运行时验证；当前失败时只有保存错误事件，没有接语音回执。
- 检查点恢复的完整事务由 00 总装；流程现在通过 0.2 ResumeData 保存/还原已触发 beat、激活检查点和 finished 标记。00 仍须先用存档恢复检查点快照与 `AttemptTracker.Resume`，再调用 `FirstStepFlow.RestoreProgress(resume)`。
- 失败恢复、失败菜单和首通时长尚无场景/真人验证证据。

本轮修复：
- `FirstStepFlow` 的续玩/存档包含 fired beats、activated checkpoints、finished；奖励把本次 attempt 的 collected IDs 幂等并入长期进度，进度写入错误同样发布 SaveError。
- 首关“电线杆”和“右边斜停共享单车”的未感知泄露对白已移除；敲杖教学改为通用路况提示。动态行人与首关世界数据见 01 handoff。
- `FirstStepWorldFactory` 暴露首关静态/动态/交互对象和地表数据；仍需由 00 注册和装配到公共世界。
- `LocalSaveService` 现在持久化 palette/fontScale/voiceprintIntensity；对旧 DTO 缺失值取默认；仅接受 checksum 有效的未来版本保护标记，拒绝覆盖未来版本主存档或备份。
- 通过 PowerShell Add-Type 实际编译 Contracts/Config/Bootstrap Core/Input/Player/World/Levels/Persistence，并对续玩元数据、新设置、备份回退与未来版本拒写执行 8 项独立断言；Unity 未运行。

contract_requests: none
next_dependencies: 00 完成跨模块恢复事务与场景装配；03/语音模块提供可用播放；创建可运行的 `Scenes/Levels/FirstStep.unity` 内容场景；验证存档 API 在实际 Unity 配置下可编译。

2026-10-13 集成复核增补：`FirstStepFlow` 增加 `Func<string,bool>` 感知门槛构造重载，生产总装可传当前合法观察查询；无门槛的旧10参数重载保留给兼容模块调用。鸟、行人、碎片、门卫及预留自行车触发都按稳定世界对象 ID 校验已感知，碎片尝试/持久奖励 ID 统一为 `l1-1/fragment/01`。Acceptance 脚本已包含纯 C# `DemoSession.cs` 交叉编译；新增感知泄露与失败恢复回归，连续盲道小遮挡覆盖也由相关模块修复后复验通过。最后运行 `pwsh -NoProfile -File Tests/Integration/Acceptance/Run-Acceptance.ps1`：19 PASS、0 FAIL。存档续玩标记、碰撞统计、碎片ID往返与三星边界/历史最高星保持通过。Unity Editor/PlayMode、Audio 硬件、真人体验未运行，不能据此宣称 Unity 验收通过。
