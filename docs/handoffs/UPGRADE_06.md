# UPGRADE_06 交接

status: READY_FOR_UNITY

role/model/effort: role06；派发目标 gpt-6-luna/xhigh；当前执行环境未提供可核实的实际 model、provider 或 effort 元数据。

contract_version: StreetContract 1.0.0

## completed / changed_files

- 新增 `StreetAcousticModel : IAcousticQuery`，每次采样仅捕获一次 `StreetWorldFrame`，要求快照时间和采样时间一致。仅从快照中活动且属于 `StreetCatalog.Sounds` 的声源产生结果；距离、面向、低专注、混淆和声线与声源之间的 `AcousticLoss` 一并计算进同一 `AcousticQueryResult`。
- 被动听觉包含前后左右，不使用盲杖探测扇形裁剪。结果仅返回合法短时声源定位和许可类别，不复制对象多边形或地面材质；信号开关直接消费快照 `SoundActive`，不重复计算信号相位。
- 混淆期间的听觉可辨度现使用共享 `StreetConfig.Gameplay.confusionMultiplier`，与角色混淆移动减速同源；低专注衰减仍由既有 `lowFocusThreshold` 与本模型曲线处理。
- 新增 `StreetAudioController`，由同一声学结果启动/更新/停止活动循环声源。车辆危害声源使用 `CriticalThreat`，普通活动声源使用 `Ambience`；切代和显式 Reset 会停止旧代声音并清除最近采样映射。后端实现正式已有的 `IStreetAudioUpdater`，对移动声源更新位置、增益和遮挡。
- Unity 合成占位目录覆盖街区声音及 `StreetCatalog.Materials` 所有脚步 ID。清单记录 ID 与占位音参数；没有录音素材、HRTF 或设备端听感验证。占位噪声使用稳定 ID 哈希种子。
- 新增 Role06 NUnit 源码，涵盖四向朝向、后方声源不受探测扇形裁剪、距离/遮挡/低专注/混淆、混淆到期、采样映射作用域、信号快照开关、声源同步/切代、危险优先级、移动循环更新及目录映射。

changed_files:
- `Assets/TingLu/Audio/StreetAcousticModel.cs`
- `Assets/TingLu/Audio/StreetAcousticModel.cs.meta`
- `Assets/TingLu/Audio/StreetAudioController.cs`
- `Assets/TingLu/Audio/StreetAudioController.cs.meta`
- `Assets/TingLu/Audio/UnityAudioPlayback.cs`
- `Assets/TingLu/AudioAssets/StreetAudioPlaceholderManifest.txt`
- `Assets/TingLu/AudioAssets/StreetAudioPlaceholderManifest.txt.meta`
- `Assets/TingLu/Tests/Upgrade/Role06/StreetAcousticModelTests.cs`
- `Assets/TingLu/Tests/Upgrade/Role06/StreetAcousticModelTests.cs.meta`
- `docs/handoffs/UPGRADE_06.md`

## integration

- `StreetSession` 草稿已按 `StreetAcousticModel(()=>World.Capture(), Player, Config)` 创建模型，并通过 `audio.Sync(Sources, Generation)` 消费同一批 `ProjectAcoustics` 输入。`UnityAudioPlayback` 已声明实现正式的 `IStreetAudioUpdater`。本角色没有修改 `Bootstrap`、公共契约、配置、程序集定义或 `AudioListener` 接线。
- 主控集成时需核实 `StreetSession` 每次重采样前都使用同一模拟时刻推进世界；在暂停/恢复中先 Reset generation、同步世界和角色，再生成并同步新快照，避免重放旧代声音。不要在 Bootstrap 重复逐对象调用 `UpdateSource`，控制器会自行更新活动源。
- `SoundFor` 和 `IsVehicleHazard` 仅针对最近一次 `Sample` 的结果有效；调用 `Reset` 会清空映射。消费者不得缓存并用旧结果重新 Sync。

## tests / verification

- Role06 NUnit 用例已编写，但本角色未启动 Unity、未运行 C# 编译或测试：`NOT_RUN`。源码级检查发现并移除了测试源码中重复的测试方法声明；尚需 Unity 编译确认。
- 00 请在排他的 Unity 窗口先过滤 `TingLu.Tests.Upgrade.Role06.StreetAcousticModelTests`，随后运行 `TingLu.Tests.Upgrade.Role06`。保留日志/XML及结果；失败时先修复，再更新本交接状态，不能把源码检查记作 PASS。
- 人工耳机方向辨识、真实材质听辨、声源移动听感、系统输出设备可听性、延迟、HRTF 及真人纯音频路线均为 `NOT_RUN`。Unity 软件 `Started` 回执不代表硬件真正发声。

## limitations / contract_requests / next_dependencies

- 无新增公共类型、配置字段、包、SDK 或契约请求；继续使用集中 `confusionMultiplier` 和 `IStreetAudioUpdater`。
- 全部可替换声源目前是合成占位音，参数区分仅用于工程测试，不代表真实声源辨识度。人工听感是独立验收项。
- 下一步由 00 独占执行 Unity 导入/编译及 Role06 定向测试；随后与 04 声学投影、暂停/恢复代际及实际 Player 音频接线联调。本角色已停止写入 `Audio`、`AudioAssets` 和 Role06 测试目录。
