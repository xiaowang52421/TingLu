# 2026-10-16 当前交付阶段复核

## 04:30 当前源码同源重验（主控审核）

本次重新冻结的源码指纹为 `92FA7E62070F6F1DF16EBE8CAD5A59472FED768AE814DB4610752D91C4A0802A`。EditMode 169/169、PlayMode 2/2、BuildWindows 均 PASS，且三阶段 `sourceUnchangedDuringStage=true`；证据分别为 `output/unity-verification/20261016-042726-44d669c2`、`20261016-042745-0e5143e6`、`20261016-042800-da8da33f`。全量 EditMode 的两个 Ignored 用例已在隔离迁移工程中完成，隔离报告为 `docs/qa/level-editor-unity-results.json`（189/189）。

当前构建复制到 `output/听路试玩_2026-10-16_街区升级重验版/`，235 文件、107963743 字节逐 SHA-256 匹配，`package-manifest.json` SHA-256 为 `DBB2F1108CC1DD66E08863D0DD7F14F663103F0068009E5C05489B26B2DF6FF6`。两档可见 Player 均 PASS：1280×720 `20261016-042922-7f5de651`，960×640 `20261016-042932-2fccbbcc`；菜单和场景截图非黑且尺寸正确，软件语音到达 `STARTED`。探针测得初始完整 Tick 43.2819ms、敲杖请求 6.8723ms、软件 Started 7.0928ms；这些数据不覆盖物理出声延迟。

验收边界：这是一份当前源码的可运行继续交付检查点。原地重探+5微片、音频池满释放专项、真实暂停菜单全屏、R15/D12专项断言/灯态坡道视觉、设备听辨、鼠标手感和真人无障碍仍未闭环，因此不宣布全部需求完成。

## 04:13 继续交付复核（覆盖本文件下方 C272 记录）

总控继续实现 R15/D12 的最小链路并补充音频拒绝上下文后，当前源码指纹为 `A99A164A7AB83AD9B8A9356996B874F71B127E199C14F16DA26E84BFC2380270`。同源 EditMode 169/169（`20261016-041145-5bdff160`）、PlayMode 2/2（`20261016-041209-ff817518`）、BuildWindows（`20261016-041245-0efcfd17`）均通过；最终包为 `output/听路试玩_2026-10-16_街区升级最终版/`，235文件逐项SHA匹配。最终包 1280×720 与 960×640 可见Player分别为 `20261016-041347-1364567f`、`20261016-041359-6dcb5c34`，均PASS并有中文软件语音Started，Started仍不等于物理出声。

R15现在有永久信号灯轮廓、同相位边界快照及经Probe确认后的短时红/绿观察；D12现在有道路0、人行道/路沿0.15、八片坡道连续0→0.15的高程面，并贯穿感知、记忆、存档可选字段和已知面渲染。这些已随上述同源阶段运行，但尚缺独立专项灯态/坡道断言和暂停菜单截图，因此不扩大成“全部需求完成”。音频拒绝保留稳定错误前缀并追加最多四个竞争声道摘要；池满释放专项回归仍未完成。

本轮未关闭的交付阻断是：原地重复探测每次增加5片微小残片尚无逐片根因与长期修复；真实暂停菜单全屏、物理出声/设备听辨、鼠标手感和真人无障碍尚未验。故当前结论仍是**可运行的继续交付检查点，不是全部需求完成**。

编写：00；独立复核：final_qa。独立角色已实际读取本版两档Player四图和Play五图、重算13样本、核对音频池计数及包/原件保护。00审核其结论并汇总本文，未将未完成的需求写成PASS。

当前源码：`C2726FBC4722040FFCBE82C996EE148368649348C1CF27AA3399B6FFF346C717`。完整机器索引为 `current-stage-results.json`。当前544项源码SHA已与构建清单重新比对一致。

|证据|结果|文件|
|---|---|---|
|EditMode|169/169，无失败/跳过|output/unity-verification/20261016-032313-b86123fa/results.json|
|PlayMode|2/2，完整A25/A30|output/unity-verification/20261016-032608-6ee7a91a/results.json|
|Windows构建|PASS，阶段内源未变|output/unity-verification/20261016-032431-14337c7a/results.json|
|1280×720可见Player|PASS，HUD/主菜单实际读图、语音软件Started|output/street-verification/20261016-032551-cb1a1fc1/results.json|
|960×640可见Player|PASS，同上|output/street-verification/20261016-032618-52d30ea5/results.json|
|中文交付路径重启|PASS，独立存档根|output/street-verification/20261016-032724-5d2663cb/results.json|
|完整包|235构建文件逐项SHA匹配、107955082字节|output/听路试玩_2026-10-16_街区复核版/package-manifest.json|
|运行后全包哈希|235项仍一致|docs/qa/upgrade/package-post-run-hashes.json|
|原件保护|246源码SHA/7保存文件SHA/464旧构建元数据不变|output/recheck-original-protection-final.json|

EXE是Unity launcher，不同版本可有相同EXE SHA；整包身份依据完整payload清单与源构建目录映射。Edit/Build工具哈希CFBA…、补跑Play工具CAA177…分别保留；改截图元数据后只重跑相应Play，未改Assets或回写历史结果。旧032356中不存在的first-step截图字段不得作图证。

性能：13次合计中位14.3171ms、最大20.4839ms；Player初始完整Tick约49–51ms，通用敲杖软件AudioSource.Play提交约7.8–8.1ms。内部地砖错向已在新图中消失，稳定TextureAxis贯穿感知/记忆/存档/渲染；原合法探测边界未扩大。对比图 `output/street-verification/texture-comparison/texture-before-after.png`。

限制与后续：R15可见灯态、D12路沿坡道高程仍未实现；原地每次+5微片未解决；Play5张图只有4种唯一哈希，恢复/继续图不能证明两种视觉状态；53条声道池拒绝尚缺竞争上下文。现有全屏图包含HUD/主菜单，未补真实暂停菜单全屏。设备真实出声、听辨、鼠标手感及真人无障碍未验。历史113/149/161或无指纹结果和旧语音FAIL的顶层PASS只保留历史，不当本版证据。

结论：性能与格纹修复可运行检查点通过限定验收，完整试玩在 `output/听路试玩_2026-10-16_街区复核版/`。全需求尚未完成。源码/证据快照另由Save-UpgradeHandoff.py在文档冻结后生成并再次核保护，不以快照代替本表测试。
