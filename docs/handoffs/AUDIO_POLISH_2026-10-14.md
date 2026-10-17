# M0 音频收尾交接（2026-10-14）

status: READY_FOR_REVIEW
contract_version: 0.2.0

本轮集中修了首关语音排队、暂停恢复和 Windows Player 中文语音启动。Windows Player 先前因 Unity Mono 找不到 `System.Speech`、进程内 SAPI COM 无法激活而导致菜单语音失败；现在在原有提供程序之后增加隐藏的 Windows PowerShell 后备路径。中文文本和临时 WAV 路径均以 UTF-8 Base64 放入固定脚本，再将脚本整体以 UTF-16LE Base64 传给 `-EncodedCommand`，不经 shell 拼接执行；等待上限 20 秒，超时终止进程，临时 WAV 在所有退出路径清理。旧恢复代际仍在主线程入队前过滤。设置 `TINGLU_AUDIO_DIAGNOSTICS=1` 时，真实开始播放后日志只记录后端名称和 WAV 帧数，不记录播报文本。

语音请求现在等当前一段结束或被明确停止后再开始下一段，合成也保持有界并发；语音播放、排队或合成期间压低氛围声。进入暂停会停止当前及正在合成的导航语音，清掉排队和已合成的导航语音，只留下界面语音，让暂停菜单正常播报，也避免旧导航句子在恢复后突然插入。暂停后才完成的导航语音合成会回报停止，不会启动。声学配置现在拒绝 NaN 与无穷距离/增益，避免无效值进入共享听觉模型。

改动文件：

- `Assets/TingLu/Audio/UnityAudioPlayback.cs`：PowerShell System.Speech 后备、诊断日志、语音串行播放、暂停清理和氛围压低。
- `Assets/TingLu/Audio/SpeechPriorityQueue.cs`：增加按条件移除排队语音的模块内操作。
- `Assets/TingLu/Audio/AcousticModel.cs`：声学配置有限值校验。
- `Tests/Accessibility/AudioChecks.cs`：非有限声学配置拒绝及暂停保留菜单语音检查。
- `tools/Verify-AudioUi.ps1`：新增队列/暂停/语音后端源码审查；探针编译导入 `System.Diagnostics`。
- `docs/handoffs/AUDIO_POLISH_2026-10-14.md`：本交接。

资源清单（均为可替换合成占位音，没有第三方或录制素材）：`footstep.asphalt`、`footstep.tactile`、`cane.tap`、`echo.ping`、`ambience.bird`、`ambience.pedestrian`、`ambience.guard`、`collision.bump`、`feedback.breath`、`feedback.heartbeat`、`feedback.checkpoint`、`feedback.menu`。来源为 `UnityAudioPlayback.PlaceholderBank` 的临时合成波形；没有外部许可负担，也不代表已达到材质可辨或正式音质。`AcousticModel` 与 Unity 后端继续共享声源位置、可辨度及最终增益，Unity 使用平坦 rolloff 曲线，避免再次按距离衰减。声源子物体、监听器更新和恢复代际处理沿用现有装配。

验证：

- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/Verify-AudioUi.ps1 -ReportPath $env:TEMP\tinglu-audio-ui-results.json`：7 项通过，包括菜单状态、声学模型、语音优先级/替换、后端生命周期源码审查和中文 WAV 合成探针。报告为临时路径，没有覆盖仓库内 QA 结果。
- 中文语音探针生成有效 PCM WAV：22,050 Hz、单声道、70,306 帧。它验证当前 Windows 语音提供程序和 WAV 解析，不单独证明 Unity 已从扬声器输出。
- 总控确认后续真实 Windows Player 菜单语音已开始播放；诊断开关输出 `backend` 与 `frames`，可用于核对使用的后端。更早的失败证据保留在 `output/unity-verification/20261014-015958-7ab8254e/player-smoke.log`，发生于本次后备加入之前。
- Unity PlayMode 3/3 与 EditMode 10/10 的记录分别在 `output/unity-verification/20261014-024933-51b0dda6/results.json`、`output/unity-verification/20261014-025301-98463868/results.json`。PlayMode 仅覆盖场景菜单启动，不等于真实音频听测。
- 耳机/外放听辨、四朝向人工声像、真实设备端到端延迟、纯音频通关、HRTF/Wwise 和移动设备仍未验证。Windows PowerShell/SAPI 取决于系统已安装中文语音；缺少语音时会真实回报失败，不会播放合成短音代替句子。

集成说明：原有 `UnityAudioPlayback` 装配不变，菜单播报沿用 `IAudioSpeech`。真实 Player 复测时可将 `TINGLU_AUDIO_DIAGNOSTICS=1` 注入进程并查找 `TingLu speech started; backend=...; frames=...`。不需要公共契约变更；关卡动态声源的位置仍应使用既有 `UpdateSource(sourceId, position, gain, occlusion)` 更新。

contract_requests: none
next_dependencies: 进行耳机/外放及纯音频真人听测，测量设备延迟并完成 HRTF/Wwise 和移动端评估。
