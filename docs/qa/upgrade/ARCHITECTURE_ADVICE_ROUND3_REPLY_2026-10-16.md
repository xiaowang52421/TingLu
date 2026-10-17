# 第三轮架构建议独立回复

日期：2026-10-16  
性质：**独立审阅建议；最终采纳由 00 审核。**本文复核 `ARCHITECTURE_ADVICE_ROUND3_2026-10-16.md` 的 S1–S8，不替代主控决策，也不执行建议书中的动作。

## 审阅范围

对照当前验证脚本、源码、测试、QA 文档和已落盘结果作静态及产物核对。未启动 Unity 或 Player、未运行测试、未采集新证据、未修改 `Assets`。文中“已核对”只表示所列证据及范围，不代表设备听辨、人工无障碍或其他版本均已验收。

## 逐项意见

| 项 | 独立判断 | 建议价值与处理范围 |
|---|---|---|
| S1 证据目录必须有 `results.json` | **观察错误，窄化后仍有低成本价值** | 例举的 032551、032618、032724 实际位于 `output/street-verification/`，且各有 `results.json`；032608 位于 `output/unity-verification/`，也有结果。`texture-comparison` 下有对比 PNG 和脚本，并非空目录。Unity verifier 在创建 evidenceRoot 后，主 `try/catch` 前仍有初始化/编辑器发现步骤；可以把“可捕获异常尽量落 `NOT_RUN`/`FAIL` 报告”纳入后续工具维护。Player verifier 已用 `finally` 写报告。不要据错误目录清单宣称当前跨 stage 存在空目录，也不要承诺强杀进程或系统崩溃后仍能保证写盘。 |
| S2 TextureAxis 跨 Memory 合并不变 | **已覆盖，新增建议过时/重复** | 当前 `StreetMemory` 的复制路径传递原 `TextureAxis`，合并分组键也包含轴，输出合并面沿用该键。Role05 的 `AdjacentSameAgeSurfaceFragmentsMergeIntoOneConvexPiece` 已让两片同向轴 `(1,1)` 与 `(-3,-3)` 合并，并断言合并结果保留 `(1,1)`；若按新矩形重派生方向，该用例会失败。相邻异轴不合并也有独立测试。当前 C272 EditMode 169/169 已通过，无需重复加同一边界测试。 |
| S3 HUD/菜单全屏图像证据 | **部分仍成立：缺暂停菜单图** | 可见 Player 证据目录已有 `street-crossroads-menu.png` 和 `street-crossroads-explored.png`；032724 交付路径启动探针也保存这两张截图，脚本检查图像尺寸和非黑采样。后者覆盖探索画面/HUD，前者是开始菜单；PlayMode `Camera.Render` 世界帧确实不能替代 UI 截图。当前核对未找到暂停菜单全屏截图，因此若补视觉证据，范围收窄为暂停菜单即可；不必重新补一张已有的 HUD/开始菜单画面。本文未独立目检 UI 内容。 |
| S4 静止重探换位置复测 | **可作为诊断实验，原因推断过强** | C272 仍记录静止重探的表面片增长，其他位置做同次数、同探针序列的复测可帮助定位其是否依赖几何位置。可是“不同位置仍恒 +5”单独不足以证明固定结构浪费，“增量变化”也不能单独证明只是几何必然。若今后安排，需同时保留每次输入覆盖、裁剪片及并集/差集证据；不应仅凭计数判根因。本次不执行复测。 |
| S5 固化 13 样本性能 fixture | **核心建议已解决，门槛问题另计** | `StreetSessionIntegrationTests.RealRouteContinuousProbesMeasurePerceptionAndMemoryStages` 已命名并实现真实路线 5 次探测加静止 8 次重探，固定 `0.02` 秒步进，并断言 13 样本、合法冷却间隔、静止位置及几何增长边界。该路线已产出 JSON 性能记录。当前没有通用延迟硬门槛；机器口径和门槛仍需另行讨论，不能把 fixture 缺失再作为待办。 |
| S6 记录音频池拒绝上下文 | **仍有诊断价值，结论范围需收紧** | 当前拒绝分支的回执包括请求 `EventId`、失败状态和 `voice_limit_no_lower_priority_voice` 原因；`AcquireVoice` 可见活跃语音的 `SourceId` 与 `Priority`，拒绝时未将竞争者信息写入该回执。若后续处理已记录的拒绝事件，可增补被拒请求的来源/优先级和有限的竞争者摘要，帮助区分池饱和与重要反馈冲突。它能解释软件为何拒绝请求，但不能据此断定耳机实际听到什么或用户是否收到等价反馈；设备听辨仍是独立证据。 |
| S7 235 项与 464 项口径 | **澄清仍有价值，现有证据足以定性** | 235 是本轮 `output/unity-verification/20261016-032431-14337c7a/Windows` 构建被复制到中文交付目录的完整文件数，`package-manifest.json` 记载 SHA-256 且 `sourceAndCopiedBuildFilesMatched=true`。464 是原 `D:\听路` 基线记录的旧试玩文件保护集合，核验口径为长度/修改时间，不是该 235 文件清单的父集合或同一构建的子集。建议在主控汇总中把两种范围和校验方式并列说明；无需重算或重跑构建。 |
| S8 文档当前性检查 | **价值已被既有决定覆盖，避免另起重复机制** | `current-stage-results.json` 已集中链接当前 Unity/Player 阶段结果；Round 2 的主控审核已采纳状态/验收文档更新，最终 QA 正在对照当前指纹更新相关文档。将“索引与首页/验收表核对”放进这次既有 QA 收尾即可。若本轮更新后仍发生重复滞后，再评估是否需要持续性流水线检查；本条目前不构成新的独立机制。 |

## 证据定位

- S1：`tools/Verify-Unity.ps1:12-44,155-160`；`tools/upgrade/Verify-StreetPlayer.ps1:12-18,79-83`；`output/street-verification/20261016-032551-cb1a1fc1/results.json`、`032618-52d30ea5/results.json`、`032724-5d2663cb/results.json`；`output/unity-verification/20261016-032608-6ee7a91a/results.json`。
- S2：`Assets/TingLu/Memory/StreetMemory.cs:174-177,335-367,816-838`；`Assets/TingLu/Tests/Upgrade/Role05/StreetMemoryTests.cs:36-73`；C272 EditMode 结果 `output/unity-verification/20261016-032313-b86123fa/results.json`。
- S3：`output/street-verification/20261016-032551-cb1a1fc1/`、`20261016-032618-52d30ea5/`、`20261016-032724-5d2663cb/`；`docs/qa/upgrade/PLAYER_IMAGE_REVIEW_2026-10-16.md`、`STAGE_BASELINE_2026-10-16.md`。
- S4–S5：`docs/qa/upgrade/PROBE_PERFORMANCE_2026-10-16.md`；`Assets/TingLu/Tests/Upgrade/Integration/StreetSessionIntegrationTests.cs:256,382-447`；当前开放项索引 `docs/qa/upgrade/current-stage-results.json`。
- S6：`Assets/TingLu/Audio/UnityAudioPlayback.cs:152-170,355-381`；拒绝事件计数仅沿用既有 `PLAYER_IMAGE_REVIEW_2026-10-16.md` 的文档记载，本次未重跑 Player。
- S7：`output/听路试玩_2026-10-16_街区复核版/package-manifest.json`；`output/upgrade-baseline/baseline.json`；`output/recheck-original-protection-final.json`。
- S8：`docs/qa/upgrade/current-stage-results.json`；`docs/qa/upgrade/ARCHITECTURE_ADVICE_ROUND2_REPLY_2026-10-16.md` 的 00 主控审核记录。

以上为独立复核意见；最终采纳与执行范围由 00 审核。

## 00 主控审核结论

00已完整读原建议与本回复，并核对目录、现行Memory/Role05/Integration及交付清单；认可上述事实更正。当前C272构建保持冻结，本次审阅不自动触发代码修改。

|项|最终决定|处理与理由|
|---|---|---|
|S1|部分采纳，后续工具维护|原空目录举例因父路径错误而不成立；Unity前置初始化的可捕获异常留证可补。更稳妥的方向是创建运行目录后尽早写初始NOT_RUN，再由最终结果覆盖，不能承诺强杀/断电仍能执行finally。本轮不因错误目录例子重写验证器。|
|S2|不新增，已完成|现有45度合并轴回归正覆盖方向不重派生；当前169项已通过，保留该回归。|
|S3|部分采纳，保留暂停图待办|HUD/开始菜单已有两档全屏和交付路径图证；真实暂停菜单全屏尚缺，明确列未完成，不再重复拍已有两类图。|
|S4|采纳为诊断计划，尚未执行|后续在贴墙/路缘/开阔区保存逐片输入与差集轨迹再比较增长；不凭“恒5或变化”两种现象直接定根因。|
|S5|不重复实现，已有fixture|命名路线、0.02步进、5+8/13样本和冷却/增长断言已固定；机器环境与延迟门槛仍另行定义。|
|S6|采纳诊断方向，尚未实现|拒绝时记录有限的请求ID/来源/优先级及竞争声道摘要有价值；需控制日志体积，不改抢占策略来掩盖问题。该信息仅解释软件拒绝，不能替代物理听辨。|
|S7|采纳澄清，已完成|235为本轮完整交付构建全文件SHA；464为原项目历史构建集合的长度/mtime保护。二者不是同一清单的子集。235项在交付路径启动后再次全量哈希一致，证据见package-post-run-hashes.json。|
|S8|采纳收尾纪律，已完成本轮更新|当前机器索引、README、状态/接续、ACCEPTANCE、COMPLIANCE和FINAL_RECHECK均已更新并保留历史。以后固定执行“冻结源→同源验证→Player/整包核验→更新索引/首页/验收→文档冻结→快照”，不另起重复维护系统。|

本轮有价值的新待办主要是S3的暂停菜单图、S4的定位实验及S6的拒绝上下文；S1有防御性维护价值。其余是已有实现或范围澄清，不能当新故障。R15/D12、长期残片及设备/真人验收仍保留缺口，不将此次审核写成全项目完成。
