# 输入与无障碍菜单修整交接

status: PARTIAL
contract_version: 0.2.0

## 完成内容

- 修复暂停菜单进入设置后 Esc 无效的问题。现在 Esc 返回进入设置前的菜单；从暂停页进入时回到暂停页，模拟时钟继续保持暂停。
- 为菜单设置面板增加“调低”“调高”按钮，调用现有 `MenuAction.Decrease` / `Increase`，可用鼠标或触屏调整音量、字号、声纹强度等数值。
- 扩展菜单纯逻辑检查，覆盖设置项反向调整，以及暂停页设置按 Esc 返回时不调用 Resume。

## 修改文件

- `Assets/TingLu/UI/AccessibleMenu.cs`：设置页响应暂停键并返回来源菜单。
- `Assets/TingLu/UI/AccessibleMenuPanel.cs`：设置页提供调低/调高按钮。
- `Tests/Accessibility/MenuChecks.cs`：增加暂停状态和向下调整回归检查。
- `docs/handoffs/INPUT_UI_POLISH_2026-10-14.md`：本交接与未完成输入入口说明。

## 验证

- 执行 `tools/Verify-AudioUi.ps1 -ReportPath $env:TEMP/tinglu-audio-ui-input-polish.json`：通过。
- 通过项包括菜单焦点/禁用继续、暂停与恢复、结果和目标重听、设置调整与保存、语音优先级、音频后端生命周期源码检查，以及 Windows 语音合成 WAV 探针。
- 报告写在临时目录，没有覆盖 `docs/qa` 中由其他角色维护的结果文件。
- Unity 导入、编辑器运行、触屏/手柄设备操作和实际音频设备未运行；WAV 探针只证明合成器能生成音频数据。

## 需总控处理的输入接线

输入读取在 `Bootstrap/Runtime/DemoEntry.cs`，本轮由其他任务独占，未修改。检查到的具体缺口：

- 手柄只读取左摇杆移动，未读取 D-pad。
- `nextPadMenu` 使用时间间隔重复，切换玩法/菜单状态时没有清零，按住摇杆可能把旧导航节奏带入新菜单。
- 触屏菜单没有“返回”“调低”“调高”或“重听目标”动作。新增面板的调节按钮只在标准显示模式显示；隐藏界面和纯音频模式需要 DemoEntry 仍提供可触摸的菜单动作入口。

建议在 DemoEntry 为触屏增加 `Back`、`Decrease`、`Increase`、`QueryGoal` 菜单动作；读取手柄 D-pad 作为移动；在切换 Playing/菜单状态时重置摇杆菜单 repeat 状态，并避免摇杆持续按住时将过期导航带入新页面。无需新增公共契约。

contract_requests: none

## 2026-10-14 补充：面板显示缓存修复

- `AccessibleMenuPanel.OnGUI` 每帧读取当前 `menu.Options.Display`，只在 Inspector 总开关 `visible` 为 true、当前为 Standard 且菜单非 Playing 时显示。`Initialize` 和 `HandleInput` 不再缓存显示模式。
- 面板渲染结束后恢复按钮/标签字号、`GUI.enabled`、背景色和文字色，避免影响同帧的其他 IMGUI 绘制。
- 针对性检查：`Tests/Accessibility/MenuChecks.cs` 三组纯 C# 检查通过；PowerShell 源码断言确认动态显示条件及 GUI 全局值恢复。
- 再跑完整 `tools/Verify-AudioUi.ps1` 时被既有音频源码审查挡住：`speech start diagnostic reports provider and frame count without text`。该检查涉及 Audio 源码，与本次 AccessibleMenuPanel 更改无关；报告未生成新 PASS 结果。Unity 编辑器仍未运行。
