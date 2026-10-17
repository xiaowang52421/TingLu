# 街区升级完整交接

## 2026-10-17 最新恢复入口

当前可运行包为 `output/听路试玩_2026-10-17_街区交付复核版/TingLu.exe`（保留完整目录）。当前源码指纹 `C22DCAC72C9FEFE366AB61E4A6A845CFDFED9285F54BC5957BE1520269D27EE2`：Edit184/184 `20261017-012625-474ab9b5`、Play2/2 `012658-aed888bb`、Build `012714-c16f466d`；均位于output/unity-verification，三阶段sourceUnchanged=true。两尺寸可见Player `output/street-verification/20261017-012750-ee20f67a` 通过；菜单裁字修复后已实际读图。235项包复制/运行后哈希及原件246/7/464保护通过。

本轮按用户最新“直接开8个子agent”完成8角色并行与主控统一集成。重复碎片已定位并修复，正式路线原地8次为266、267、267、267、267、267、267、267；音频池真实释放重试/代际重置、R15灯态许可/时效、D12八坡道高程/四剖面/存档已通过专项。主控还修复坡道轴及盲道路沿侵入路口、箭头与灯色埋地、色盲灯色、短点击/恢复漏按、菜单布局/裁字与启动错误退出。源码已冻结，读 `UPGRADE_STATUS.md` 顶部、`../qa/upgrade/current-stage-results.json` 与 `FINAL_RECHECK_2026-10-17.md`，不要继续以旧92FA缺口为现况重复修复。

仍需验证的是实际设备出声和物理延迟、真人键鼠/焦点/触屏与无障碍、启动错误故障注入，以及所有灯相位/八坡道的完整人工视觉签收。本次确定性Player实走覆盖出生→西检查点→NS灯→北侧过街→东落点；不能扩大为整关真人验收。全需求完成仍为false，原D:/听路和旧存档保持只读，用户不要求旧编辑器回归作新版前置。以下为各历史阶段，不是当前待办。

## 2026-10-16 04:30 当前继续交付检查点（最新）

当前工作树已重新冻结并完成同源验证，指纹为 `92FA7E62070F6F1DF16EBE8CAD5A59472FED768AE814DB4610752D91C4A0802A`。EditMode 169/169：`output/unity-verification/20261016-042726-44d669c2`；PlayMode 2/2：`20261016-042745-0e5143e6`；BuildWindows PASS：`20261016-042800-da8da33f`。三阶段均 `sourceUnchangedDuringStage=true`。隔离关卡编辑器迁移验证 189/189，报告为 `docs/qa/level-editor-unity-results.json`。

可运行包为 `output/听路试玩_2026-10-16_街区升级重验版/TingLu.exe`（须保留整个目录）。235 文件逐 SHA-256 复制核对通过；1280×720 和 960×640 可见 Player 分别见 `output/street-verification/20261016-042922-7f5de651`、`20261016-042932-2fccbbcc`，软件语音为 `STARTED`。仍不能把软件提交时间当成物理出声延迟，也不能把自动探针当成人类无障碍验收。

继续工作时优先处理：原地重探+5微片逐片交集/差集诊断和长期修复；音频池满→释放→成功 Started 专项回归；真实暂停菜单全屏图；R15/D12专项灯态/坡道断言与视觉检查；设备听辨、鼠标手感和真人无障碍。未完成这些项目之前，交付状态保持“可运行检查点”，不要标成全部需求完成。

## 2026-10-16 04:15 当前继续交付检查点（优先于以下历史）

总控已完成 R15/D12 最小实现接线并补充音频池拒绝上下文；当前同源证据为 EditMode 169/169 `output/unity-verification/20261016-041145-5bdff160`、PlayMode 2/2 `20261016-041209-ff817518`、BuildWindows `20261016-041245-0efcfd17`。最终试玩目录是 `output/听路试玩_2026-10-16_街区升级最终版/`，235文件逐SHA复制并在1280×720/960×640可见Player启动通过。源码/证据快照 `output/upgrade-handoff/20261016-041555`，status=SAVED_AND_VERIFIED，原项目246/7/464保护通过。

尚未完成的交付项仍是：原地重探+5微片的逐片根因和长期修复；音频池满释放专项回归与关键反馈影响；真实暂停菜单全屏图；物理出声/设备听辨、鼠标手感和真人无障碍。R15/D12虽已实现并随同源阶段运行，仍需专项功能断言和灯态/坡道视觉核验，不把本检查点写成全部需求完成。当前机器索引以 `docs/qa/upgrade/current-stage-results.json` 为准。

## 2026-10-16 当前可运行检查点（优先于以下历史）

运行 `output/听路试玩_2026-10-16_街区复核版/TingLu.exe`（整个目录须保留）。已完成敲杖计算性能整改和格纹连续性修复；当前C2726F指纹Edit169/169、Play2/2、Build、Visible Player两尺寸及中文交付路径启动通过。具体原始证据、工具哈希、截图与剩余限制见UPGRADE_STATUS顶部和 `../qa/upgrade/PROBE_PERFORMANCE_2026-10-16.md`。

Assets作者全部冻结，00已统一验证，不再有待交的TextureAxis实现。Round2/3分别由verification_review/architecture_review独立审阅，00完整读后已在各REPLY附最终决策，附件不直接触发修改。final_qa已独立核当前图像/计时/包及保护，00接手四文档完成汇总并冻结；随后生成源码/证据ZIP及再次保护复核，不把快照当游戏验收。

下一轮继续R15可见灯态、D12路沿/坡道高程接口实现；原地重复探测+5微小残片仍待逐片交集/差集快照取证，不能改大面积阈值吞掉。真实设备/物理出声与真人体验尚未验证。原D:/听路与原存档保持只读；本轮246/7/464保护复核已通过。下文为历史，不再按其旧失败/进行中结论重做已完成阶段。

## 2026-10-16 格纹连续性修复接续点（最新）

CAD6588性能阶段真实Edit161/161、Play2/2、Build及两尺寸Visible Player均PASS，详细范围与剩余增长见 `../qa/upgrade/PROBE_PERFORMANCE_2026-10-16.md`。当前已按用户新反馈修改TextureAxis链路，不能继承该阶段PASS；所有生产Assets作者已READY，验证工具作者完成ordinal/隐藏清单/verifier哈希/失败留证/保护硬门后由00统一Unity验证。原地+5碎片仍只读诊断，未再改几何算法。

用户新提交第二轮建议书，由verification_review独立阅读并写 `ARCHITECTURE_ADVICE_ROUND2_REPLY_2026-10-16.md`；建议采纳由00审核，子Agent不得自行扩大执行范围。原项目仍只读，不安装升级工具。R15/D12与人工验收缺口继续保留，下方为历史。

## 2026-10-16 性能整改接续点（最新）

稳定阶段 `98C682205FD570D14A9341ADBFCDF300BF901E3382C27126EE0692FE3C7804FA` 已通过 EditMode149/149、PlayMode2/2完整A25/A30、Windows构建，见 [阶段证据](../qa/upgrade/STAGE_BASELINE_2026-10-16.md)。当前用户指出约0.47秒敲杖反馈过慢，性能整改已改变Assets，不能继承该阶段PASS。

00负责Perception静态输入缓存、敲杖声请求前移、统一Unity与交付；geometry_fix负责Memory合并热点；audit_runtime已冻结13次正式路线/原地探测分阶段测量及声音请求顺序回归；render_performance_review负责Surface Mesh缓存与计时回归。等全部Assets作者READY后统一EditMode→PlayMode→Build，再对新EXE以 `Verify-StreetPlayer.ps1 -Visible` 做两尺寸检查并实际读图。本机隐藏窗旧probe曾生成纯黑假PASS，工具已硬化拒绝，不能沿用其视觉结论。

未通过性能实测前不宣称卡顿解决。R15可见灯具/灯态、D12路沿坡道高度仍为未实现缺口；声音设备听辨与真人无障碍仍需实际参与验证。原D:/听路只读，不安装升级工具。下方02:04等内容为历史。

## 2026-10-16 02:04 最新接续点

先读 UPGRADE_STATUS 顶部，覆盖本文件下方所有历史状态。最新冻结EditMode144/144通过，但同版PlayMode完整路线因 `StreetMemory.MergeTouchedSurfaces` 生成退化多边形而失败（1/2）。geometry_fix已获得事前完整提示词 `prompts/dynamic/20261016_DEGENERATE_POLYGON_FIX.md` 的Memory/Perception与Role04/05修复权；其他Assets作者冻结，00排他Unity。完整异常栈在 `output/unity-verification/20261016-020335-6fdcabf0/playmode.xml`；临时Session诊断改动已恢复。不能跳过故障直接打包旧EXE。

本轮已读两份架构审查并创建 `ARCHITECTURE_ADVICE_DECISIONS.md`；已补车辆首次接触、故障锁/存档保护、缓存语音、几何渲染等实现。后续需收几何READY，统一EditMode→PlayMode→Build→两尺寸Player与实图检查→完整打包→最终QA→哈希快照；真实声音/人工无障碍单列。原D:/听路只读，保护依据与最新检查仍保留。

## 2026-10-16 当前继续状态

本轮在用户“全部核实并继续”“子agent采用6 luna xhigh”授权下继续开发。最新现场基线为新版EditMode113/113通过，证据`output/unity-verification/20261015-232743-a02fe7f5`；退出卡住已如实记录。更晚磁盘已有Windows构建与旧截图，但存在地面径向黑缝、中文语音失败和音频池高频重试问题。四个子任务分别处理几何、语音、声源池与运行链复核/续玩断言；主控补Player等待语音结果与通关Camera.Render截图，统一跑真实Unity并交付完整试玩目录。改后验证尚未完成时不得继承旧PASS。

原源码246项与存档7项已在本轮现场重新哈希保护核对通过。此轮不安装/升级Unity或声音组件，不改原项目。README顶部已切换新版入口，旧M0说明下移为历史。最后可用状态以UPGRADE_STATUS顶部和最新原始结果为准。

## 最新验证入口

开发仍在进行，非暂停。生产集成条件宏已移除，StreetCrossroads已设为构建首场景。最新导入PASS：`output/unity-verification/20261015-171056-bef784ce`；新版全量EditMode `20261015-171729-08ca496d` 为108项101过7失败，输入/角色/关卡测试正在修复。首个稳定源码ZIP已实际生成在`output/upgrade-handoff/20261015-164432`，并核对原项目保护通过。当前运行状态以UPGRADE_STATUS.md顶部、最新Unity XML与独立QA报告为准，不沿用下方旧暂停结论。

## 2026-10-15 当前会话已正式恢复开发

用户已明确要求“继续做，可以直接开始，作为主控”。00已实际恢复8个执行Agent实现各自模块，并派新的独立上下文角色12准备跨模块与场景验收。当前真实状态见UPGRADE_STATUS.md、DISPATCH_LOG.md及TAKEOVER_2026-10-15.md。旧暂停点完整保留在下文；其中“暂停后不继续开发”是旧会话停止指令，已由用户最新恢复指令解除。

已现场实测8个子Agent同时running（加root共9）；不是配置推断。本会话首次接手发现旧文所称output/upgrade-handoff不存在，旧暂停快照未证实。原项目/存档保护不变。所有新增实现仍需当前源码的正式Unity验证，不能继承旧PASS。

## 2026-10-15 旧暂停点

本文件是新会话的第一入口，优先于旧 docs/RESUME.md 和各角色较早的状态。用户于本次会话明确要求“你先保存并完整交接”，已暂停开发；本交接不表示开发或验收完成。当前只写副本 D:\听路_街区升级，原 D:\听路 不得修改。

## 新会话可直接使用的提示词

> 继续 D:\听路_街区升级 的街区升级，由你担任总控。先完整阅读 docs/upgrade/RESUME.md、IMPLEMENTATION_CONTRACT.md、UPGRADE_STATUS.md、DISPATCH_LOG.md，以及其中列出的角色交接和验证证据，再阅读本轮需求、架构、分工、验收及各角色完整提示词。已有代码不要重做，修复真实失败并继续派发未完成角色，直至完成新街区集成、独立验收和 Windows 完整试玩目录。06/07旧评审文档是我删除的，不需要恢复；旧版已存档，集中新版，不把旧关卡/旧编辑器回归作为交付前置。主控目标 gpt-6.1-sol/high，执行与验收 gpt-6-luna/xhigh；用户希望提高到8个子Agent，但先核实新会话工具实际槽位，不得仅凭配置宣称已经生效。不创建其他用户聊天，不修改原项目和原存档。先处理交接列明的未验证源码/编译风险、失败测试和探测超时，再完成后续实现；自动化不冒充真人声音/设备/无障碍验收。按正式分工实际部署子Agent，所有导入/测试/构建窗口由总控排他协调。恢复工作不需再次询问常规开发许可。

## 用户确认与范围

- 用户已授权正式实施，00总控负责接口、调度、集成、验证、修复、记录和最终交付，不是仅写计划。
- docs/upgrade/06_分工评审意见.md、07_关于分工评审建议的回复.md 是主动删除，无需提供或恢复；失效链接已改说明。
- 用户说明旧的已存档，直接做新的。保留当前副本旧资产，旧回归不作为新版交付阻断；原 D:\听路 仅只读。
- 不升级 Unity、包或编辑器工作台。Unity 2022.3.62f1c1、Built-in、Input System 1.7.0。
- 固定北向斜俯视2.5D相机，只跟位置；WASD按画面方向移动、鼠标水平相对delta独立转身、隐藏锁定指针，右键按下沿一次扇形；无主动聆听、空格敲杖、玩法屏幕按钮、脚下材质文字。
- 被动四周听觉；移动原速度/消耗、静止原聆听恢复、低专注/混淆有效。连续几何扇形与真实遮挡，禁止玩法网格。永久已知地面面积与局部几何持续保存。
- 临时旧位置冻结，仅实际再覆盖部分作局部差集刷新，发现同ID新位置不可远程删旧位置。活动声源只短时合法声音定位，不揭地面/完整实体、不入永久档。
- 真实世界碰撞独立于显示。进入车道和红灯过街不自动警告、扣值或失败；真实车辆/电动车/骑行者碰撞立即回检查点，一事故一次恢复，无检查点安全出生；累计探索/碰撞不回滚。
- 普通二线城市老城区十字路口，详见01需求；有道路、人行道、盲道提示砖与占用绕行、过街信号音、坡道、建筑/小巷、临时物体及活动对象；禁公交站、店铺、树池、电线杆、井盖。

## 已读与权威资料

上位规则是用户后续指令 > 本轮需求/已确认决定 > 默认方案 > 旧资料。正式唯一接口为 docs/upgrade/IMPLEMENTATION_CONTRACT.md 和实际 Contracts/StreetContracts.cs、StreetGeometry.cs；集中配置 StreetConfig.cs。原0.2.0契约仍在，新街区不使用其旧输入/360°玩法。

本会话已读项目 README、升级文档入口、docs/upgrade 全部现存规范与角色任务、旧 RESUME/STATUS/INTEGRATION/OWNERSHIP/CONTRACTS/DECISIONS/DISPATCH/PROGRESS_REVIEW、编辑器指南及主要QA/交接。各角色共同规则重复段按一份读取；后续执行角色仍应读对应完整提示词。原 PDF/DOCX 通过 PDF/documents 技能只读提取，输出在 output/upgrade-baseline/original-architecture.txt、original-design.txt；未修改原附件。

## 环境、模型与并发的真实情况

- 当前工程无 Git 仓库，以源哈希和ZIP作阶段保存，不伪造提交。
- 实际 Unity：C:\Program Files\Unity 2022.3.62f1c1\Editor\Unity.exe。不要再报告Unity未安装。
- Python：C:\Users\xiaowang5242\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe。
- 主控目标 gpt-6.1-sol/high；实际provider/model/effort工具没有独立元数据，不能把目标值当实际证明。
- 已成功派发执行目标 gpt-6-luna/xhigh，fork_turns=none；返回任务名是调度证据，但provider实际元数据同样不可独立核实。
- 本会话最初工具明确4总槽，含主控，即3个子Agent。用户希望改成8个。只读核实 C:\Users\xiaowang5242\.codex\config.toml 当前已有 [agents] max_concurrent_threads_per_session = 8；本会话未改该配置。
- 后期 role03、role07完成停写后，成功创建role10、role09；活跃列表为root、role01、role09、role10。随后试图followup role03时工具返回 agent thread limit reached，所以没有证明本会话能同时超过3个子Agent，更没证明8个子Agent。不要将已结束角色数误算为并发。
- 官方Codex文档HTTP抓取两次403，不能当成功文档验证；output/upgrade-baseline/codex-multi-agent-docs.html为失败后空文件，没有引用价值。新会话需据实际工具槽位安排，配置8的计数含义不凭猜测断言。

## 当前落盘模块与真实状态

|角色|产物/状态|下一步|
|---|---|---|
|00|共享契约/配置/几何、所有Role测试asmdef、Shared fixture、主控接线草稿、工具/证据|继续集成与排他Unity验证|
|01|Input/StreetInputSampler.cs及7项NUnit；有UPGRADE_01|已跑4过3失败，尚未修，详见下节|
|02|Player/StreetPlayerController.cs及11项NUnit、对应meta；UPGRADE_02由停写角色补齐|新源码尚未Unity编译/测试，不能称通过|
|03|World/StreetWorld.cs及8项NUnit；UPGRADE_03|已跑7过1失败；同tick分离重入测试需核实真实时序|
|04|Perception/StreetPerception.cs及12项NUnit；UPGRADE_04|修meta后编译通过，但包含它的全量运行300秒超时，尚未定位卡住用例|
|05|未派发，只有共享接口/测试asmdef|优先实现连续面积并集和临时局部差集记忆|
|06|已派发/只读分析，只有UPGRADE_06，无生产实现/测试|实现StreetAcousticModel、StreetAudioController、后端声音目录与更新接口|
|07|View/StreetCameraRig.cs、5项NUnit及UPGRADE_07|5项真实Passed；实际画面手感未验|
|08|未派发，只有测试基础|基于04/05许可帧实现StreetRenderer，不查World真值|
|09|已派发但暂停前只交UPGRADE_09，地图草案未落代码/场景|实现StreetLevel、StreetLevelFactory.Create()、StreetCrossroads.unity和路线测试|
|10|Persistence/StreetSaveService.cs初稿、UPGRADE_10|未编译，缺meta和测试，有已知潜在语法错误，详见下节|
|11|未派发，只有测试基础|真实StreetHudPresenter/菜单与中文说明；退出项接线|
|12|未派发|全新独立上下文准备/执行跨模块、场景、同会话A25/A30，独立判A01–31；流程另表|

角色交接文件统一在 docs/handoffs/UPGRADE_XX.md。有些交接仍写暂停前的NOT_RUN或meta待补，以本文件与真实Unity日志为最新依据；保留原记录，不改写历史失败为通过。

## Unity证据与确定未解决问题

所有路径从工程根开始；证据均保留，不覆盖旧结果。

1. output/unity-verification/20261015-135803-ee02fe9c/results.json：首次导入PASS。
2. 20261015-140245-347b54ca/results.json：共享接口/测试基础导入PASS。
3. 20261015-140413-213eb1b7：全量EditMode 24项、22Passed、0Failed、2旧编辑器Skipped，根Skipped:Ignored，严格工具记FAIL；其中当时新增Role00四项全部Passed。旧Skipped不能改成全量通过。
4. 20261015-145333-e0c5cdd1：编译FAIL。主控 Stamp-UpgradeMeta.ps1 中数组拼接优先级使新Perception.meta分成guid:与下一行值，Unity忽略该脚本，Role04测试找不到类型。00已修脚本加括号，并修该meta；meta-format-repair.json有记录。后续编译已成功，初次FAIL保留。
5. 20261015-145448-79fd6c2c：过滤TingLu.Tests.Upgrade，编译/加载成功，开始测试后300秒超时，无XML。未证明所有模块通过；疑似Role04或其调用耗时，仍未定位，不要断言原因已证实。
6. 20261015-150041-b5472da8：排除Role04，过滤Role00/01/03/07，共26项22Passed、4Failed、0Skipped，真实XML和日志已保存；Unity完成结果后退出卡住，工具记录FORCED_AFTER_COMPLETED_STAGE，只结束该子进程。Role00 6/6、Role07 5/5、Role01 4/7、Role03 7/8。

四个失败（精确XML）:

- Role01 EscapePausesAndSuppressesMovementAndProbeOnThatFrame：StreetInputSamplerTests.cs:158，Expected None，实际 Previous。测试期待是否错误需结合菜单正常新按键规则核对，不为通过而删断言。
- Role01 MenuLeftClickIsLeftForTheUiAndRightClickOnlyConfirmsMenu：:138，Expected Confirm，实际 None。左键仍只交IMGUI，避免双确认。
- Role01 RightButtonPressAndReleaseInOneInputUpdateStillProducesOneProbeEdge：:114，Expected True，实际 False。当前 InputAction.WasPressedThisFrame + InputState.updateCount 方案未通过同一InputSystem更新中down+up，需修真实短点击沿。
- Role03 VehicleContactEmitsOnceUntilThePlayerSeparates：StreetWorldTests.cs:114，分离后重新接触Expected1、实际0。当前生产vehicleContactsThisTick只在World.Tick清理，而测试在同Tick多次查询。先核实测试应按真实世界Tick→玩家一次查询推进；不要盲目削弱一Tick事故去重。

暂停前新增的02/10不在上述已编译版本内，现磁盘不是已确认可编译状态。StreetSaveService.cs:460 存在 `suffix++.ToString(CultureInfo.InvariantCulture)`，角色10报告疑似语法错误，暂停后未修；接续者先检查，通常需对后缀自增表达式加括号。Role10没有meta/NUnit。Role09测试asmdef还缺TingLu.World引用，由00添加后可用真实World fixture。

## 正式接口/时序关键点（不要重新发明）

- Point2(X,Y)→Unity(X,高度,Y)，北+Y，正yaw从北顺时针转东；ScreenMove归一，鼠标delta.x×0.15度不乘dt。Probe完整夹角100°、半径7、显影0.8s、冷却3s；临时调参集中StreetConfig/GameplayConfig。
- 镜头yaw0、tilt52、size9、distance20，位置偏移0、指数跟随sharpness10；07 Follow(Point2,bool snap)，暂停00停止Follow，恢复snap。
- StreetWorld构造(surfaces,objects,routes,StreetConfig)；Move真实圆身扫掠/墙滑，VehicleContacts相对车辆连续运动；Capture不可变帧，Tick世界先于Player，时钟相同采样时刻。
- StreetRoute.Offset是初始累计路径米数，不是秒；StopLineDistance是从Points[0]累计到车辆中心停止线，要09考虑车身半长与斑马线裕度。ns绿14、clearance3、ew绿14、clearance3；世界驱动street.signal.ns/ew对象SoundActive，06不另算相位。
- 分类Permanent/Temporary/Active独立于Solid、VehicleHazard、BlocksProbe、AcousticLoss等。停放自行车普通伤害可15，不因类别自动变机动车事故。
- StreetObject尾部ContactDamage/ContactStun，对应StreetContact.Damage/StunSeconds，02空值回退原damageStatic。World.MaterialAt无覆盖返回null，不能发footstep.null。
- 04 Probe/Contact唯一产几何许可，Contact只接触点半径0.22局部，活动不产几何；声学ProjectAcoustics只许可/时效过滤，不二次查世界。05仅吃许可帧，不读真值。
- IStreetAudioUpdater.UpdateSource已由00声明，06令UnityAudioPlayback实现其已有同签名方法。StreetAudioController同步同一声学快照，优先保护车辆声。
- StreetSaveData含当前Player/Traffic和CheckpointPlayer/CheckpointTraffic、全关Memory、累计Collisions/Generation/ActivatedCheckpoints/Finished。schema1、kind tinglu.street、contract1.0.0，保存只用注入根+命名空间；未来主档/备份都要保护。
- 世界/人物回检查点时间，累计永久探索和碰撞不回滚，所有旧记忆CurrentUntil归0（含永久，防世界时间回退重新变亮）。失败同步回调可能恢复，旧Player.Tick必须立刻停止，不再写回旧状态。

## 主控接线草稿：尚未启用

Assets/TingLu/Bootstrap/Integration/StreetSession.cs 与 Runtime/StreetEntry.cs 都包在 `#if TINGLU_STREET_INTEGRATED` 内。这是临时隔离未交齐依赖，不是可运行入口；不要通过创建空生产替身“解决”编译。依赖齐备后00移除条件并真实编译/测试。

草稿已接World/Player/Perception/Memory/Audio/Save/Level/Menu、最大0.02s子步、失败代际恢复、检查点累计记忆、保存续玩及终点。Menu仅用旧InputFrame适配，玩法使用StreetInputFrame。CurrentSpeech封装菜单播报generation，恢复事件清输入/snap。实际API/异常边界仍需联调。

StreetEntry创建单相机和单AudioListener，Application.persistentDataPath+SaveNamespace隔离；诊断可用环境TINGLU_STREET_SAVE_ROOT。产品名已改TingLuStreetUpgrade，Settings用隔离Options目录内LocalSaveService。已有PlayerStartupProbe反射兼容StreetEntry，但仍沿用first-step-menu.png证据名；新关完整链诊断由12写。

待修/待核对接线：

- 11补菜单Quit事件/回调，00接StreetEntry.Quit；目前旧菜单无退出项。同会话首存档后Menu.canContinue刷新需补API。
- Retry在结果态应明确新一局/检查点恢复，现草稿逻辑可能恢复Finished快照；必须实际测。
- RefreshSenses在暂停恢复阶段的clock.Paused语义与06/04协调，恢复后才放输入，防旧声/旧观测。
- Audio.Sync实现UpdateSource后，StreetEntry重复foreach更新可消重。音频初始化、菜单语音和恢复异常仍待测。
- 09场景绑定StreetEntry脚本GUID固定88a335567c78436e97a3b26d0b733e90。当前BuildSettings仍FirstStep，不要误构建旧关冒充新版。
- 08运行时Shader.Find自定义shader有构建剥离风险，要求Resources/显式材质或由00配置AlwaysIncludedShaders，并实际Player验证。禁止高度或阴影向未知处延伸。

## 接续执行顺序

1. 核对新会话实际槽位/目标模型能力及哈希；不需要重新做基线。根据工具容量部署，明确唯一生产/测试范围。旧会话Agent不假定仍可复用，必要时新建执行Agent并完整派发提示词+正式契约。
2. 先修10编译风险/缺meta、01短点击与测试期望、03测试真实时序；定位04超时（定向到具体用例，限制超时，避免一遍遍盲跑全量）。可先用纯C#小进程反射加载已编译程序集做定位，不启动第二个Unity。
3. 尽快实现05记忆、06音频、09地图、11HUD及10存档测试；02编译/测试闭环。04完成后可立即派05，08只依许可帧与镜头接口；10无需等05/09。
4. 各波写入安全停止后由00跑Unity。主控拥有全部共享asmdef，先给Role09增加TingLu.World。不要在Unity导入期间让角色改Assets。tools/Stamp-UpgradeMeta.ps1只补缺失meta，已修数组优先级，仍须核对GUID。
5. 00启用StreetSession/StreetEntry、修实际API、切BuildSettings新场景。12必须新独立上下文、只写Diagnostics/Upgrade、Tests/Upgrade/Integration&Acceptance、tools/upgrade、docs/qa/upgrade和UPGRADE_12，不修改生产使测试通过。
6. 跑新版EditMode/PlayMode、BuildWindows、实际可见Player和截图检查；验证同会话A25暂停全子系统、A30新入口探索→绕行→过街→检查点→真实撞车恢复→保存退出续玩→终点。
7. 12独立逐项A01–31；A32–34另COMPLIANCE。真人听感/HRTF/输入设备/无障碍未做就NOT_RUN，不以软件Started冒充听见。
8. 保留失败原始证据，复查原项目/存档保护，冻结新版源哈希与完整Windows目录（不是只有exe），更新入口/状态/运行说明。

可复现命令（恢复开发后排他执行；暂停点不再启动）：

```powershell
pwsh -NoProfile -File tools/Stamp-UpgradeMeta.ps1
pwsh -NoProfile -File tools/Verify-Unity.ps1 -Stage EditMode -TestFilter TingLu.Tests.Upgrade -TimeoutSeconds 300
# 定位卡住项时只过滤一个Role或具体用例
pwsh -NoProfile -File tools/Verify-Unity.ps1 -Stage PlayMode -TestFilter TingLu.Tests.Upgrade
pwsh -NoProfile -File tools/Verify-Unity.ps1 -Stage BuildWindows
pwsh -NoProfile -File tools/Verify-Player.ps1 -ExecutablePath <新版构建exe> -Visible
```

Verify-Player前显式注入临时TINGLU_STREET_SAVE_ROOT，避免自动检查写真实新版试玩档。Windows Build目前仍输出TingLu.exe文件名但productName是新版；最终可整理目录名为新版街区试玩。独立QA不得拼接局部PASS假称A25/A30整项通过。

## 保存与证据定位

开发前基线在 output/upgrade-baseline：246源文件哈希及ZIP、7原存档哈希、464原试玩文件元数据。不要套旧文档“297个”数字。

本次暂停保存位于 output/upgrade-handoff/ 下最新时间戳目录：完整源码文档与本轮日志证据ZIP、源哈希、相对基线差异、原项目/存档/试玩保护复核、角色状态、缺meta清单及证据索引。ZIP不含Library/Temp等可重建缓存，不含可玩新版exe（尚未构建）。暂停后不再自动继续开发。
