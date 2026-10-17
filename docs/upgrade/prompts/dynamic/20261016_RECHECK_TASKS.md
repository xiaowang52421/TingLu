# 2026-10-16 复核任务及所有权补登记

本文件由总控在任务已派发后补写，不是事前原始提示词逐字存档。初次工具派发及后续消息才是原始记录；此处完整整理当前有效任务边界，并再次发给执行者核对。首次未及时将动态提示词落盘属于流程缺口，不通过倒填日期消除。

用户已要求全面核实并继续完成，授权部署 gpt-6-luna / xhigh 子 Agent。四个任务均已实际创建，fork_turns=none；请求配置获工具接受，实际 provider 元数据不可独立证明。共同契约 StreetContract 1.0.0。

## 四任务共同要求

- 阅读本项目升级文档入口、docs/upgrade/01至04、IMPLEMENTATION_CONTRACT、对应原角色完整提示词和真实证据。
- 只写升级副本；原 D:\听路、原存档和原试玩只读。代码不依赖固定D盘路径。
- 不升级Unity/包、不安装语音组件、不改注册表/安全配置、不提升权限。Unity导入、测试、构建仅由总控执行。
- 不自行派生子Agent，不创建其他聊天。共享契约、配置、asmdef、Bootstrap、ProjectSettings由总控持有。
- 只写下列互斥文件；跨目录需求先提精确请求。新资源必须有.meta；生产不得使用测试替身。
- 不弱化未知区域、遮挡、记忆局部更新、暂停与恢复断言。确定性行为测试优先，不写常量/文本镜像测试。
- READY后冻结源码并提交交接：status、model/effort请求及核实限制、contract_version、completed、changed_files、integration、tests及原始证据、limitations、contract_requests、next_dependencies。未实际Unity运行必须NOT_RUN。
- 子任务只报告局部行为；整项A01–A31由独立验收复核，A32–A34另表。Started不等于真人听见，构建不等于画面/输入体验通过。

## geometry_fix

父职责04/08。输入：正式关卡出生探测截图存在径向黑色缺片；真实Player程序集探针说明coverage面积未丢失，Renderer三角化拒绝大量共线顶点片段。

独占：Perception/StreetPerception.cs、Presentation/StreetRenderer.cs；Tests/Upgrade/Role04及Role08；新增交接docs/handoffs/UPGRADE_GEOMETRY_RECHECK.md。当前修复应锁定Renderer，不改记忆或放宽遮挡来填黑缝。

目标：用真实正式地图→Perception→Memory→Renderer路径复现，修三角化退化问题。凸面或凹面均不得越过原许可多边形边界。验收：三角形面积覆盖与输入面一致，无大块丢面；保留凹形/未知处裁剪；总控再看实际Player图像。保存诊断输入、失败根因与新回归用例。

## speech_fix

父职责06。独占：Audio/UnityAudioPlayback.cs；Role06/UnitySpeechSelectionTests.cs及新语音缓存行为测试；AudioAssets/Resources/TingLuSpeech；tools/upgrade/Build-StreetSpeechCache.ps1；交接docs/handoffs/UPGRADE_SPEECH_RECHECK.md。不写StreetAudioController及StreetAcousticModelTests。

输入：Player在当前低完整性进程环境下三种中文动态后端均失败；普通Windows PowerShell可用Enabled zh-CN Huihui合成WAV。保持原有动态合成，修Enabled候选选择及失败后继续尝试。

目标：为当前正式可达中文提示生成随包系统合成语音备用资源。固定菜单、目标、检查点、恢复、结果及有限设置焦点值覆盖；设置逐标签枚举，不构造组合笛卡尔积。使用hash命名WAV和manifest、确定性meta；生成过程暂存，失败不得破坏现有资源。运行时StartNextSpeech主线程读取Resources，复制PCM成SpeechWave，miss才走原Worker动态合成；不在Worker调用Unity API，不Destroy共享资源。恢复代际、停止与回调清理仍须有效。总控实测构建Started及失败日志，真人听辨另记。

## audio_pool_fix

父职责06。独占：Audio/StreetAudioController.cs、Role06/StreetAcousticModelTests.cs；交接docs/handoffs/UPGRADE_AUDIOPOOL_RECHECK.md。

输入：同一PlayMode日志出现453次voice_limit_no_lower_priority_voice；同步Failed回执导致每0.02秒立即重试。

目标：按模拟时间持续指数退避0.5、1、2秒，后续每2秒继续尝试，短暂满池不得永久静音。Started、换音、离开与Reset清理退避；车辆优先并稳定排序；Stop前解绑映射，防同步Stopped回执重入。真实行为用例覆盖两次拒绝后恢复、暂停、重入、离开重返及Reset。完成后冻结，不占Unity。

## audit_runtime

职责12的独立审查分任务。初始只读运行链，已交docs/qa/upgrade/RECHECK_AUDIT_2026-10-15.md；后续授权独占Integration/StreetSessionIntegrationTests.cs的A26新增用例，已冻结；目前只写docs/qa/upgrade/REQUIREMENTS_RECHECK_2026-10-16.md。

目标：从权威R01–R23、D01–D15、A01–A34与角色00–12产物建立需求/实现/证据/未完成矩阵。A26正式Session保存、真实碰撞恢复、临时旧记录及新Session续玩必须一条生产链，不使用伪造真值。历史113测试不包含新用例时明说未运行；不把旧22:10构建作为改后源码证据。

不运行Unity，不改生产或验收结论文件。报告覆盖范围须具体，不声称未读文件已读。下一阶段独立复核由总控另行授予ACCEPTANCE/COMPLIANCE所有权并提供同版新证据。
