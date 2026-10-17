# 2026-10-16 性能迭代前阶段基线

状态：阶段基线，**不是最终 QA、发布验收或当前工作树的认证**。记录冻结指纹 `98C682205FD570D14A9341ADBFCDF300BF901E3382C27126EE0692FE3C7804FA` 下已完成的 Unity 阶段。随后已重新开放 Memory/Perception 性能与重复三角化相关修改；最终验收必须以修改完成后的新指纹重跑并交叉核验。

## 统一指纹证据

| 阶段 | 状态 | 证据与结果 |
| --- | --- | --- |
| EditMode | PASS | `output/unity-verification/20261016-022328-b808b7b5`；XML 149/149 Passed、0 Failed、0 Skipped；`sourceUnchangedDuringStage=true`。 |
| PlayMode | PASS | `output/unity-verification/20261016-022408-6540f60d`；XML 2/2 Passed、0 Failed、0 Skipped；包含场景相机/AudioListener 检查与 A25/A30 真实场景路线用例；`sourceUnchangedDuringStage=true`。 |
| BuildWindows | PASS | `output/unity-verification/20261016-022449-a4afb3ad`；Unity 日志确认构建成功，`sourceUnchangedDuringStage=true`；构建产物 `Windows/TingLu.exe` 为 666,624 bytes，SHA-256 `CC858EB9A7E2DB2D414632600041100BD4840DD2F016593A242B145AFE6C5941`。 |

三阶段 `sourceFingerprint` 相同，均为上述 `98C682…`。EditMode 与 PlayMode 的 `source-manifest.json` 字节完全一致；按 `tools/Verify-Unity.ps1` 的清单生成及 SHA-256 方式复算，也得到同一指纹。三份 `results.json` 均报告阶段内源码未变化。该结论只覆盖这次阶段基线。

## A25/A30 自动化证据与图像复核

PlayMode 生成的 `street-acceptance/a25-a30-results.json` 报告 PASS：真实场景 `StreetCrossroads`，路线中发生一次车辆碰撞，保存检查点并 Continue，最终到达目标，`finished=true`，模拟时间约 41.04 秒。`recoveries=0` 是 Continue 后新 Session 的局部计数；用例在保存前已断言旧 Session 的碰撞恢复数为 1。该用例证明自动化链路，不代替 Player 键鼠、菜单、设备声音或人工可访问性体验。

五张 `Camera.Render` 世界帧截图均为有效 1280×720 PNG。目检记录如下：

- `a30-start-explored.png` 显示出生点周边已探索地面和未知区域；未见旧 Player 截图中显著的整片黑色地表缺失。
- `a25-paused.png` 显示暂停时的世界画面与路口区域；测试明确该图只包含相机世界帧，不含暂停菜单或 HUD，不能据此验收界面和指针行为。
- `a30-real-collision-recovery.png` 与 `a30-continued.png` 的 SHA-256 都是 `2A32093B7F9625C2E8DE7525241E846DAFFB8E7D8ACD0329E9D612CAEF4A0663`，所以是完全相同的图像。两者都在同一检查点和相机姿态取帧，适合作为恢复/续玩落点的辅助图，但不构成两个独立视觉状态的证明；碰撞与恢复过程由 PlayMode 断言支撑。
- `a30-finished.png` 显示终点区域世界帧，不含 Results HUD；完成状态由同一自动化会话断言支撑。

## 该构建的 Player 可见性探针

以下 Player 都来自同一个 `98C682…` BuildWindows EXE，SHA-256 为 `CC858EB9A7E2DB2D414632600041100BD4840DD2F016593A242B145AFE6C5941`。这些结果只认证阶段基线构建，不认证后来重新开放修改后的源码。

| 探针 | 结果 | 独立目检及边界 |
| --- | --- | --- |
| `output/street-verification/20261016-023336-fa2c4e02` | 历史 `results.json` 为 PASS，probe 与 `speechBackend` 为 STARTED | 两张 1280×720 PNG 的 SHA 都是 `5CAAF573FFC294827DF39B5DE5BF9F433AC2D1F443C29FF696D19FD12D36C47B`；目检均为纯黑。这里的软件启动/探针 PASS 不能作为画面通过。 |
| `output/street-verification/20261016-023510-34f5162b` | 硬化后的探针结果为 FAIL | `visibleWindow=false`，同样得到两张纯黑图；`reason` 明确要求以可见窗口重跑。此工具检查正确拒绝了黑屏截图。 |
| `output/street-verification/20261016-023430-37955fea` | 可见窗口 probe PASS，语音后端状态 STARTED | `street-crossroads-menu.png` 与 `street-crossroads-explored.png` 哈希不同。目检可见中文菜单、HUD 与已探索扇区，没有整图黑屏或品红材质。只验证一档 1280×720 的可见 Player 和自动化语音 Started；不代表两种分辨率、人工输入/可访问性或真人听辨均通过。 |

该阶段只保存了 1280×720 的可见 Player 图片。最终版本仍需在新 BuildWindows 上以可见窗口采集两种目标分辨率，并保留 exe 运行前后 SHA-256。`speechBackend=STARTED` 表示自动化后端启动，不证明扬声器实际出声或真人听清。

## 保留的失败与局部诊断边界

`output/unity-verification/20261016-021201-81e1c92b` 必须继续保留为 PlayMode **FAIL**：Unity 退出码 `-1`、没有完成 XML，不能改写成 PASS，也不认证 `98C682…`。其 `6E4773…` 指纹是旧阶段证据。

`output/pure-probe/RootMemoryRouteDiagnostic-observed.txt` 记录的是离线源代码诊断，不是 Unity 验收。第三次路线探测曾产生 19,502 个 surface，Memory 阶段未在该诊断窗口内完成；后续局部修复同路诊断为 1,766 个 surface、Memory.Apply 约 472 ms，并完成西侧检查点路线。此数字是本机局部观察，没有跨设备性能保证，也没有替代修复后的完整 Unity 计时或新构建。

## 最终复核仍需的证据

- 性能修改完成后，以一个新冻结指纹重新跑 EditMode、PlayMode、BuildWindows，并校验三个 manifest、测试 XML、阶段内源码稳定性和对应 Player 构建来源。
- 对最终 Player 做两种目标分辨率的真实运行和截图目检；记录运行前后 exe 哈希、中文路径完整包与保存隔离结果。自动化 `Started` 或 Unity 音频日志都不能代替设备实际发声和真人听辨。
- R15 可见信号灯/灯态及 D12 路沿/坡道高程语义仍按既定范围如实标注，不从相位、信号音、材质边界或二维通行推定完成。
- A32 实际模型/provider/effort 仍以工具可见事实为限；A33 是开发前文档阶段历史 PASS；A34 依 `COMPLIANCE.md` 保持 PARTIAL。它们与本阶段 Unity 结果分开判断。

在以上新指纹与 Player/人工证据完成前，不得把本文件描述为最终验收或发布通过。
