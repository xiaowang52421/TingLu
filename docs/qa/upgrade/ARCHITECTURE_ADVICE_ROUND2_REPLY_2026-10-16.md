# 第二轮架构建议独立回复

日期：2026-10-16  
性质：**独立审阅建议；最终采纳由 00 审核。**这是对 `ARCHITECTURE_ADVICE_ROUND2_2026-10-16.md` 的逐项复核，不是执行指令，不改写原建议或主控决策表。

## 审阅边界与证据口径

本稿对照当前源码、工具、验收/交接文档和已落盘验证产物；没有启动 Unity、运行测试或修改 `Assets`。表中“静态”表示源码/文档审阅，“实测”仅表示所列既有 XML、JSON、哈希或图片复核结果；它们不自动证明其他版本、设备或人工体验。

当前可独立核对的 C272 版本指纹为 `C2726FBC4722040FFCBE82C996EE148368649348C1CF27AA3399B6FFF346C717`：EditMode 169/169、PlayMode 2/2、BuildWindows PASS，三个 `results.json` 均为 `sourceUnchangedDuringStage=true`，验证器 SHA-256 同为 `CFBA67C5106A77C03A1E6F70EFDCB936DF08AB60F92A8F9C36A7014763C07839`。EditMode XML 还记录 13 次探针：Perception 中位/P95/max 为 7.8846/11.6414/11.6414 ms，Memory.Apply 为 6.0387/10.4746/10.4746 ms；这是一条 Unity Editor 路线样本，不是跨机器延迟保证。00另报告 C272 两档可见 Player 及包/搬迁启动检查通过；本稿不把它扩展成耳机实际出声或真人无障碍验收。

## 对“已解决 A1–A9”的校正

| 声明 | 独立复核 | 证据边界 |
|---|---|---|
| A1 探针性能阻塞已解决 | 当前 C272 Unity 路线样本显示探针与 Apply 在该样本内已明显低于旧离线热点；但 `UPGRADE_GEOMETRY_RECHECK.md` 的旧离线 `Probe+Apply` 数字与 Unity 计时口径不同，不能据此宣称全场景/设备性能已闭环。 | Unity 13 样本见 C272 `probe-performance-*.json`；离线数字见几何复核交接。实测，口径不同。 |
| A2 错误预合并优化已撤回 | 当前 Memory 交接说明已撤回把预合并面直接作差集 mask 的方案；原始观察面仍用于差集。 | `docs/handoffs/UPGRADE_MEMORY_MERGE_REVIEW.md`；静态交接复核与所列离线诊断。 |
| A3 静止车辆接触边界已修 | 有根因前后诊断；C272 PlayMode 的 A25/A30 通过为有限真实路线证据，不等价于所有车辆接触几何。 | 车辆诊断与 C272 PlayMode XML。实测限于已覆盖路径。 |
| A4 Area 浮点修复已落地 | 当前实现先平移坐标再以 `double` 累加，保留 `float` 返回和原守卫；Role00 现有正反向/复制回归。 | `StreetGeometry`、`ContractTests.cs`；C272 EditMode 169/169。 |
| A5 Player 语音假 PASS 已堵 | Player verifier 要求语音到 `STARTED` 才能 PASS；并记录 verifier / EXE 哈希。 | `Verify-StreetPlayer.ps1:21-22,74-77`；C272 Player 结果由 00 报告。软件 Started 不等于设备可听。 |
| A6 语音语料缺失已处理 | manifest 记录 89 项；它证明资源索引存在，不证明运行时每个可能文案键均被穷举检查。 | `TingLuSpeech/manifest.json`；资源存在是静态事实，播放证据另见 Player 日志。 |
| A7 编译边界已设置 | 四个 Player/World/Memory/Perception asmdef 均需保持 `noEngineReferences`；这是编译配置约束，不替代当前 Unity 编译。 | 四份 asmdef；C272 BuildWindows PASS。 |
| A8 Renderer Surface 缓存已实现 | 缓存失效与计数实现仍在；当前 C272 计时 fixture 证明稳定帧重建计数为 1，不是全场景/Player 帧率验收。 | `StreetRenderer.cs`、Role08 测试与 C272 XML；静态实现 + 有限实测。 |
| A9 同指纹阶段机制已落地 | 当前 verifier 对清单使用 `-Force`、ordinal 排序、写 `verifierSha256`；C272 三阶段指纹和验证器哈希一致，阶段内未变。 | `Verify-Unity.ps1:15,20,26` 与三份 C272 `results.json`。 |

## A–G 逐项意见

优先级：P1 为需求/发布证据或持久化风险，P2 为当前有价值的验证或小范围正确性缺口，P3 为维护或条件性待办。代码/证据位置均用仓库相对路径。

| 编号 | 优先级 | 独立意见与建议价值 | 代码位置与证据类型 |
|---|---|---|---|
| A-A1 | P2 已完成 | 原稿说现有测试只有原点附近小矩形不准确：Role00 已有平移薄片正反向和复制回归；当前参数化用例又补了 `(1000,-1000,0.001)`。C272 EditMode 169/169 通过，因此这一项有当前源码的 Unity 测试证据。 | `Assets/TingLu/Tests/Upgrade/Role00/ContractTests.cs:9-21`；实测 C272 EditMode。 |
| A-A2 | P3 保留 | 长度、角度、面积等容差仍需单位与误差预算说明；不要合并为一个 epsilon。低风险文档化，适合下次改数值前完成。 | `Assets/TingLu/Contracts/StreetContracts.cs`、`StreetPerception.cs:16-17`、`StreetMemory.cs:9`；静态。 |
| A-A3 | P3 暂缓 | 暂缓改变覆盖语义合理；补待办可要求一个具体遮挡反例及修改前后的 coverage/observed 差集。当前没有该类对照证据，不应把两个角度常数的大小关系直接判成缺陷。 | `StreetPerception.cs:16-17,166-197`；静态，反例/对照待证。 |
| B-B1 | P2 部分解决 | “Apply 先破坏内部状态”的旧描述已过时：当前实现先构造 `nextSurfaces`、`nextObjects`、`nextCurrent`，之后才提交。剩余价值是注入失败测试；目前没有测试证明中途异常时所有私有结构均保持旧快照。 | `Assets/TingLu/Memory/StreetMemory.cs:18-118`、Role05 tests；静态实现，C272 测试通过不等于故障注入覆盖。 |
| B-B2 | P3 观察 | 13 样本中西侧静止重探仍从 206→229，随后每次 +5；面积约从 164.286204 到 164.286212，增量很小但不是严格零。记入长期观察即可，先定 20 次序列的表面数/面积预算，再决定是否优化。 | C272 `probe-performance-*.json` 的 `stationary-repeat`；实测小样本。 |
| B-B3 | P3 已澄清 | 先播放 `cane.tap` 是“动作已触发”的反馈语义，失败仍故障暂停并阻止保存；性能复核已明确这层语义。无需为了减少一帧延迟改顺序，除非产品要求提示“探测已提交”。 | `StreetSession.cs:70`、`PROBE_PERFORMANCE_2026-10-16.md`；静态 + 既有交接说明。 |
| C-C1 | P2 部分解决 | 插桩已运行并读出数字。C272 fixture 首次完整 Render 0.792 ms，30 稳定帧 p50/P95/max 为 0.021/0.161/1.581 ms，重建计数 1。它没有禁用缓存对照，也未覆盖到期、恢复、AudioOnly 或全场景，因此只证明该 fixture 路径，不证明缓存净收益。 | `Role08/StreetRendererTests.cs:364`、C272 EditMode XML；有限实测。 |
| C-C2 | P2 保留但慎设门槛 | 旧建议“尚无断言”仍成立；当前 C272 有 13 样本 Unity 路线基线，Perception 与 Apply 的最大值分别为 11.6414 和 10.4746 ms，但单机小样本不足以把 `<50 ms` 直接当所有环境的稳定硬门槛。可先记录基线并定义代表路线/机器口径，再选宽松门槛。 | C272 `probe-performance-*.json`、Role04/集成测试；有实测，无延迟断言。 |
| D-D1 | P3 条件性 | 旧版与新版共存的可发现性问题真实；但按修改时间圈定 39 文件容易误标共享基础设施。优先在决策文档列出入口/共享类；仅对确认旧版专属文件加注释。 | `ARCHITECTURE_ADVICE_DECISIONS.md` 的入口关系；静态。 |
| D-D2 | P3 条件性 | `StreetEntry` 仍用 `LocalSaveService` 存 Options，旧 `PerceptionService` 仍用 `Geometry2`；若未来拆除旧运行时，这是前置依赖。当前旧资产按决策保留，故不是本轮阻断，不建议先迁移。 | `StreetEntry.cs:47`、`PerceptionService.cs` 多处、`Geometry2.cs`；静态检索。 |
| D-D3 | P2 精确化 | 关卡工厂没有统一出口 validator，但 Role09 已覆盖部分固定地图/路径约束；“完全无校验”过宽。若做，应只补明确契约项（禁放物、声源-实心区域、出生/检查点/终点占位），不要把所有声源都限制在可走面。 | `StreetLevel.cs`、`Tests/Upgrade/Role09/StreetLevelFactoryTests.cs`、`MAP_TRAFFIC_RECHECK_2026-10-16.md`；静态 + 新断言已有 C272 EditMode。 |
| D-D4 | P2 需意图决策 | 鸟源与实心建筑的几何重叠可静态确认，是否为屋顶/室内声源无法由坐标判定。先由内容/产品负责人确认意图；不应自动移动声源或称为已复现运行故障。 | `StreetLevel.cs:64,124`、`MAP_TRAFFIC_RECHECK_2026-10-16.md`；静态。 |
| D-D5 | P1 需求缺口 | 红绿灯可见呈现和坡道/路沿高度仍未实现。R15/D12 涉及共享契约和展示语义，必须由 00 对照原需求定界；本审阅不能单方把它们列为范围外，也不能由相位声源/材质片代替完成。 | `StreetLevel.cs`、`StreetWorld.cs`、`StreetSurface` 契约、`MAP_TRAFFIC_RECHECK_2026-10-16.md`；静态。 |
| D-D6 | P3 维护 | `StreetWorld` 仍比较两个信号 ID 字面量，集中到 catalog 常量可减少重复真相，但不影响当前路线行为。 | `StreetWorld.cs:482-491`、`StreetCatalog`；静态。 |
| D-D7 | P2 文案 | 暂停菜单“从路口重来”执行 `Retry()`，活动中实际回最近检查点；结算页“再走一次”则会重新开始。应只澄清暂停页文案，不把此项扩大成重置永久记忆的新功能。 | `AccessibleMenu.cs:250-259`、`StreetSession.cs:168-172`；静态。 |
| E-E1 | P3 已解决 | 已改用 `StringComparer.Ordinal` 固定路径顺序；对 Unity 2026 当次结果影响不大，但跨区域稳定性收益成立。 | `tools/Verify-Unity.ps1:25-27`；源码核对及 C272 三阶段相同清单。 |
| E-E2 | P3 已解决 | 清单遍历现在有 `-Force`。 | `tools/Verify-Unity.ps1:19-22`；源码核对。 |
| E-E3 | P3 已解决 | Unity 结果现在记录 verifier 自身 SHA-256；C272 三阶段都记录同一个值。 | `Verify-Unity.ps1:15`、C272 `results.json`；实测字段。 |
| E-E4 | P3 已解决 | 总状态现随原基线保护位失败而写 `FAIL`；原稿关于无条件 `SAVED_AND_VERIFIED` 已过时。 | `tools/Save-UpgradeHandoff.py:119-129`；静态核对。 |
| E-E5 | P2 已解决 | Player EXE 路径解析与哈希已在 `try` 内，统一 `finally` 写 `results.json`；覆盖原稿所指的坏路径/不可读失败。 | `Verify-StreetPlayer.ps1:14-22,79-83`；静态核对。 |
| E-E6 | P2 有实证 | C272 PlayMode 结果 `032356` 记录了 `first-step-menu.png`，该文件在该运行目录不存在，原建议成立。00已决定报告只列实际截图并核哈希；修订版 verifier 的复跑结果应单独留证，不回写历史结果。 | `Verify-Unity.ps1:88-90`（对应 C272 运行时版本）、`output/unity-verification/20261016-032356-1e44a6a3/results.json`；实测路径缺失。 |
| E-E7 | P3 潜在，不建议排除整类 | `Assets` 全量纳入指纹是防漏改的有用约束；历史 Unity 生成的 `InitTestScene*` 是潜在源变更，但当前 C272 前后指纹未变，当前 Assets 根也无此文件。若复现，应识别/留痕具体 Unity 产物，不应把整个 Assets 或无条件该模式排除。 | `Verify-Unity.ps1:19-27`、C272 manifest/results；静态 + 当前阶段未触发。 |
| E-E8 | P3 潜在，不建议排除整类 | 当前 C272 阶段将 `ProjectSettings` 纳入清单且 `sourceUnchangedDuringStage=true`。历史 TimeManager 重写说明需监控，但“移出指纹”会削弱保护；若再复现，先查明 Unity 预热/写回时点并保留明确差异证据。 | `Verify-Unity.ps1:19-27`、C272 results、旧复核记录；静态 + 当前阶段未触发。 |
| F-F1 | P1 证据更新 | `ACCEPTANCE.md` 中 A08/A12 仍是旧版品红/无地面截图 FAIL。后续 CAD Player 已显示有地面、无品红，但格纹错向；00报告当前 C272 修复图像目检通过。应保留原 FAIL 历史并附后来同版证据/范围，不能只把 FAIL 擦成 PASS。 | `ACCEPTANCE.md`、`PLAYER_IMAGE_REVIEW_2026-10-16.md`、C272 Player；历史实测 + 00最新目检报告。 |
| F-F2 | P3 持续 | 重要阶段已有按组列指纹的文档；老验收表仍有无指纹行。按阶段汇总清单和指纹可满足追溯，不必机械把一条相同指纹复制到每个子项。 | `STAGE_BASELINE_2026-10-16.md`、`PROBE_PERFORMANCE_2026-10-16.md`、`ACCEPTANCE.md`；文档核对。 |
| F-F3 | P3 持续 | 对旧假 PASS/无指纹结果应标历史且保留原始文件。当前基线和工具复核已提醒部分历史证据限制；建议仅补仍可能被误读的索引，不重写旧结果。 | `STAGE_BASELINE_2026-10-16.md`、`VERIFICATION_TOOLS_RECHECK_2026-10-16.md`；文档核对。 |
| F-F4 | P1 更新 | `UPGRADE_STATUS.md` 头部仍以 CAD 161/161、texture 待验证作当前描述；当前已有 C272 169/169、PlayMode、Build 和两档 Player 结果，故该首页需要以新指纹更新，并保留 CAD 为历史阶段。 | `docs/upgrade/UPGRADE_STATUS.md`、C272 三阶段结果及 Player 证据；文档与既有实测。 |
| F-F5 | P3 采纳纪律 | 同 SHA 截图不能算独立画面证据。自动列唯一截图数有价值，但须保留每个文件路径/哈希映射；不得因去重丢掉标签差异和历史原件。00已采纳该工具改进，等待新 verifier PlayMode 复跑。 | `PLAYER_IMAGE_REVIEW_2026-10-16.md` 中重复 PNG 哈希；00的工具复跑记录。既有实测 + 待复跑。 |
| G-G1 | P2 基本接入，覆盖待复核 | 静态碰撞经 `Contacted` 播 `collision.bump`；车辆失败走恢复语音，普通接触清零会先触发 Contacted。静态链路看不出缺少即时语义反馈，但仍不证明每类状态损伤都正确播放或设备可听；`feedback.breath` 当前新版 Session 未见调用不单独构成故障。 | `StreetSession.cs:70-74,130-145`、`StreetPlayerController.cs:246-283`；静态 + C272 软件运行证据边界。 |
| G-G2 | P2 有价值，表述需修正 | 89 项 manifest 真实存在，运行时按文案 SHA key 查资源；当前代码未见把所有代码文案键与 manifest 集合做启动期完整性比对。建议未来检测缺失项并给出明确报告；“缺缓存必然静默无声”尚不能由静态审查确认，因为后端另有 fallback/失败回执路径。 | `UnityAudioPlayback.cs:747-785`、`manifest.json`；静态。 |
| G-G3 | P3 部分采纳 | 原“Navigation 从未使用”已过时：敲杖请求以 `FeedbackPriority.Navigation` 发送；`VoiceprintIntensity` 明确影响视觉声纹。音频后端也有同名音量乘数，但新入口是否把设置传入后端仍应按产品语义决定；不应据此判设置整体无效。 | `StreetSession.cs:70`、`UnityAudioPlayback.cs:323-325,680-681`、`StreetRenderPlan.cs:96-99`；静态。 |

## 独立建议顺序

1. 先将 C272 指纹和两档 Player 证据追加到状态/验收文档，保留 CAD 与旧 FAIL 历史；确保新的截图去重 verifier 也有独立 PlayMode 结果。
2. 对 R15/D12 与鸟源重叠作需求/设计意图确认；在决策前不把需求改写为范围外，也不移动声源。
3. 低风险工具项 E1–E5 已落地；保留完整 `Assets`/`ProjectSettings` 指纹。E6/F5 按 00 已授权的方式在新 verifier 结果中补证。
4. 几何/记忆方面，A-A1 与 B-B1 的主要源码改动已有当前测试；可选补中途异常注入，以及按明确次数/规模观察重复探测碎片。
5. 其余 epsilon 误差表、旧版文件标注、条件性拆除依赖、信号常量、菜单文案、语音资源自检按维护窗口处理。

以上是独立复核意见，不代替 `ARCHITECTURE_ADVICE_DECISIONS.md` 或 00 的最终决策。

## 00 主控审核与最终回复

00已完整阅读原建议书及上述31项独立意见，并对照当前源码、真实结果和图片审核。以下是最终采纳决定；前文是独立审阅时间点快照，涉及“待验证/待更新”的表述以本节及当前状态文档为准。子Agent未因附件建议自行获得修改权。

结论：**建议书有价值，尤其是验证报告可信度、重复探测长期增长和文档滞后；其中多项故障描述已过时，不能原样照做。**

|条目|主控决定|落实或理由|
|---|---|---|
|A-A1|采纳，已完成|保留真实失效薄片回归，并补x≈1000参数化用例；当前169/169包括该项。|
|A-A2/A-A3|部分采纳，后续取证|保留容差单位说明与遮挡反例/覆盖差集要求；本轮不统一epsilon或改变探测边界。|
|B-B1|主要问题已解决；故障注入延后|Apply已先算nextSurfaces/nextObjects/nextCurrent再提交，不重复重构；定点异常注入仍可补，不能称已有该专项覆盖。|
|B-B2|采纳为未解决稳定性项|实际有约8.26e-6面积漂移，不能写零增量，更不能凭总面积近似不变证明无重叠。逐片交集/差集及时间键快照是下一步证据；不改阈值吞残片。|
|B-B3|采纳语义说明|保留先反馈动作的顺序；cane.tap表示已触发，结果失败仍会暂停并拒绝直接保存。|
|C-C1/C-C2|部分采纳|计时已执行并记录。缓存净收益/到期与恢复阶段耗时对照仍未做；跨机器硬上限须先定环境，不直接把50ms写入所有机器的单测。|
|D-D1/D-D2|条件采纳，暂不改代码|按依赖和入口识别旧版专属文件；拒绝凭mtime将39个文件一概标LEGACY或拆除实际共用类。|
|D-D3|部分采纳，后续内容管线|现有构造守卫和Role09并非零校验；可以补准确的工厂出口校验，不把所有声音都限制到可走面。|
|D-D4|暂不移动，保留内容问题|鸟源落在建筑平面内是事实，二维位置无法证明屋顶/室内意图；先明确声源的空间语义，不凭此自动挪地图。|
|D-D5|保留为必须继续的需求缺口|R15可见灯具/灯态与D12路沿坡道高程未实现，不能由总控单方改成范围外。接口方案已在MAP_TRAFFIC交接，尚无实现PASS。|
|D-D6/D-D7|采纳，下一维护阶段实施|信号常量集中、暂停文案准确化有价值；当前不为低风险文案再更换已冻结构建。原稿“没有真正重开入口”不准确，结算再走一次已有重开行为。|
|E-E1/E-E2/E-E3/E-E4/E-E5|采纳，已完成|Ordinal/-Force/verifier哈希、保护失败硬门、Player坏路径失败留证已落实。工具独立fixture及真实后续阶段分别留证，不混为同一种验证。|
|E-E6/F-F5|采纳，已完成|Unity报告仅列真实PNG、路径与哈希并记uniqueScreenshotCount；补跑032608-6ee7a91a为2/2，5文件仅4种唯一图，不重写旧结果。|
|E-E7/E-E8|不采纳排除监控的建议|完整Assets/ProjectSettings属于可信源码身份的一部分。若Unity改写，保留差异并重跑；当前各阶段均sourceUnchanged=true，不预先删除/排除模式或忽略项目设置。|
|F-F1/F-F2/F-F3/F-F4|采纳，更新索引与验收|README、状态/接续和性能文档已更新，最终QA四文档由独立角色更新当前证据并保留历史失败。无指纹旧PASS或顶层PASS但语音FAIL不得当当前依据。|
|G-G1/G-G2/G-G3|部分采纳，保留覆盖待办|Navigation已实际用于敲杖、声纹强度有视觉消费，旧断言纠正；语料穷举自检/受损路径反馈值得补，但不等于当前所有路径均静默失效。音频池拒绝上下文与真实设备听辨仍需验证。|

最终实测：C272源码Edit169/169（032313）、Play2/2（032608）、Build PASS（032431），各阶段源未变；两档Visible Player（032551、032618）及中文交付目录启动（032724）均通过。00已看新图，内部格纹错向修复；完整235项构建文件SHA匹配。新版Unity verifier在截图元数据补正后哈希为 `CAA17773E508173C4C2BBE45AA47E44B843480352910577EE85A520D30BDABC3`，Edit/Build使用此前 `CFBA…` 版本，逐次如实记录，不宣称工具从未改变。

完整试玩在 `output/听路试玩_2026-10-16_街区复核版/`，不是全需求完成声明。探测+记忆本次13样本最大20.4839ms，软件敲杖Started约7.8–8.1ms；耳机实际延迟未测，长期残片、R15/D12及真人无障碍仍是后续工作。原246源码/7保存文件/464构建元数据本轮保护复核通过。

E-E5补实测：00以故意不存在的EXE路径运行verifier，进程返回1，`output/street-verification/20261016-033236-6446ae42/results.json` 正确记录FAIL与原因，未启动Player。这是预期失败分支检查，不是新构建运行失败。交付结束前重算当前544项源码SHA与032431构建清单完全相同。
