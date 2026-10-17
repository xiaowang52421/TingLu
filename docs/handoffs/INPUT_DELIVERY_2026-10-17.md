# 输入交付交接（2026-10-17）

状态：SOURCE_FIX_READY；Unity 测试、构建和 Player 输入验收均未运行。

## 约束与检查范围

- 阅读了 `docs/upgrade/DECISIONS.md`、`docs/upgrade/RESUME.md`、`docs/qa/upgrade/INPUT_UI_RECHECK_2026-10-16.md`、`docs/handoffs/UPGRADE_INPUT_UI_AUDIT.md`，并核对输入相关的角色交接记录。
- 在工作区根目录及 `Assets/TingLu/Input`、`Assets/TingLu/Tests/Upgrade/Role01` 和文档目录的适用层级未发现 `AGENTS.md`。
- 原 `D:\听路` 保持只读；未运行 Unity、Player、构建或任何测试。
- 生产源码改动位于 `Assets/TingLu/Input/StreetInputSampler.cs` 和获批的 `Assets/TingLu/UI/AccessibleMenuPanel.cs`；回归代码只在 `Assets/TingLu/Tests/Upgrade/Role01/`。本交接文件位于本任务明确要求的 `docs/handoffs/`。

## 代码改动

- `Assets/TingLu/Input/StreetInputSampler.cs`：新增原始 Input System 状态事件监听，按事件顺序识别右键按下沿。短按在同一个 Input System 更新内完成按下和释放时，轮询只能看到最终释放状态；在某些 Editor 更新模式里 InputAction 回调也不派发，因而之前的测试路径无法恢复短点击。原始状态事件保留了按下事件，现有 `InputState.updateCount` 去重仍保证每个更新最多产生一个探测。监听在组件禁用时解除，焦点丢失时忽略输入。`Clear` 后旧的按住状态保持阻塞，原始事件流观察到释放时立即解除阻塞，因此释放后同一更新中的新按下仍会被捕获。
- `Assets/TingLu/Tests/Upgrade/Role01/StreetInputSamplerTests.cs`：既有“同一更新内按下并释放仍只产生一次探测”用例现在明确确认采样时按钮已释放，再检查读取恰好产生一次探测；新增 `Clear` 后按住按钮、消费过渡读帧、同一更新内释放并重新按下的回归用例。
- `Assets/TingLu/UI/AccessibleMenuPanel.cs`：按视口宽高自适应计算菜单面板边距和尺寸，边距为对应维度的 5% 且最多 24 px，宽度上限仍为 440 px；删除低分辨率下固定 220 px 最小宽度和 1 px 最小高度。菜单标题、消息、字幕和按钮改用本次 `OnGUI` 持有的局部 `GUIStyle` 副本；标签用副本的 `CalcHeight` 配合 `GUILayout.MinHeight`，避免 GUILayout 延迟布局在恢复全局字号后按旧行高裁切中文文本。

## 窄视口公式边界核对

使用 `margin = min(24, viewport * 0.05)`、`areaWidth = min(440, max(0, width - 2 * marginX))`、`areaHeight = max(0, height - 2 * marginY)` 核算。左右边缘满足 `marginX >= 0` 且 `marginX + areaWidth <= width - marginX <= width`；上下方向同理，因此在零尺寸到正尺寸范围内都不越出视口。

| 视口维度 | 自适应边距 | 面板尺寸 | 结果 |
| --- | ---: | ---: | --- |
| 宽 200 px | 10 px | 180 px | 左右各留 10 px |
| 高 40 px | 2 px | 36 px | 上下各留 2 px |
| 宽 960 px | 24 px | 440 px（受上限约束） | 与原常规菜单宽度一致 |
| 宽 1280 px | 24 px | 440 px（受上限约束） | 与原常规菜单宽度一致 |

这是公式边界核对，未做 Unity GUI 实际剪裁截图；极小视口下菜单内容是否易用仍需可见 Player 检查。

## 自动化可验证范围

现有 Role01 fixture 及源码逻辑覆盖以下软件行为，但本轮没有运行 fixture：

- 右键单次探测、持续按住不重复探测、释放后再次按下可再次探测；同一 Input System 更新内短按释放仍触发一次；Space/Shift 不触发探测。
- 键盘四向移动、斜向归一化、鼠标水平相对位移转向、忽略垂直位移、反向设置，以及同一输入更新重复读取不重复应用 delta。
- Esc 帧抑制移动与探测；菜单态右键只确认、不产生玩法探测；玩法态读取 G 查询目标。
- `Clear`、焦点失去/恢复、菜单和玩法状态切换时清理待处理边沿、相对位移，并阻止恢复前已经按住的键或按钮泄漏进游戏。
- 固定镜头投影关系可从 `StreetCoordinates.ScreenMove`、`StreetCoordinates.CameraYaw == 0` 和 `StreetCameraRig` 固定旋转路径静态核对；Role07 已有镜头用例覆盖源码级旋转和跟随参数。
- 新场景 `StreetCrossroads.unity` 绑定 `StreetEntry`。街区输入路径没有触屏设备监听或触屏覆盖创建；旧 `DemoEntry` 的触屏控件属于保留的 FirstStep 路径，不会因新场景运行时连接触屏而自动加入新街区场景。

以上只能证明 C# 状态转换、接线和 fixture 预期。它们不能证明 OS 指针锁定、设备驱动产生的输入边沿、真实窗口焦点回调顺序、或玩家操作的手感。

## 仍需人工操作的项目

在总控统一验证后的正式可见 Player 中完成：

1. 以真实鼠标进行很短的右键敲杖，确认每次点击恰好一次、长按不连发；移动鼠标确认水平相对转向手感合适、垂直位移不改变朝向，镜头始终不旋转。
2. 进入暂停菜单时确认光标释放且可点击；从菜单恢复后确认光标重新锁定、隐藏，键盘/右键按住跨恢复时不会误移动或敲杖。
3. 实际 Alt+Tab 离开再返回，确认游戏暂停、光标释放、旧鼠标 delta 不导致转身，用户主动恢复后重新捕获指针。
4. 游戏运行中连接触屏设备，确认街区玩法没有新增方向、敲杖、聆听、暂停或目标虚拟按钮。
5. 由真人确认 WASD 对应屏幕固定方向符合预期，并完成真实鼠标灵敏度与操作舒适度判断。

这些项目必须在真实窗口/设备上完成；模拟输入 fixture 不作为真人体验证据。

## 本轮验证状态与其他路径问题

- 仅完成源码、场景引用和 Input System 1.7 API 的静态核对。EditMode/PlayMode、构建、Player 与人工输入验收：`NOT_RUN`。
- 只读边界复核确认并修复了 Clear 时持有右键、同一输入更新内先释放后重新按下会因旧拦截位而丢边沿的问题；Role01 已增加对应回归用例。失焦读帧会清空待处理按下沿，焦点恢复时 Clear 并丢弃首次过渡读帧；`OnEnable` 早于 Configure 时输入动作可能先启用，但未配置的 `Read` 返回空帧并清除待处理边沿，随后 Configure 再调用 Clear。以上是源码路径核对，真实 Unity 焦点回调排序未运行验证。
- 窄窗口菜单面板越界及可见 Player 中中文标题/字幕下半截裁切问题已在获批范围内修复；需由总控统一重验可见菜单剪裁和文本高度。
- 没有修改共享状态文档、场景、Bootstrap、原项目或存档。
