# 音频池满→释放→重试回归交接（2026-10-17）

## 状态

`READY_FOR_CONTROLLER_UNITY`。本轮新增了直接驱动生产 `UnityAudioPlayback` 的 Role06 回归测试，未启动 Unity；Unity EditMode/PlayMode 由总控独占运行。

## 修改文件

- `Assets/TingLu/Tests/Upgrade/Role06/UnityAudioPoolRetryTests.cs`：新增真实后端池满、释放、按模拟时间退避重试、暂停冻结、Reset/代际取消、CriticalThreat 保留和稳定 Started 断言。
- `Assets/TingLu/Tests/Upgrade/Role06/UnityAudioPoolRetryTests.cs.meta`：新增测试元数据。
- `docs/handoffs/AUDIO_POOL_REGRESSION_2026-10-17.md`：本交接。

生产音频代码 `Assets/TingLu/Audio/StreetAudioController.cs` 与 `Assets/TingLu/Audio/UnityAudioPlayback.cs` 本轮未修改。静态审阅确认现有实现已有以下行为：`StreetAudioController` 按车辆危险优先、同优先级按 source ID 排序；池拒绝按 0.5/1/2 秒模拟时间退避；Started 清退退避；Reset/代际变化清理旧状态；`UnityAudioPlayback.AcquireVoice` 只允许更高优先级请求抢占更低优先级声道；池拒绝回执保留请求及竞争声道摘要。

## 回归场景与验收断言

`UnityAudioPoolRetryTests.RealPoolRetainsCriticalThreatAndStartsAmbienceAfterReleaseAndSimulationRetry` 使用 `voiceLimit=1` 的真实 `UnityAudioPlayback`，与 `StreetAudioController` 和 `StreetAcousticModel` 连接，不使用 fake playback：

1. 同一同步同时提供车辆 `CriticalThreat` 与普通 `Ambience`。车辆先收到真实 `Started`；普通请求收到 `voice_limit_no_lower_priority_voice`，且拒绝上下文包含 `priority=Ambience` 和活动车辆 `priority=CriticalThreat`；普通声源不得抢占车辆。
2. 从采样中移除车辆后，控制器通过真实后端 `Stop` 释放实际 `AudioSource` 声道；暂停状态下以同一 `SampledAt` 重复同步 100 次，普通声源不得提前重试。
3. 模拟时间推进到 0.5 秒后重新同步普通声源，真实后端依次回报 `Ready`、`Started`，且 `Started.SimulationTime=0.5`。之后同一采样再次同步不得重复启动。

`UnityAudioPoolRetryTests.RealPoolResetCancelsBackoffAndRejectsOldGenerationBeforeCurrentGenerationStarts` 断言：

- 池拒绝后执行控制器和真实后端 `Reset(8)`；旧 generation=7 的结果被控制器拦截，不再进入后端，不产生旧代 `Started`。
- generation=8 的同一声源可清除旧退避并经真实后端启动，保留 `Ambience` 优先级；旧代不复活。

这些断言覆盖交付缺口要求中的池满→释放→重试→软件 `Started`、低优先级不得抢占、暂停、Reset、旧代取消和 `CriticalThreat` 保留。`Started` 只表示 Unity `AudioSource.Play` 接受软件调用，不证明物理设备出声。

## 根因与本轮决策

既有缺口不是已确认的生产实现错误，而是专项证据只覆盖了 `StreetAudioController` 的 fake playback，未让真实 `UnityAudioPlayback.AcquireVoice` 参与池容量、优先级和释放路径。本轮将回归移动到 Role06 Unity 测试，直接创建真实组件和真实 `AudioSource` 池；因静态代码审阅未发现必须修改生产实现的确定 bug，保留生产代码不变，等待总控 Unity 运行结果决定是否需要修复。

## 已完成的离线核对

- 已核读 `docs/upgrade/RESUME.md`、`docs/upgrade/DECISIONS.md`、`docs/qa/upgrade/DELIVERY_GAPS_RECHECK_2026-10-16.md`、`docs/handoffs/UPGRADE_AUDIO_COMPLETION.md` 与 `docs/handoffs/UPGRADE_AUDIOPOOL_RECHECK.md`。
- 未找到适用的 `AGENTS.md`。
- 已检查测试源码、生产声道抢占/回执/退避/代际清理代码和 `Role06` asmdef；新增测试位于现有 `TingLu.Tests.Upgrade.Role06` 编辑器程序集，引用未改变。
- 以工程中当前 Unity/Bee 为 Role06 生成的 response file、参考程序集和 Roslyn 编译器，把现有 Role06 测试加上本新增文件离线编译到 `%TEMP%`，`OFFLINE_ROLE06_CSC_PASS`（修正 Reset 计数基线后再次通过）。离线编译器提示两个 Unity source generator analyzer 因外部 Roslyn 版本无法加载；C# 编译仍返回成功。此检查没有运行 Unity，也不替代对当前完整工作树的导入/测试。
- `tools/Verify-AudioUi.ps1` 曾尝试以临时报告运行，但在既有 `MenuChecks.Run()` 处失败，异常为 `Exception calling "Run" with "0" argument(s): "main menu initialized"`；该脚本并不编译/运行本新增 Unity Role06 测试，不能作为本回归结果。

## 尚未运行

- Unity EditMode Role06 定向测试（总控独占）。
- 当前工作树同源的全量 EditMode/PlayMode、Build 和可见 Player。
- 物理设备出声、延迟、听辨、方向/距离辨识和真人无障碍验收。

