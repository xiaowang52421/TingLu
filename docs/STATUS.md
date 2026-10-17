# 项目状态

2026-10-14 新增零代码图形化关卡编辑器。契约保持 0.2.0，原 M0《第一步》保持兼容。原始系统架构、策划案与旧角色提示词未修改；旧 PROGRESS_REVIEW.md 保留为历史复核，不代表当前文件状态。

## 已落盘的实现

- DemoSession 统一装配输入、角色、世界、声学、感知、静态记忆、首关流程、菜单与本地存档。
- DemoEntry 接入 Unity Input System、单一 AudioListener、独立相机、声纹渲染、菜单、触屏动作及退出存档。
- FirstStep.unity 已绑定 DemoEntry/VoiceLine.mat，Build Settings 指向该首关。Create First Step Demo Scene 菜单在缺失时生成入口，保留已有场景。已通过 Unity 2022.3.62f1c1 导入、编译及实际场景启动。
- 首关声音触发使用感知门槛；碎片奖励使用稳定世界 ID。窄遮挡只裁剪相交角域；动态对象不进入静态记忆。
- 音频声源使用独立子物体；暂停中的世界声音延后 Started 回执。菜单语音可在暂停中处理，暂停清理过时导航语音；播报和后台语音合成串行，播报期间压低氛围音。Windows Player 的直接 System.Speech/SAPI 兼容问题已修复，使用 Windows 自带 PowerShell 生成中文 WAV，再由 Unity 播放；真实 Player 日志已确认 speech started。
- 恢复事务最后才恢复输入；恢复/读档异常冻结模拟并保留可操作菜单。失败黑屏立即生效，碰撞计数不回滚。
- 所有 Assets 资源配套 meta；程序集引用与 GUID 有可复现检查。EditMode 测试程序集已建立。
- 中文关卡工作台源码：`TingLu/关卡编辑器`，模板复制、素材拖放、位置/尺寸/朝向、矩形区域、连续盲道、对白与合法感知触发、受控声音列表、撤销重做、草稿保存/重开、中文检查与错误定位。
- 版本化关卡配置编译为只读定义，进入正式 DemoSession。新关使用独立 ID 与剧情前置，保留原首关兼容；试玩主机通过专用临时场景和独立存档目录隔离玩家数据。Unity 场景切换、域重载、界面与设备尚未运行。

## 自动检查证据

以 docs/qa/*.json 和实际检查命令为准：

| 检查 | 证据 |
| --- | --- |
| 公共契约 | core-results.json；11 项通过，含冻结状态下的时间恢复 |
| 感知/记忆/表现 | Run-PureChecks.ps1；10 项通过 |
| 业务验收 | acceptance-results.json；涵盖 T01–T18 和新增恢复/认知边界项 |
| 音频/菜单 | audio-ui-results.json；菜单、声学、队列及源码生命周期审查；语音合成单独记录 |
| 生产装配 | demo-results.json；11 项，另含正式 JSON 路线冻结/续玩相位/三星与旧存档迁移兼容 |
| 工程结构 | project-results.json；meta、GUID、程序集无环、场景/材质引用及 IMGUI 依赖 |
| 关卡编辑器 | level-editor-results.json；保存/重载、稳定 ID、复制引用、草稿与未来保护、坏数据拒绝、合法感知、真实配置注入、独立关卡结算及存档目录隔离 |

Verify-Demo 运行生产 DemoSession 与真实 LocalSaveService，但使用记录播放请求的测试音频后端。完整路线通过表示输入/玩法/存档闭环，不表示真人凭听觉可通关。Verify-AudioUi 的源码审查也不表示 Unity 后端运行通过。

编辑器专项和既有回归证据保留在 `docs/qa/level-editor-*`。纯 C# 专项使用保存重载后的自定义出生、对象、盲道、对白、音效、检查点、碎片及终点走正式 DemoSession。完整工程目前已在 Unity 编译；关卡编辑器工作台的拖放和临时试玩流程仍需人工验收。操作说明和完整交接见 LEVEL_EDITOR_GUIDE.md、handoffs/AGENT_09.md；人工验收清单见 qa/LEVEL_EDITOR_ACCEPTANCE.md。

## 2026-10-14 Windows 试玩交付

最终 Editor 验证：独立 C 盘中文/空格工程副本，预先迁移关卡模块与材质后执行全部 EditMode，20/20、0失败、0跳过，正常退出。覆盖窗口、真实 Undo、保存跟踪、路线编辑命令、场景序列化、实际进入/退出试玩、隔离存档及未命名场景恢复。报告 `qa/level-editor-unity-results.json`；原始证据已归档 `output/editor-verification/20261014-120454-final`；最终源码 ZIP `output/level-editor-source-20261014-120451.zip`。鼠标拖放、弹窗选择与真人听感仍待验收。

最新配置版入口：`output/听路试玩_2026-10-14_配置版/TingLu.exe`。正式场景绑定 FirstStep.json，内容和人物路线来自配置；旧存档保持稳定的关卡、对象、剧情与检查点标识。PlayMode 3/3、Windows 构建与实际程序启动通过，启动探针确认 FirstStep TextAsset 已绑定；中文语音 started 确认通过。将整套文件搬到 C 盘中文/空格目录，从系统临时目录启动也通过。旧试玩包独立保留。

证据：`output/unity-verification/20261014-115511-b63d63d9/results.json`、`output/unity-verification/20261014-115550-03987604/results.json`、`output/player-verification/20261014-115711-f159655d/results.json`、`output/player-verification/20261014-115755-a5cdf0b5/results.json`。首个可见探针运行期间进入 Playing，严格的 Main 断言因此失败，保留 `20261014-115637-0af33f55`；后续隐藏启动通过，不能据隐藏截图声称布局已人工验收。

试玩入口：`output/听路试玩_2026-10-14/TingLu.exe`，配套说明为同目录 `开始体验.txt`。程序依赖同目录 Data、DLL 与 Mono 文件，不能单独拷贝 EXE。

最新真实 Unity EditMode 20 项和 PlayMode 3 项通过；PlayMode 加载正式 FirstStep 场景，验证启动主菜单、单一监听器、真实 AudioSource 空间位置、暂停延后播放和旧代际拒绝。Windows 开发构建成功；真实渲染模式 Player 启动探针确认首关/标准模式主菜单、会话创建、单一监听器和无启动异常。启动日志确认中文 WAV 进入 Unity 播放，尚未由人实际听辨。

汇总证据在 `docs/qa/unity-results.json`；原始日志/XML/构建在 `output/unity-verification`，Player 探针在 `output/player-verification`。部分编辑器测试写完结果后卡在退出，验证脚本仅停止自己启动的实例，并以 `FORCED_AFTER_COMPLETED_STAGE` 记录；不修改测试结果。后台隐藏 Player 截图为黑色，不能用该截图证明可视菜单布局正确。

本轮还修复设置页 Esc 返回暂停页、菜单显示模式缓存、IMGUI 状态污染；补齐手柄 D-pad、纯音频触屏菜单操作及目标重听；声纹渲染器复用排序/定位缓冲。失败的感知 EditMode 用例修正为允许动态短时回波但禁止永久记忆，未放宽认知规则。

## 尚未验证与限制

- Unity 安装位置为 `C:/Program Files/Unity 2022.3.62f1c1/Editor/Unity.exe`。导入、编译、EditMode/PlayMode、Windows 构建和 Player 启动已执行；实体键盘/手柄/触屏完整通关及正式界面布局尚未由人验证。
- 音效为占位合成音；实际空间听辨、材质听感、耳机/扬声器、端到端延迟与中文声音听感未测。
- Wwise、HRTF、移动真机、震动输出、系统读屏、视障玩家测试未接入或未运行。输出设备选项当前保存偏好，未路由设备。
- 首关无车辆/车门危险；回执公平性使用替身验收，不能代表硬件可听保障。
- 编辑器鼠标拖放/文本输入、关闭未保存弹窗及真人设备操作仍待验收。第一版需要 Unity，人物移动路线已可配置；未提供独立 EXE 编辑器、任意音频导入或编辑器内试听。

G0–G3 具有源码、自动逻辑与 Unity 运行证据，已交付可启动的 Windows 工程 Demo。正式无障碍标准、真人纯音频通关与 G4 体验验收仍未完成。下一步体验首关并记录真实音频、界面与输入问题；详细入口见 README.md 与 INTEGRATION.md。

