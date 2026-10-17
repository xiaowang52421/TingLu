# UPGRADE_INPUT_UI_AUDIT 交接

status: PARTIAL
role/model/effort: 输入与界面只读 QA；本执行环境没有独立返回实际 provider/model/effort 的工具元数据，实际配置不可核实。目标配置来自派发要求 gpt-6-luna/xhigh。
contract_version: StreetContract 1.0.0

## completed

- 按动态完整提示词阅读升级入口、需求/架构/分工/验收文档、正式契约及 Role01/11/12 提示词。
- 只读核对 StreetInputSampler、AccessibleMenu、AccessibleMenuPanel、StreetHudPresenter、StreetEntry、StreetSession、相关音频/呈现设置路径、新场景绑定及 Role01/11 测试源码。
- 发现确定的 P3 窄窗口布局边界：面板从 x=24 开始且宽度下限 220，宽度低于 268 px 时面板触发 220 px 宽度下限，宽度低于 244 px 时右缘越界；高度低于 49 px 时面板仅 1 px 高。
- 向总控即时报告该问题，并将旧版 OutputMode 偏好仅保存的已知基线排除为新版阻断项。

## changed_files

- `docs/qa/upgrade/INPUT_UI_RECHECK_2026-10-16.md`
- `docs/handoffs/UPGRADE_INPUT_UI_AUDIT.md`

未修改生产代码、测试、场景、Bootstrap、其他角色交接或原项目。

## integration

- 未改接线。现有新入口通过 `StreetInputSampler` 读取 `StreetInputFrame`，Session 消费玩法与菜单输入，HUD 挂接 `AccessibleMenuPanel`。
- 音量由 `StreetEntry.Update` 写入播放后端；设置变更写入 Session 后保存到隔离 Options 目录。
- 需要 UI 所有者处理窄窗口面板尺寸；需要总控安排当前版本 Unity 验证。

## tests

- 源码静态审查：已完成，发现见 `docs/qa/upgrade/INPUT_UI_RECHECK_2026-10-16.md`。
- Unity/EditMode/PlayMode/Build：NOT_RUN；按任务要求没有启动 Unity 或构建。
- 键鼠、触屏、Alt+Tab、分辨率/窗口、音量与字幕实际体验：NOT_RUN；当前可用工具没有 node_repl 或 Computer Use 入口，且本任务禁止启动 Unity。
- 已有旧结果不认证本轮正在修复的源码。

## limitations

- 小窗口问题由布局约束推导，未做实际截图；优先级低（P3）。
- 没有发现输入状态机的确定源码错误，但这不等同于实机行为通过。
- OutputMode 的实际设备路由为已知旧基线限制；本次不作为新版本回归报告。
- VoiceprintIntensity 在 RenderPlan 控制视觉声纹；音频后端有单独强度乘数但新入口没有调用，产品范围未能从本任务规则确定，不判整项失效。

## contract_requests

- None.

## next_dependencies

- UI 所有者修复或确认窄窗口最小尺寸策略。
- 00 对修复后的统一源码版本排他运行 Role01/Role11，并复查真实 Player 菜单与输入生命周期。
- 12 在同一正式构建复核 A07/A25/A28 的集成部分。

