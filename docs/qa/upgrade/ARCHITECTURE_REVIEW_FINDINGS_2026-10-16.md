# 架构意见逐项复核（只读）

日期：2026-10-16。对应第三方意见 `ARCHITECTURE_ADVICE_2026-10-16.md` 与 `ARCHITECTURE_AUDIT_2026-10-16.md`；主控决策另记于 `docs/upgrade/ARCHITECTURE_ADVICE_DECISIONS.md`，本报告不修改该文件，也不替总控代作最终决定。

**状态：READY；Unity 编译、NUnit、PlayMode、Build、当前 Player 画面/发声均 NOT_RUN。** 只读检查升级副本 `D:\听路_街区升级`，未读取原项目数据或存档。两份意见产出时间为 01:00、01:01；决策文件为 01:19。并行源码继续变化：本次看到 renderer 01:16:55、config/session 01:17:48、Role08/Role06/Role09 测试约 01:20、语音资源 01:21:34、播放器 01:22:33、Session 集成测试 01:25:55。时间为该工作区本地文件时间，不构成 source fingerprint。

## 15 项意见核对

编号与建议书第四部分一致。表中“本轮”是技术复核判断；当前源码存在或测试文件新增，不等于测试已执行。

| # | 原意见 | 当前实现、事实判断 | 本轮与最小验证 |
|---|---|---|---|
| 1 | 生成 86 条随包语音 | 建议书的“无 WAV”是生成前快照。当前 `Assets/TingLu/AudioAssets/Resources/TingLuSpeech/manifest.json` 有 89 个条目，文件夹有 89 个 WAV；manifest 含中文错误通用句、资源字节数和摘要。数量口径已从 86/88 变动，应该按 manifest 记 89。只证明产物存在，不能证明当前 Unity Resources 加载或 Player 发声。 | 语料在途/已生成；不再重复生成。冻结后用 Unity 缓存加载测试和新 Player Started/设备听辨核验。 |
| 2 | 错误文案固定化或加入语料 | 当前 `StreetSession.Report`（`Assets/TingLu/Bootstrap/Integration/StreetSession.cs:190-191`）用固定句 `出了点问题，请先回到首页，再试一次。` 调 `Menu.PresentKnownText`，菜单会走语音请求；原异常文本另通过 `Error` 事件交日志路径。此修复不是把任意异常拼串缓存。源码可见 manifest 有该固定句。 | 已有实现，待与语音管线一同运行验证；错误路径应验证固定句能进入播放队列、异常仍可诊断。 |
| 3 | 耳切清理顺序改动并把 split 容差提到 1e-3 | 原耳切算法已被 `StreetRenderer.Triangulate` 扫描线分片取代（`StreetRenderer.cs:271-324`），原建议行号/补丁不适用。新 Role08 测试对三角形面积与边界采样作断言，并涵盖凹口、自接触和从真实片缩减的共线片（`StreetRendererTests.cs:32-84, 165-231`）。 | 不套旧补丁；当前目标合理。必须在冻结源码上运行 Role08，并对新 Player 截图复查。现有回归仍含缩减样例，不能称完整 183 顶点样例已覆盖。 |
| 4 | 统一几何“相等”契约 | 多种 epsilon 分属长度、角度、面积/裁剪判定，量纲不同，单一数值不成立；应分别定义容差单位及误差预算。上游覆盖语义尚未因此改变。 | 部分采纳/系统重构后置合理；如排期需先定义各操作的单位、边界不变量，再跑几何属性回归。 |
| 5 | 四个业务 asmdef 加 `noEngineReferences` | `Player/World/Memory/Perception` 当前四份 asmdef 都设置 `noEngineReferences: true`；此前审计所见旧状态过时。静态读取未见这些纯逻辑目录需要 UnityEngine。 | 已采纳到源码；Unity 编译尚未验证。统一编译四程序集并确认没有遗漏依赖。 |
| 6 | `FinishSpeech` 池满退避重试 | 当前 `FinishSpeech` 在无 voice 时保留同一 `SpeechWork` 并排入退避；使用 `Time.realtimeSinceStartupAsDouble`，恢复代数变化、取消、暂停和处置有清理分支（`UnityAudioPlayback.cs:506-595, 609-615`）。Role06 新增池满释放后重试及取消/reset 用例（`UnitySpeechSelectionTests.cs:157-202`）。 | 已采纳到源码/测试，仍 NOT_RUN。冻结后运行 Role06；另核实持续池满时待重试项的终止/清理边界。 |
| 7 | Tick 异常保护、恢复失败锁定及半状态保护 | `Tick` 捕获运行异常并暂停（`StreetSession.cs:93-112`）；恢复异常进入 `sessionFault`，`Resume`/菜单门禁拒绝继续，`Save` 拒绝覆盖（`:132-148, 157-166, 175-191`）。Integration 新增 `RecoveryBackendFailurePausesProtectsLastGoodSaveAndRetryRestoresPlay`，注入 Reset 失败、拒绝 Resume/Save、健康后 Retry（`StreetSessionIntegrationTests.cs:426-494`；文件时间 01:25:55）。无敌帧改变碰撞语义，当前没有证据要求加入。 | 故障锁部分采纳；不加帧保护合理。新增集成测试仍未运行；需运行完整失败—重试链确认不会暴露半恢复态。 |
| 8 | 不可解析版本按未来处理，Save/Load 对称并记录原因 | 当前存档代码已将非空、不可识别 contract 作为 `FutureVersion` 防止读取/覆盖；Load 与 Save 均检查主档和备份。Role10 覆盖可识别未来主/备版本（`StreetSaveServiceTests.cs:173-205`）及 `2.0.0-beta` 未知版本主备字节保持（`:207-289`）。IO 异常以 `IoFailure` 和 `Error` 事件返回；Session 对普通存档失败报固定语音提示。 | 已采纳必要的数据保护；不重复加已有对称逻辑。新增 Role10 未运行；冻结后运行并回读主档/备份摘要。细粒度 IO 原因可独立排期。 |
| 9 | 同一量只留一处定义 | 新旧 `ContractVersion=0.2.0` 与 `StreetContract=1.0.0` 对应不同契约代际，强合并会模糊存档兼容。`ProbeAngle` 拒绝 360；配置实际消费的 `lowFocusAngle` 另加 `<360` 校验，Role00 测试覆盖 360/361/359（`StreetConfig.cs:15-22`、`ContractTests.cs:9-13`）。其余 ID/材质键重复属于维护债。 | 部分采纳合理；保持旧兼容边界。Role00 新用例需随 Unity 编译运行；其他集中化按独立小改动处理。 |
| 10 | 调整 Perception epsilon 错配 | 现有两侧射线偏移参与可见性边界采样；仅因偏移大于角度合并阈值，不能证明覆盖错误。改变其值会改变探测覆盖及记忆输入；细片是否被 renderer 正确覆盖还要在新算法下复测。 | 暂缓改变覆盖语义合理。准备具体遮挡反例、偏移前后覆盖差集/面积证据后再决定。 |
| 11 | 为 `StreetLevelFactory` 增全图构造校验器 | Factory 是代码固定关卡，DTO/构造器及 Role09 测试已有局部校验，故“零校验”不准确；全图 validator 有价值但不等于本轮运行故障。东/西向路线偏移已按右侧通行调整，Role09 添加位置/停车线断言（`StreetLevel.cs` 与 `StreetLevelFactoryTests.cs`，更新约 01:20:47）。 | 延后通用 validator 可接受；方向修正是源码已改、断言已加，**测试未运行**。不可把“已修复并验证”写成运行验证完成。 |
| 12 | 文档标注活代码/历史代码，不立即删除 | 主控决策文件第“当前生产入口与保留旧代码”节已标出 StreetEntry/StreetSession 新入口、首场景及共享/旧版入口；这比按文件名清理安全。旧编辑器与旧 FirstStep 保留符合升级边界。 | 已采纳；无需批量删除。场景/asmdef 引用关系要在结构变更时重新检查。 |
| 13 | 合并 Generation 与双时间权威 | Session generation、播放器回调代数、音频代数职责并不相同，不能仅按名称合并。World 与 Clock 由同一个固定子步推进；恢复/续玩同步导入交通时间并重置 Clock，Integration 已断言两者及感知采样时点一致（`StreetSession.cs:100-108, 138-154`；集成测试 `:45-68, 192-196`）。 | 后续排期合理；目前没有复现漂移。不改存档契约，保留时钟一致性回归即可。 |
| 14 | 抽取 `RecoveryCoordinator` | 当前故障状态集中仍在 `StreetSession`；新故障锁与失败集成用例先约束事务边界，但抽类本身不保证原子恢复。 | 延后合理；待增加第二关卡/恢复失败类型或状态继续膨胀时再抽取，并以现有失败链测试作迁移基线。 |
| 15 | Renderer 面积自检、属性测试及真实片回归 | 当前 renderer 测试以输出三角形绝对面积对照输入 shoelace 面积，并做顶点/边中点/重心边界抽样（`StreetRendererTests.cs:32-84`）；加入共线缩减例、自接触 asphalt 和凹口例（`:165-231`）。单一有符号 shoelace 仍不足以证明任意弱简单片并集正确，也没有随机裁剪序列测试。 | 部分采纳合理。新测试源码未运行；不声称已覆盖原始完整 183 点数据或随机属性。冻结后先运行 Role08，再补真实输入/面积并集 oracle 与 Player 图像证据。 |

## 其他重点断言

- 语音“空缓存、低完整性 Player 必然静音”的后半句过强：当前有 WAV，但现有 Player 语音失败证据来自旧构建；最新播放器、资源和测试尚无新 Player 证据。菜单通用错误句已固定并入 manifest；动态异常正文仍用于诊断，不应广播任意异常字符串。
- “Save 看备份未来版本、Load 不看”的原断言已过时：当前两条路径均检查主档及备份；最新非空不可解析版本保护用例尚未运行。
- 普通碰撞反馈当前由 Session 非车辆 `Contacted` 播放 `collision.bump` 并更新记忆（`StreetSession.cs:71`）；这是源码变化，不代表设备可听。
- 对 Perception 微片、Memory 跨时间片不合并、Generation/双时钟、池尺寸、UI/灯具/坡道表达等观点，分别属于需覆盖对照、长期量测、维护风险或体验缺证据；静态审查不能写成已复现的数据损坏/泄漏/运行故障。

## 证据与限制

- 当前语音资源：`Assets/TingLu/AudioAssets/Resources/TingLuSpeech/manifest.json` 89 entries；目录当前 89 WAV。Player 证据 `output/street-verification/20261016-003323-speech-context/` 是旧语音上下文验证，不是这些新资源和新播放器的验收。
- 最新可见 Unity EditMode 输出仍为 `output/unity-verification/20261015-232743-a02fe7f5/`（113/113 的旧源码证据）。本次未运行命令来启动 Unity、NUnit、PlayMode 或 Player。
- 相关文档：`docs/qa/upgrade/REQUIREMENTS_RECHECK_2026-10-16.md`、`SAVE_ISOLATION_RECHECK_2026-10-16.md`、`MAP_TRAFFIC_RECHECK_2026-10-16.md`；它们中的 NOT_RUN 限制与本报告一致。MAP_TRAFFIC_RECHECK 明确车道方向新断言未运行。
- 本报告检查了两份架构文档、决策表、动态提示词、升级恢复/实现契约与主要生产源码、相关 Role00/06/08/09/10/Integration 测试和 QA 记录；没有逐行重审每个旧版编辑器/入口文件，也没有核实模型/provider 元数据。测试源码存在不是通过证据；实际音频输出、听辨和人工无障碍体验保持 NOT_RUN。

