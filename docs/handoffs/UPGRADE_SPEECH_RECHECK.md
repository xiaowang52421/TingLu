# 街区升级语音回查

更新：2026-10-16（本机 PowerShell 5.1.19041.6456）

## 本机语音检查

在 `D:\听路_街区升级` 的升级副本上，通过隐藏、无配置的 Windows PowerShell 子进程枚举 voices。结果为：

| Voice | Culture | Enabled | 隔离 WAV 合成 |
|---|---|---:|---|
| Microsoft Huihui Desktop | zh-CN | true | 成功，101,424 bytes |
| Microsoft Zira Desktop | en-US | true | 不作为中文候选 |

合成文本为“测试语音合成”。WAV 写入唯一临时目录，检查后已删除临时文件和目录。未安装软件、修改 voice 注册表或调整系统 voice 设置。

指定 Player 日志仍记录 `Process is running in low integrity mode...`，随后依次出现 `System.Speech assembly not found`、`SAPI:Unmanaged activation is not supported` 与 PowerShell 的 `SelectVoice` 安全设置失败。medium integrity 的本机 PowerShell 复测成功，因此当前证据不能证明历史 Player 错误由 voice 被禁用引起，也不能证明其低完整性上下文可用；需由主控重新验证 Player。

## 修改

- `UnityAudioPlayback.cs` 的 managed voice 选择现在只考虑 `Enabled` 的中文 voice，并在一次 `SelectVoice` 失败后继续下一个候选。
- Windows PowerShell fallback 同样筛选 `Enabled` 与 `zh*`，并按顺序尝试每个候选的选择和 WAV 合成。失败候选会关闭输出并清理该次 WAV，然后继续；全部失败时保留失败原因。输入文本和临时路径仍通过 Base64 注入 `-EncodedCommand`，子进程保持隐藏，父进程仍负责超时终止和最终 WAV 清理。
- Role06 新增 `UnitySpeechSelectionTests.cs`：覆盖禁用/非中文 voice 跳过、首个中文候选失败后继续，以及 PowerShell fallback 的过滤/重试脚本结构。

## 验证与交接

- 本机只读枚举和隔离 WAV 合成：通过；唯一启用的中文 voice 为 Microsoft Huihui Desktop。
- Unity/Role06 测试未运行，遵守主控排他验证约定；未启动 Unity。
- 主控下一步应重新构建并用 Windows Player 验证实际语音播放和回执，重点确认低完整性启动下 PowerShell fallback 是否成功。若仍失败，日志应能区分“没有启用的中文 voice”与“所有候选合成均失败”。

## 2026-10-16 随包语音缓存补充交接

状态：`READY_FOR_UNITY_REVIEW`，本次源码与资源已冻结。这里只交接语音适配器和Role06局部行为，不代表街区整体验收通过。

- 任务请求的模型/effort为 `gpt-6-luna/xhigh`；当前执行工具没有可独立核验的实际provider元数据，故不把请求值写成已确认的实际模型。
- `contract_version`：`1.0.0`。
- `completed`：运行时先在主线程按UTF-8 SHA-256读取随包Resources WAV并将PCM复制进 `SpeechWave`；缺失或读取失败时走既有后台动态合成；缓存资源不销毁。缓存日志provider改为通用描述，具体实际 voice 记录在manifest中。
- 固定语料逐条枚举菜单焦点、显示/输出/字幕/震动/颜色/音量/字体/声纹设置标签，以及开始、目标、检查点、恢复、结果和不可用提示，不构造设置组合。生成脚本按启用的中文System.Speech voice稳定排序并逐个尝试；先在工程 `Temp` 暂存全部内容，完成验证后才替换受管资源。已有缓存若没有本任务manifest会拒绝覆盖。
- `changed_files`：`Assets/TingLu/Audio/UnityAudioPlayback.cs`；`Assets/TingLu/Tests/Upgrade/Role06/UnitySpeechSelectionTests.cs`；`tools/upgrade/Build-StreetSpeechCache.ps1`；新目录 `Assets/TingLu/AudioAssets/Resources/`（含确定性目录meta）以及 `Assets/TingLu/AudioAssets/Resources/TingLuSpeech/manifest.json`、manifest meta、88个 `<lowercase-sha256>.wav` 及各自确定性 `.meta`。
- 资源准确路径：`Assets/TingLu/AudioAssets/Resources/TingLuSpeech/`；Unity运行时加载键前缀为 `TingLuSpeech/`。`manifest.json`记录88条文本、UTF-8 key、wav文件名/字节数/SHA-256、实际voice和格式。本次生成者为 `Microsoft Huihui Desktop`，`zh-CN`；共88条WAV，PCM 16-bit mono、22050 Hz，WAV总计12,773,678 bytes。
- `tests`：Windows PowerShell 5.1解析及完整生成：PASS；第二次完整生成后manifest SHA-256和抽样WAV SHA-256保持不变：PASS；独立脚本逐项核验88/88 UTF-8 key、wav内容摘要、PCM格式、meta GUID确定性/唯一性、文件计数及代表性中文菜单短句：PASS；Unity导入、Role06 NUnit及Player：`NOT_RUN`，未启动Unity，需由总控排他验证。
- `integration`：运行时Resources key和资源目录一致。真实资源加载PCM与缓存miss调用fallback两个行为测试已加在Role06，但尚未由Unity执行。
- `limitations`：缓存语音是当前机器已启用中文Windows voice生成的合成语音，不等于人工听辨、设备播放或无障碍体验证据。历史Player低完整性运行失败原因未变；本次缓存是否在Player实际发声及其回执必须由总控重建验证。未知动态文本继续由系统语音后台合成。
- `contract_requests`：无。
- `next_dependencies`：总控在当前源码上排他运行Unity导入与Role06测试、重建Player并检查缓存命中/Started/Failed回执；真人听辨单独记录，不能由缓存文件、构建成功或软件Started替代。

## 2026-10-16 语音池满保留与重试补充交接

状态：`READY_FOR_UNITY_REVIEW`；`Assets` 源码及随包语音缓存已冻结。本角色没有启动 Unity。

- `contract_version`：`1.0.0`。
- `completed`：`FinishSpeech` 遇到 Unity 语音源池满时保留原 `SpeechWork` 和已合成 PCM，以 `Time.realtimeSinceStartupAsDouble` 开始 0.25 秒退避，逐步增至 2 秒上限后继续低频重试；不重新合成，也不让后续句越过等待句。暂停时 Interface 语音仍重试；有当前播放语音时先保持串行。显式取消、同键 supersession、暂停时丢弃世界语音、Reset/Dispose 与恢复代数变化会清理等待项，旧语句不复活。
- `changed_files`：`Assets/TingLu/Audio/UnityAudioPlayback.cs`；`Assets/TingLu/Tests/Upgrade/Role06/UnitySpeechSelectionTests.cs`；`tools/upgrade/Build-StreetSpeechCache.ps1`；缓存目录 `Assets/TingLu/AudioAssets/Resources/TingLuSpeech/` 及受管 `.meta`。固定语料新增“出了点问题，请先回到首页，再试一次。”，缓存现有资源重建为89条。
- `tests`：Windows PowerShell 5.1完整生成两次：PASS；manifest重复生成稳定：PASS，SHA-256=`CAF84A34301453CDE30878E18D195891A1C03C2214DED435141A3F4104B056AE`；独立核验89/89条文本key、WAV SHA-256、字节数、PCM格式（mono/16-bit/22050 Hz）、meta GUID及提示语：PASS；Role06 Unity测试、Unity导入及Player：`NOT_RUN`，未启动Unity。
- `integration`：Role06 增加实际 `UnityAudioPlayback`/`AudioSource` 池满后释放并收到 `Started` 的回归，并将 `Time.timeScale=0` 且暂停时仍保持 Interface 语音重试；另以 `[TestCase]` 检查取消和 Reset 后无 `Started` 复活。测试使用随包真实缓存PCM，不是 fake playback backend。请由总控排他运行；这里不把源代码测试写成已通过。
- `limitations`：实际 Unity 执行仍未验证；缓存合成语音也不替代设备实际发声和真人听辨。新固定提示目前只加入语料，尚未接入 `StreetSession.Report`。
- `contract_requests`：总控在 `StreetSession.Report` 播报该固定错误提示，并在日志中完整保留原始原因；`StreetSession` 不在本角色修改范围。
- `next_dependencies`：总控排他运行 Unity 导入和 Role06 测试；在 `StreetSession.Report` 接线后重新构建 Player，检查池满后释放时 `Started`、取消/Reset 不复活、错误提示及完整原因日志，再单独记录真人听辨结果。

## 2026-10-16 EditMode 清理失败修正

状态：`READY_FOR_UNITY_REVIEW`；本次仅调整后端拥有对象的清理方式，未启动 Unity。

- 读取了完整失败记录 `output/unity-verification/20261016-012916-1bd88367/editmode.xml`。三条语音池重试用例都在 fixture `Dispose` 清理时出现 `Destroy may not be called from edit mode`：两条经过 `UnityAudioPlayback.Dispose` 销毁 voice 对象，池满释放用例还通过 `Reset` → `ReleaseSpeechClip` 销毁语音 clip。堆栈均指向 Unity EditMode 的 `UnityEngine.Object.Destroy`；池满、Started 与取消/Reset 断言没有报告失败。没有用 `LogAssert` 捕获或吞掉这些日志。
- `changed_files`：仅 `Assets/TingLu/Audio/UnityAudioPlayback.cs` 与本交接文档。
- `completed`：`Dispose` 清理 voice GameObject/后端生成的 clips，以及 `ReleaseSpeechClip` 清理 speech clip，现在统一经 `DestroyOwnedObject`；`Application.isPlaying` 时调用延迟 `Destroy`，EditMode 则调用 `DestroyImmediate`。资源均为此后端创建并拥有的对象，缓存共享 clip 仍不销毁。
- `tests`：静态确认三个清理路径都调用该 helper、EditMode 分支使用 `DestroyImmediate`、Role06 没有新增 `LogAssert`：PASS；Unity 重跑：`NOT_RUN`，仍需总控在修复源码冻结后复测。
- `next_dependencies`：总控重跑 EditMode，确认三条语音行为测试及完整套件不再因清理日志失败；真实池满释放与取消/Reset 行为断言保持原样。
