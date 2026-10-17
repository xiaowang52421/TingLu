# 集成与运行

公共契约 0.2.0。DemoSession 负责纯 C# 模块装配，DemoEntry 负责 Unity 适配。业务程序集彼此不引用；Bootstrap.Integration 可统一引用业务模块，通过注入连接。

## 场景

用 Unity 2022.3.62f1 候选编辑器打开工程，等待包解析并核对 Active Input Handling 为 Input System Package。直接打开 Assets/TingLu/Scenes/Levels/FirstStep.unity 后 Play。场景仅保存 DemoEntry 与显式声纹材质引用；玩家相机、唯一 AudioListener、声源池、声纹池在 Start 中创建，静态世界不创建可见 Renderer。Build Settings 已以 FirstStep 为首场景。

场景或材质缺失时，菜单 TingLu/Create First Step Demo Scene 会创建新场景和材质；已有场景原样保留。原 G0 Create Bootstrap Scene 只显示契约状态。2026-10-14 已在 Unity 2022.3.62f1c1 实际导入、编译并验证 FirstStep 启动。

DemoEntry Inspector 可启用 Show Touch Controls 作为 PC 触屏适配入口；实际触屏自动显示。键盘、手柄与触屏都输出同一 InputFrame。标准模式显示基础数值/字幕；无 UI 模式保留声纹；纯音频关闭声纹。菜单、目标、失败与结算用 IAudioSpeech 播报已知信息。

## 生命周期与恢复

正式 FirstStep 场景通过 TextAsset GUID 绑定 `Levels/Data/FirstStep.json`，DemoEntry 编译其内容；缺少配置会明确报启动失败。出生、世界对象、盲道、对白、检查点及人物路线来自该资产。FirstStepTemplate/旧首关构造用于模板生成与兼容回归，正式入口不再回退到代码首关。GUID 是资产身份，工程盘符和安装目录可以变化。

人物路线按 SimulationClock 插值；暂停冻结，循环包含完整回程，非循环到末点停止。新一局重置时钟与旧痛觉反馈；续玩恢复检查点时间再采样路线，避免角色恢复首帧使用错误相位。失败恢复保留当前全局时间，再重采样人物位置。

Tick 先处理菜单，菜单期间模拟时钟不前进。正式玩法使用 PlayerController 与 WorldRegistry 的同一移动解析，脚步按实际位移产生，静态接触进入感知与合法记忆。声源采样来自共享 AcousticModel，Unity 只更新已采样位置/增益/遮挡。首关位置触发同时检查合法感知；正式 UI 不查询世界真值。

恢复：冻结时钟/输入 → reset 音频 generation → 恢复世界事件、角色、合法静态记忆 → 清临时感知/接触 → 同步监听器 → 重算感知/重建声源 → 保存 → 恢复输入。失败保持冻结、黑屏和暂停菜单；异常续玩退回主菜单，仍可从头开始。AttemptStats 独立于检查点，因此累计碰撞与碎片不回滚。退出/重新开始释放事件订阅及业务实例；Dispose 释放音频资源。

UnityAudioPlayback 每个声源有独立 Transform，世界声音在暂停期间延后真正 Play/Started；界面语音保持可操作。Windows 中文语音先尝试 System.Speech，再尝试 SAPI COM；Unity Mono 不支持时通过系统自带 Windows PowerShell 隐藏子进程渲染 WAV，不安装 SDK。文本使用编码参数，临时文件带随机 ID，超时终止并清理；无中文声音明确返回 Failed。实际 Windows Player 已确认合成并开始播放，仍不能把软件 Started 回执等同设备端真正可听。

## 检查与缺口

README 列出纯 C# 和真实 Unity 检查；报告在 docs/qa。纯 C# Demo 检查使用真实本地存档 IO 和录制音频请求的替身。Unity 编译、场景启动、EditMode/PlayMode、Windows 构建与中文语音提交播放已验证；定位/材质听辨、延迟、HRTF、系统读屏、震动、移动真机和真人纯音频通关仍需实际验证。

音效为标明工程用途的合成占位音。耳机/扬声器选项当前只保存偏好，没有设备路由策略；本轮不将其计作设备兼容通过。

## Unity 导入、测试与构建入口（2026-10-14）

`tools/Verify-Unity.ps1` 可运行 Import、EditMode、PlayMode、BuildWindows 四个独立步骤，支持显式 `-UnityPath` 和超时。自动查找工程指定版本的安装位置；没有编辑器不会安装软件或把纯 C# 检查算成 Unity 通过。运行前关闭占用该项目的 Unity 编辑器。

每次验证将日志、测试 XML 或 Windows 开发构建与结果 JSON 保存到独立 `output/unity-verification` 子目录。EditMode 要求实际执行至少一项测试且全部通过；构建要求 BuildPipeline 成功并生成 EXE。脚本失败或缺少环境均返回非零状态。Windows 构建需要编辑器配套的 Windows Build Support。

`SessionLifecycleTests` 在 Unity EditMode 中使用生产 DemoSession 与真实临时存档，覆盖暂停/续玩、失败恢复保持碰撞记录、资源释放，已通过 Unity/NUnit 运行；音频仍用记录后端，不提供设备输出证明。
