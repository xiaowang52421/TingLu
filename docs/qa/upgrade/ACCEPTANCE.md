# 街区升级独立验收记录

## 2026-10-17 当前复核（覆盖下方全部历史状态）

当前源码 `C22DCAC72C9FEFE366AB61E4A6A845CFDFED9285F54BC5957BE1520269D27EE2` 已同源通过 Edit184/184、Play2/2、Windows构建，三阶段分别为012625-474ab9b5、012658-aed888bb、012714-c16f466d。试玩包 `output/听路试玩_2026-10-17_街区交付复核版` 的235文件复制与运行后SHA一致，两尺寸可见Player `20261017-012750-ee20f67a` 通过并经主控读图。

重复探测、音频池释放重试/Reset、暂停冻结/恢复完整UI图、R15灯态许可及过期清理、D12八坡道轴/端点/四向连续剖面及保存恢复专项已闭合。修复南侧盲道/路沿侵入路口、箭头/灯色埋地、色盲灯态混同、右键短按/恢复漏按及菜单标题字幕裁切；最新图片确认字幕完整。下方“R15/D12未实现”“每次+5”“无暂停全屏图”等仅为旧阶段，不代表当前。

验收仍分层：自动化与确定性Player PASS不代替物理出声、实际键鼠/焦点/触屏、真人无障碍；单条路线图不覆盖所有灯相位/八片坡道独立视觉签收；启动错误页面有源码退出路径但未做故障注入。统一证据与边界见 `FINAL_RECHECK_2026-10-17.md`、`REQUIREMENTS_CLOSURE_2026-10-17.md` 和 `current-stage-results.json`。

## 2026-10-16 04:13 继续交付结果

本节覆盖下方较早的 C272 阶段表。源码指纹 `A99A164A7AB83AD9B8A9356996B874F71B127E199C14F16DA26E84BFC2380270` 同源通过 EditMode 169/169、PlayMode 2/2、BuildWindows；最终包 `output/听路试玩_2026-10-16_街区升级最终版/` 的235个文件逐项SHA匹配，1280×720和960×640可见Player均PASS。R15/D12已有最小实现并进入运行链，但专项灯态/坡道断言和暂停菜单截图仍缺。静止重探微片增长、音频池释放专项、设备/真人验收仍未闭环，故本节不把版本标为全需求完成。

## 2026-10-16 当前阶段复核（覆盖下方历史状态）

本节由00汇总，final_qa已独立逐张目检C272两档Player四图与PlayMode五图，复算计时、重复哈希、音频池拒绝次数及包/保护结果；其结论已交回。原始旧FAIL与旧表保留为历史，不直接代表当前版本。完整当前映射见 `current-stage-results.json`、`FINAL_RECHECK_2026-10-16.md`。

源码 `C2726FBC4722040FFCBE82C996EE148368649348C1CF27AA3399B6FFF346C717`：Edit169/169（032313-b86123fa）、Play2/2（032608-6ee7a91a）、Build PASS（032431-14337c7a），各阶段sourceUnchanged=true。两个验证器版本分别按结果内SHA记录；补跑Play已不再宣称不存在的first-step截图，5张图有4种唯一哈希。

|当前复核项|结果与范围|
|---|---|
|A08/A12历史品红/地面缺失与内部格纹错向|当前有限图证已修复：Player1280×720=032551、960×640=032618，含HUD和主菜单；同一已知地面格纹连续，未知区黑色、无品红。外缘扇形属于探测规则。历史截图FAIL不得当当前故障。|
|完整A25/A30逻辑链|Play2/2通过，含暂停时间冻结、碰撞恢复、存档续玩与完成断言；Camera.Render图片不含HUD/暂停/结算UI。|
|恢复/继续独立视觉态|未单独证明：两图哈希相同；最终recoveries=0是Continue后局部统计，不能据此否定此前恢复断言，也不能用相同图证明两态。|
|试玩包|完整235构建文件SHA复制一致（107955082字节）；中文交付路径032724可见启动/语音软件Started通过，启动后235项再次哈希均不变。|
|敲杖性能|Unity13样本Probe+Apply中位14.3171ms/最大20.4839ms；Player首次Tick约49–51ms、软件Started约7.8–8.1ms。非GPU帧率或物理出声延迟。|
|声道拒绝|Play XML仍有53条voice_limit_no_lower_priority_voice；缺完整竞争上下文，不能说无丢声。|
|原件保护|246源码SHA、7保存文件SHA、464旧构建长度/mtime本轮均未变；详细范围见output/recheck-original-protection-final.json。|
|R15/D12|未实现：可见灯具/灯态、路沿坡道高程。声音相位与平面材质不替代这些需求。|
|长期/人工验收|原地重探每次+5微片、末264仍待诊断；暂停菜单真实全屏、设备音频、物理延迟、输入手感和真人无障碍未完全验证。|

当前结论：本次性能与格纹修复通过限定验收，可运行试玩；**不是全需求完成或发布级认证**。以下为此前阶段的原始验收记录。

状态：阶段验收记录，尚非最终发布验收。版本/契约：`StreetContract 1.0.0`；Unity `2022.3.62f1c1`；场景 `StreetCrossroads`；平台：Windows Unity Editor PlayMode / batchmode 及 Windows Development Player。证据截至 2026-10-15 的 PlayMode、EditMode、构建与两档可见截图结果。Windows 构建成功，但当前渲染存在阻断故障，修复构建与人工体验仍待完成。

## 实测摘要

- PlayMode：`output/unity-verification/20261015-190344-f0c2524b/playmode.xml`，Acceptance 测试 2/2 Passed、0 Failed；结果 `results.json` 为 PASS。包括场景启动相机/AudioListener 检查和 `A25AndA30_RealSessionPausesRecoversContinuesAndReachesGoal`。
- EditMode：`output/unity-verification/20261015-194152-75ae4e76/editmode.xml`，`TingLu.Tests.Upgrade` 113/113 Passed、0 Failed、0 Skipped；结果 `results.json` 为 PASS。它证明所选自动化用例通过，不等于 A01–A31 全部通过。
- Windows Development Player：`output/unity-verification/20261015-195119-a4712a6e/results.json` 为 BuildWindows PASS，程序位于 `output/unity-verification/20261015-195119-a4712a6e/Windows/TingLu.exe`。两次播放器 probe 在 `1280x720` 与 `960x640` 均报告进程/场景/Session/单相机/单AudioListener/截图文件检查 PASS；这些结果仅验证启动与结构，不覆盖图像内容。
- 两张实际探索画面均经目检为失败：全画面地面黑色不可见，场景轮廓和标记呈品红色，没有正确的表面填充。证据：[1280x720](../../../output/street-verification/20261015-202506-70ec20f5/street-crossroads-explored.png)、[960x640](../../../output/street-verification/20261015-202651-87ab42b8/street-crossroads-explored.png)。初步归因是当前 StreetSurface shader/材质在目标 Player 渲染路径不受支持；修复仍在进行，必须用新 Player 截图复核后才能关闭。
- A25/A30 结果：`output/unity-verification/20261015-190344-f0c2524b/street-acceptance/a25-a30-results.json`。报告状态 PASS，场景为 `StreetCrossroads`，通关时模拟时间约 41.04 秒，累计碰撞 1，`finished=true`，最终截图列表为空。
- PlayMode 日志反复出现 `Street audio failed: voice_limit_no_lower_priority_voice`。此记录不推导真人必然听不到，但属于待主控在 Player 构建中复核的声音输出问题；自动化状态或日志均不构成听感证据。
- PlayMode batchmode 本身未生成 A30 路线截图（JSON 的 `screenshots` 是空数组）。随后独立 Windows Player probe 生成了启动菜单及出生点探索画面，但这不是 A30 通关过程截图。Player 结果中的 `visualReview` 初值为 `PENDING_IMAGE_INSPECTION`；本独立目检结论为 FAIL，不以 probe 结构 PASS 覆盖该结论。

## A01–A31

`PASS` 仅用于下表说明的自动化验收范围。任何未完整覆盖的整项保持 `NOT_RUN`；画面、真实声音、设备与人工体验不由 batchmode 用例判定。

| 项 | 状态 | 证据与边界 |
| --- | --- | --- |
| A01 | PASS | `output/upgrade-handoff/20261015-164432/summary.json` 记录原工程246源码、7存档及464构建元数据保护复核通过；新Windows构建和运行证据均在副本 `D:\听路_街区升级\output` 下，Player probe 保存根为该证据目录的 `isolated-saves`。此项只确认路径隔离/保护核查，不代表构建画面可交付。 |
| A02 | NOT_RUN | PlayMode 有场景相机数量与正交性断言；未检查可见镜头跟随、倾角、鼠标转向时的稳定性。 |
| A03 | NOT_RUN | EditMode 覆盖水平相对位移输入到朝向；未在可见 Player 验证锁定指针、四向声像及实际人物朝向。 |
| A04 | NOT_RUN | EditMode 覆盖固定基向量、移动与朝向解耦及斜向归一；尚无真实键鼠操作验收。 |
| A05 | NOT_RUN | EditMode 覆盖右键单次探测边沿与空格/Shift 不触发；未做真实键鼠操作。 |
| A06 | NOT_RUN | 有专注与移动逻辑测试；未核对最终运行节奏、真实声学混淆或真人体验。 |
| A07 | NOT_RUN | 未检查实际 Player 画面和触屏设备。 |
| A08 | FAIL | 两档实际Development Player探索截图都未显示已知地面/纹理，轮廓和标记呈品红色。结构probe PASS不能覆盖渲染FAIL；修复后需重构建、复查黑暗区域和纹理可辨性。 |
| A09 | NOT_RUN | 感知扇形/覆盖单元用例通过；未在成品画面验证连续合并与探索边界。 |
| A10 | NOT_RUN | EditMode 覆盖冻结位置/朝向的扇形几何；尚未验证实际显示时序与交互。 |
| A11 | NOT_RUN | 有记忆与覆盖更新单测；没有可见运行的探索保存/返访证据。 |
| A12 | FAIL | 两档实际Player截图的已探索场景仍为黑底，未显示地面面积/材质填充，直接违反“不能只显示道路轮廓线”的标准。需要修复后用相同probe重新取图核验。 |
| A13 | NOT_RUN | 有遮挡与墙后覆盖单测；没有实际截图复核未知区域泄露。 |
| A14 | NOT_RUN | Level/路线和终点自动化覆盖部分通行性；未完整目检建筑、小巷及禁放对象要求。 |
| A15 | NOT_RUN | 有临时/永久记忆和纹理规划用例；没有画面对比及旧记录辨识验收。 |
| A16 | NOT_RUN | 临时对象局部重探逻辑由 EditMode 覆盖；未做可见地图和时间流程核验。 |
| A17 | NOT_RUN | 同 ID 迁移与局部覆盖有记忆单测；未独立跑完整可视场景验收。 |
| A18 | NOT_RUN | 声学模型有活动声源与方向数据用例；没有实际播放、设备或人工听辨。PlayMode 日志包含 voice limit 拒播记录。 |
| A19 | NOT_RUN | RenderPlan 有声音定位不绘制几何的用例；未做成品画面/声音联合检查。 |
| A20 | NOT_RUN | Role09 地图与路线自动化用例、真实场景加载及 A30 路线通过；未用最终 Player 截图核对清单中的所有街区设施。 |
| A21 | NOT_RUN | 世界/关卡测试覆盖信号路线、停车线、相位来源及多周期车流；尚未在最终可见 Player 同步核对信号提示音和画面。 |
| A22 | NOT_RUN | A30 实际路线过街且无碰撞前置失败；未覆盖红灯与各类车道的警告、扣值、失败、正常环境音全项断言。 |
| A23 | PASS | A30 同一真实场景会话未合成碰撞：路线车辆导致碰撞，断言恢复数 1、碰撞数 1、回到 `checkpoint.east.landing` 且未结束。只认证本次路线上发生的一次机动车碰撞恢复。 |
| A24 | NOT_RUN | 未运行无检查点恢复、连续接触防重入及普通非车辆碰撞的场景级完整验收。 |
| A25 | PASS | 同会话暂停自动化断言通过：时钟、车辆/信号交通状态、玩家位姿与敲杖冷却、记忆/感知帧保持，暂停状态与恢复后时序一致，菜单状态切换通过。实际鼠标焦点/指针和可听声音体验未验证；日志拒播项需结合 Player 复核。 |
| A26 | NOT_RUN | A30 证明碰撞统计/检查点可保存后 Continue 并完成路线；永久探索与临时记忆、活动声源在读档后的完整整合行为未由该用例核实。 |
| A27 | NOT_RUN | 自动化使用临时隔离存档根；最终 Player 安装、原项目存档与编辑器旧格式隔离尚未对成品包核验。 |
| A28 | NOT_RUN | EditMode 有失焦清输入用例；未验证 Alt+Tab、窗口/分辨率变化、锁鼠释放及恢复的大幅转向。 |
| A29 | NOT_RUN | 有许可帧渲染单测；未检查正式画面/字幕/导航是否泄露未知信息。 |
| A30 | PASS | 完整真实场景链通过：新场景启动、生产 Session 探索、绕开占道物、过街、到达西/东检查点、等待真实路线车辆碰撞并恢复、隔离存档、返回菜单、重载场景 Continue、绕行并到终点。JSON 最终碰撞 1、`finished=true`。`recoveries=0` 是 Continue 后新 Session 的局部计数重置；碰撞发生会话中 `recoveries==1` 已由测试断言。没有模拟碰撞、手动调用恢复或篡改感知。 |
| A31 | NOT_RUN | 这轮未对旧编辑器或 FirstStep 做运行回归；无需升级编辑器本身的范围说明不等于回归通过。 |

## 尚需完成

1. 主控完成 Windows Development Player 构建，再用隔离存档运行街区 probe，并检查 `1280x720`、`960x640` 截图及日志；该操作正在由主控协调，本记录不预判结果。
2. 对正式画面逐项核对未知区、纹理、建筑/阴影遮挡、HUD 和菜单布局；当前没有可供目检的 A30 截图。
3. 实际播放并听辨环境声、车辆方向/距离、信号提示音和碰撞恢复；记录设备、输出模式、失败日志与人工结论。不得把 `Started`、Session 状态或 EditMode 声学查询当成“听见”。
4. 鼠标/键盘焦点与窗口切换、设备适配及视障/无障碍体验仍需实机或真人测试；本轮未做参与者验收。

因此，本记录只认证上述自动化范围；在 Player 构建、可见画面、音频和设备体验证据补齐前，不代表新版可发布或已完成正式无障碍验收。
