# 音频池拒绝上下文补充（2026-10-16）

总控审核后采用的最小诊断改动：`UnityAudioPlayback` 在 `voice_limit_no_lower_priority_voice` 稳定前缀后追加请求 SoundId/priority，以及最多四个竞争声道的 SourceId（缺失时 EventId）和 priority、activeCount。没有扩大池、改变抢占排序或降低导航/接口反馈优先级；这段信息只解释软件为何拒绝请求，不能证明设备实际听辨结果。

本次源码修改后尚未重新运行 Unity/Player；需在下一次同源 EditMode/PlayMode/Build 和可见 Player 中核对拒绝回执、池满→释放→Started、Reset/暂停清理，并继续把物理出声延迟和真人听辨单独标为 NOT_RUN，直到有设备证据。
