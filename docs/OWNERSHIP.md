# 2026-10-15 街区升级所有权优先

当前执行以docs/upgrade/03_任务与并行计划.md唯一角色表为准；公共契约/asmdef/Bootstrap/集中配置由00，01–12仅各自目录与测试范围。动态指派记录docs/upgrade/DISPATCH_LOG.md。下文旧角色编号为历史，不得据其越界。

# 目录所有权

| 角色 | 独占目录 |
| --- | --- |
| 00 | Contracts、Bootstrap、Config/Shared、共享 asmdef、Packages、ProjectSettings、Scenes/Startup、docs 顶层、tools 总控工具 |
| 01 | Assets/TingLu/Input、Player、World；对应 PlayerWorld 测试 |
| 02 | Assets/TingLu/Perception、Memory；对应 PerceptionMemory 测试 |
| 03 | Assets/TingLu/Audio、AudioAssets；对应 Audio 测试 |
| 04 | Assets/TingLu/Presentation、Shaders、VisualAssets；对应 Presentation 测试 |
| 05 | Assets/TingLu/Levels、Narrative、Persistence、Scenes/Levels；对应 LevelPersistence 测试 |
| 06 | Assets/TingLu/UI、Accessibility；对应 Accessibility 测试 |
| 07 | Assets/TingLu/Diagnostics、Tests/Integration、Tests/Acceptance、docs/qa |

每个角色可写 docs/handoffs/AGENT_XX.md。模块自己的 asmdef 可提出内容，由 00 创建/修改后纳入统一依赖图。禁止修改其他目录、平行复制公共类型、改变包版本或删除他人成果。资源和 .meta 配套提交；总控最后检查全部 GUID。

分批调度可由一个子 Agent 顺序承担多个角色，但需分别交接。08 本轮不执行。

## 2026-10-14 编辑器任务追加授权

用户授权派发 09 零代码图形化关卡编辑器。本轮 09 可修改 Levels 及其内部新增的数据和 Editor 目录、新增关卡编辑器测试和 `tools/Verify-LevelEditor.ps1`；可对 `Bootstrap/Integration/DemoSession.cs`、`Bootstrap/Runtime/DemoEntry.cs` 及相关 asmdef 作必要且向后兼容的数据注入、试玩隔离和启动调整，并创建本任务所需的新 asmdef。05/00 不同时编辑这些实现文件。

09 可写 `docs/handoffs/AGENT_09.md`、`docs/LEVEL_EDITOR_GUIDE.md` 和 `docs/qa/level-editor-*`。公共契约、包、引擎设置、共享数值、其他业务模块仍保持原有所有权；新增契约须向 00 请求。完整范围和验收以 09 提示词为准。

