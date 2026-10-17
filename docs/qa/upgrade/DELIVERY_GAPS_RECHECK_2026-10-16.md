# 独立交付缺口复核（2026-10-16）
## 00 主控审核更新（04:33，当前结论）

主控已针对当前工作树完成重新冻结和全链验证：EditMode 169/169（`output/unity-verification/20261016-042726-44d669c2`）、PlayMode 2/2（`output/unity-verification/20261016-042745-0e5143e6`）、BuildWindows（`output/unity-verification/20261016-042800-da8da33f`），三阶段同一 `sourceFingerprint=92FA7E62070F6F1DF16EBE8CAD5A59472FED768AE814DB4610752D91C4A0802A`，且 `sourceUnchangedDuringStage=true`。隔离关卡编辑器迁移验证为 189/189，报告 `docs/qa/level-editor-unity-results.json`。

当前构建包为 `output/听路试玩_2026-10-16_街区升级重验版/`，235 文件、107963743 字节逐 SHA-256 匹配，清单 SHA-256 为 `DBB2F1108CC1DD66E08863D0DD7F14F663103F0068009E5C05489B26B2DF6FF6`。两档可见 Player `042922-7f5de651`、`042932-2fccbbcc` 均 PASS，软件语音为 STARTED；运行后全包哈希见 `package-post-run-hashes-20261016-0433.json`。PlayMode 当前共记录 65 条 `voice_limit`，包含竞争声道摘要，但没有池满释放后成功 Started 的专项链。

结论仍为**可运行的继续交付检查点，不是全部需求完成**。原地重探+5微片、音频池满释放专项、真实暂停菜单全屏、R15/D12专项断言/灯态坡道视觉、设备听辨、鼠标手感和真人无障碍仍开放。下方原 041xxx/D4F7 内容作为历史缺口复核保留，不覆盖上述 92FA 当前树证据。

## 历史中间证据（04:16，041xxx/D4F7）

独立复核完成后，00 已落盘 EditMode 169/169（`output/unity-verification/20261016-041145-5bdff160`）、PlayMode 2/2（`20261016-041209-ff817518`）、BuildWindows（`20261016-041245-0efcfd17`），最终包两尺寸可见Player亦分别通过（`041347-1364567f`、`041359-6dcb5c34`）。这些结果彼此对应 D4F7 验证快照；随后工作树在 4:17 修改了 `StreetSaveService.cs`，所以不能再写成“当前工作树同源”。对应最新机器索引仍需以结果 JSON 的 D4F7 指纹为准，索引中残留的 A99 指纹须修订或标为历史。

00 的取舍是保留 `StreetContract.Version=1.0.0`，把高程字段作为 schema-1 可选扩展，以维持已有存档契约兼容；原复核中引用“必须升到1.1.0”的动态任务草案已由主控修订，不构成当前失败。R15/D12 已有实现并进入上述同源构建，但仍缺专项功能断言、灯态/坡道视觉专项图；原地微片、暂停全屏、池满释放和人工设备验收仍是开放项。

状态：**DELIVERY_NOT_READY，READY_FOR_ROOT_REVIEW**。

本复核只读了现行需求、DECISIONS.md、UPGRADE_STATUS.md、Round 3 回复、当前源码和已落盘证据；没有启动 Unity/Player，没有运行测试，没有修改 Assets。已核读 041xxx 三阶段结果、Play XML、两次 Player 日志、截图哈希和新 source manifest。文中的“源码候选实现”不等于已编译、已集成或已在 Player 中验收。“设备/真人未验”单独标为证据缺失，不把它们虚构成软件故障。

## 历史版本边界（041xxx/D4F7）

新一轮 041xxx 证据已经形成同一验证快照：

|阶段|结果|证据|sourceFingerprint|
|---|---|---|---|
|EditMode|169/169，无失败/跳过|`output/unity-verification/20261016-041145-5bdff160/results.json`|D4F7F12A6BF2F6AB4EF5108A6F39640F012A2FA72044BC894F10CB4478F0773B|
|PlayMode|2/2，无失败/跳过|`output/unity-verification/20261016-041209-ff817518/results.json`|同上|
|BuildWindows|PASS|`output/unity-verification/20261016-041245-0efcfd17/results.json`|同上|

三份清单均为 544 项，且 041145/041209/041245 的 `sourceFingerprint` 均为 D4F7…；两次可见 Player 也成功启动并正常退出（`20261016-041347-1364567f`、`20261016-041359-6dcb5c34`）。但将 041145 清单逐文件与当前工作树复核后发现唯一不匹配：

|文件|041xxx 清单 SHA-256|当前工作树 SHA-256|结论|
|---|---|---|---|
|`Assets/TingLu/Persistence/StreetSaveService.cs`|`37FDF349374964FFE64B7AA6FFD0259EC88F9A7510BDDAE608CFB83E1DB76961`|`2DE0A66938B895D1A66828B4E0E8EA75175C97C2148E14E4D1E17C0D5680F422`|041xxx 构建后又发生修改，当前树不能直接沿用该构建结论|

因此 D4F7 结果可证明“041xxx 快照”已编译、通过基础测试并产出 Player，不能证明 4:17 修改后的当前工作树仍与该 Player 同源。`docs/qa/upgrade/current-stage-results.json` 和 `FINAL_RECHECK_2026-10-16.md` 将这组三阶段写成 A99A…；该值属于更早的 040xxx 快照，与 041xxx 结果 JSON 不一致，不能用作本轮证据指纹。C272 及其文件差异表均降为历史背景，不再作为当前源码结论。

## R15 老城区十字路口

**判断：PARTIAL；92FA 当前验证已接入基础运行链，需求整项仍未完成。**

静态证据：

- 关卡数据已有四向道路、四段斑马线、八块 ramp、八段 curb、连续/转弯盲道和提示砖：Assets/TingLu/Levels/StreetLevel.cs:134-187。
- 有盲道占用自行车、纸箱、施工围挡、停放车辆、四条信号车流、骑行者、行人和鸟声源：Assets/TingLu/Levels/StreetLevel.cs:61-126。
- 当前工作树的 AddSignal 已把两个信号对象改为永久小轮廓并给出高度：Assets/TingLu/Levels/StreetLevel.cs:205-213；相位仍由 StreetWorld.SyncSignalSources 驱动：Assets/TingLu/World/StreetWorld.cs:470-495。
- 当前工作树新增“仅在灯具轮廓实际被确认时产生、到相位边界失效”的灯态观察：Assets/TingLu/Perception/StreetPerception.cs:369-385，Session 以短时列表传给渲染：Assets/TingLu/Bootstrap/Integration/StreetSession.cs:26,49,72-73,196-199；RenderPlan 才按观察画红/绿：Assets/TingLu/Presentation/StreetRenderPlan.cs:93-107,250-260。
- 042xxx EditMode 的 169/169 已包含 Role03 信号相位、Role04 许可观察、Role08 灯态渲染计划、Role09 路线和 Integration 测试；这证明 D4F7 快照通过了代码/测试层的基础链路。由于 `StreetSaveService.cs` 在该快照之后又变化，不能把该结果直接扩大为当前工作树同源证明。
- 两次新 Player 的 explored 图只显示出生点附近一小块道路，未显示信号灯具或相位状态；截图也没有 NS/EW/clearance 对照。

尚未满足的验收条件：

1. 冻结或重跑当前工作树后，再取得与当前清单同源的 Role03/Role04/Role08/Role09/Integration 结果。
2. 新 Player 截图同时证明信号灯具可见，NS/EW/clearance 状态随同一模拟时间切换；遮挡或未覆盖时不泄漏灯态。
3. 新 Player 逐项目视确认斑马线可通行、路沿/坡道、盲道转弯和提示砖、占用与绕行、围挡、停放车辆、行人和行驶车辆均在实际场景中出现；确认 R16 禁放设施不存在。
4. A20/A21/A30 在同一最终构建中通过；提示音软件提交、实际设备播放和真人听辨分别留证。

## D12 路沿与坡道

**判断：IMPLEMENTED_IN_D4F7_SNAPSHOT；专项坡道/路沿视觉与当前树同源性仍未验。**

当前源码已具备高程数据通道：

- StreetSurface / StreetKnownSurface 有 HeightBase/HeightSlopeX/HeightSlopeY 和 HeightAt：Assets/TingLu/Contracts/StreetContracts.cs:36-50,165-178。
- StreetLevel.AddSurface 给人行道/路沿设 H=0.15，并给 ramp 设置斜率：Assets/TingLu/Levels/StreetLevel.cs:233-255。
- Perception/Memory 保留高程，Renderer 按已知面片逐顶点取高度：Assets/TingLu/Perception/StreetPerception.cs:423-424,501-517、Assets/TingLu/Memory/StreetMemory.cs:174-179,336-367、Assets/TingLu/Presentation/StreetRenderer.cs:235-256。

041xxx EditMode 169/169 已包含 `StreetRendererTests.KnownHeightRaisesContourAndLocalEdgesWithoutExtrudingKnownGround`、`StreetRenderPlanTests.MaterialIdsMapToStableSurfacePatterns("ramp",5)`、Role09 坡道路由检查以及 Integration 管线，说明这套高程代码在 92FA 当前树中编译并通过了已有自动化。冻结决策明确 `StreetContract.Version=1.0.0`、`SaveSchema=1`，高程是 schema-1 可选扩展；现行 1.0.0 与决策一致，不构成接口缺口。当前树的 `StreetSaveService.cs` 已在快照后变化，故存档相关结论仍需当前树重跑。

D12 关闭条件：

- 与当前树同源的版本中验证八片 ramp 的道路端为 0、高侧与人行道 H 连续，路沿公共边的高度差/立面只在双方均已知时出现；不得由未知面补几何。
- 新 Player 逐向走过四个过街坡道，确认无卡边/悬空/跳变；确认路沿的材质边界和可辨高差可见。
- 高程经保存、读档、恢复后仍一致，旧 1.0.0 存档按约定迁移；对应 Role10/A26 测试必须在当前源码运行。

## 探测重复的微小残片

**判断：OPEN；042xxx 阶段没有新增重复探测诊断。**

docs/qa/upgrade/PROBE_PERFORMANCE_2026-10-16.md:9,52 记录同一 13 样本路线的 Probe+Apply 合计中位 14.3171ms、最大 20.4839ms；8 次静止重探第一次由 206 增至 229，之后每次再增 5 片，最后 264，面积跨度约 8.2631e-6 m²。这不是性能超时结论，而是重复覆盖产生的几何碎片未解释、未证明长期稳定。

041xxx 目录没有新的 13 样本或 8 次静止重探结果；当前 `StreetMemory.cs` 虽在 D4F7 快照中通过基础测试，仍不能用旧 13 样本 JSON 关闭本项。

关闭条件：

- 在当前指纹运行同一路线和至少 8 次合法静止重探，保存每次覆盖、多边形并集/差集、片数、面积和边界段数据。
- 解释首轮 206→229 及后续增量；长期重复不应无界增长，不能靠提高面积阈值或删除已知区域吞掉差集。
- 至少补一个换位置/保存读档/恢复后的重复探测复测，确认不是固定位置偶然现象。

## 音频池拒绝与重试

**判断：PARTIAL；软件候选修复存在，当前交付证据不足。**

042xxx Play XML `output/unity-verification/20261016-042745-0e5143e6/playmode.xml` 中计到 65 条 `voice_limit_no_lower_priority_voice`。新记录已带被拒请求、优先级和 4 个竞争声道摘要，例如 `request=footstep.*` 或 `street.signal.*`，并列出 `active[0..3]` 的 source/priority；这能解释软件拒绝发生的竞争上下文，但不能证明关键反馈没有遗漏。

当前源码的候选修复包括：

- 活动声源按车辆危险优先、再按 source ID 稳定排序，并以 0.5/1/2 秒退避重试：Assets/TingLu/Audio/StreetAudioController.cs:10-12,72-79,130-175,213-236。
- 语音池满时保留已合成语音并退避重试：Assets/TingLu/Audio/UnityAudioPlayback.cs:506-590。

新 Player 的短时探针日志只出现两次缓存中文语音 `Started`，没有池满竞争场景；Play XML 也没有明确的 `retry` 或“拒绝后成功”链。因此候选重试仍未被专项证明，且当前树在快照后变化。关闭条件：

1. 冻结或重跑当前树后运行 Role06 和整合 A25/A30；拒绝日志保留请求、优先级、当前竞争者摘要/可解释原因。
2. 在池满、释放、暂停、Reset、代际变化和车辆危险声源场景证明：重试有界、成功后清理退避、旧代不复活、CriticalThreat 不被环境声挤掉。
3. 新 Player 日志至少证明资源命中/合成回退、Requested→Ready→Started/Failed 链。Started 只表示软件调用 AudioSource.Play，不能作为设备已出声。
4. 设备实际出声、听辨、方向/距离辨识及真人无障碍另做独立记录；未做这些测试不应标成软件缺陷。

## 暂停截图与状态

**判断：EVIDENCE_MISSING；逻辑通过不能代替 UI 全屏图。**

042xxx PlayMode 结果的 `screenshotScope` 仍是 Camera.Render 世界帧；HUD 和菜单需要单独的 Player 截图。新文件哈希为：

- `a25-paused.png`：`E554A9583BA5AB2A6C81ECE915B7BD14B613473FE1F1E6B60F34FE6ADF9296EC`，目检仍只有暂停时道路世界帧，没有暂停菜单覆盖层。
- `a30-continued.png` 与 `a30-real-collision-recovery.png`：同为 `C0065C543383986561669322D80962A6D6D84EA5ACFDB49D2053CBBD5CFEA463`，不能证明恢复前后是两个视觉状态。
- `a30-finished.png`：`AECAB1163B3B1267C24B74496187B2E6EDBD38D6C288E202D7E921F91C7C9EEE`；`a30-start-explored.png`：`B7084345C570C078A3877B166CF27B4C1E52294C32FC735F6ED48B1AE7B893DB`。新 Play 共 5 张、4 个唯一哈希，但仍没有真实暂停菜单全屏图。
- Round 3 回复也确认开始菜单/探索图已有，暂停菜单全屏图缺失：docs/qa/upgrade/ARCHITECTURE_ADVICE_ROUND3_REPLY_2026-10-16.md:16,27,43。

关闭条件：

- 用与当前源码同源的新 Player，在实际 Esc 暂停状态保存 1280×720 和 960×640 的全屏截图，能读到暂停菜单及正常菜单项，且无玩法虚拟按钮。
- 同一会话保留暂停前、暂停中、恢复后的图像和时间/车辆/信号/探测冻结记录；恢复/继续两张图不得只靠文件名区分。
- 真实鼠标焦点、锁鼠释放、Alt+Tab 和触屏连接属于另行的设备/人工验收。

## 其他交付需求

|需求/项|当前判断|精确证据与边界|
|---|---|---|
|格纹方向/内部地面碎片（A08/A12）|D4F7 快照基础图像通过；当前树需重验|041347/041359 两次 Player 图均有非黑内容并显示 HUD/地面纹理，但只覆盖出生点附近；`StreetRenderer.cs` 在 D4F7 快照内通过，当前树仍应在冻结后重跑完整路线与纹理核验。|
|永久/临时记忆、Continue、活动声源重算（R10/R13/D07/A26）|D4F7 快照 EditMode 覆盖；当前树需重验|042726 的 169/169 包含 `ContinueRestoresStaleExplorationAndResamplesLiveSourcesFromWorld`、A25 暂停/恢复、Save/Load 与 Role10 测试；当前 `StreetSaveService.cs` 在快照后变化，不能直接当当前树结论。|
|车辆碰撞、检查点恢复、终点链（R17/R18/A23/A30）|D4F7 快照 PlayMode 2/2；Player 路线证据不足|042745 Play XML 的 A25/A30 测试通过；两次新 Player 仅运行初始静止探针，未提供完整实际过街/碰撞路线图，且恢复/继续截图仍有同哈希。|
|输入、菜单和无屏幕玩法按钮（R03/R04/R06/D01/D02/D10）|源码/旧自动化存在；实机 NOT_RUN|INPUT_UI_RECHECK:11-13,21 仅确认代码路径；没有当前 Player 的实时鼠标、窗口焦点、触屏或暂停菜单证据。应写作输入/设备验收缺失，不写作已复现软件 bug。|
|被动听觉、活动声源定位（R05/R14/D03/D04）|软件路径部分存在；设备/真人 NOT_RUN|Player speechBackend=STARTED 只代表软件提交；current-stage-results.json 将 physicalAudioLatency、humanAccessibility 记为 NOT_RUN。|
|禁放设施和 HUD 不泄漏脚下文字（R16/R19）|静态部分通过，场景目检未完成|源码静态审计未发现公交站、店铺、树池、电线杆、井盖；完整 Player 场景巡检和最终 HUD/字幕图证缺失。|
|试玩包和原件保护（R01）|042xxx 包与 92FA 当前树匹配|`output/听路试玩_2026-10-16_街区升级重验版/` 有 235 项 package manifest，原件保护复核通过；当前树与该包同源。|
|A32/A34 流程项|不计游戏完成；仍按合规记录|模型后端元数据不可独立核实，A32 保持 NOT_RUN；早期动态提示词事后补存，A34 保持 PARTIAL，见 COMPLIANCE.md:3-17。|

## 总控重新验收门槛

1. 当前树已冻结并完成 92FA source manifest/fingerprint；042xxx 结果已替代 D4F7/A99 历史证据。
2. 042xxx 已重跑 EditMode、PlayMode、Build；仍需把灯态/坡道、音频池释放、完整路线和人工体验分别留证。
3. 重新构建 Windows Player，补暂停菜单全屏图、红/绿/clearance 灯态图、D12 坡道/路沿图和完整路线图；为每张图保存路径、尺寸、SHA-256。
4. 重做 13 样本与静止重探碎片诊断，保存逐片并集/差集证据。
5. 复查音频池拒绝上下文和重试后 Started/Failed；再单独安排设备出声、听辨、鼠标手感和真人无障碍验收。
6. 新包生成后再次核对 235 项（或新口径）清单、原项目/存档保护和文档索引，并使包与最终 source manifest 同源。

本文件只作独立缺口复核；未修改 Assets、未运行 Unity/Player。READY 后交总控审核并冻结本报告。





