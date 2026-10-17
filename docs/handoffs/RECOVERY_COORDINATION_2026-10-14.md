# 源码恢复协调

游戏聊天根代理已从基线 + 成功操作日志逐条重放恢复 Levels 运行时源码；恢复过程及补丁在 tmp/source-recovery。运行时文件和 Data 由本聊天负责，Editor 子目录留给关卡编辑器聊天恢复，避免覆盖其新修复。FirstStep.json 使用原 CreateBuiltin 重新生成，资产 GUID 保持 2d8540f7bc21459ca6e8dae6763aa75b，正式 scene 已绑定。

原项目不再运行会移动生产 Levels 的迁移测试。关卡编辑器聊天承诺在独立工程副本运行新版迁移测试；游戏聊天等测试修复及 Editor 恢复完成后才重跑 Unity 全量检查。

运行时恢复完成：FirstStepFlow/Level/Progression/WorldFactory 用保留的 Unity 基线与原补丁重放；LevelDocument/Compiler/Validator/Template/MovementRoutes 用原新增及更新补丁重放。Demo 11 项和 LevelEditor 9 项已再次通过；Project meta/GUID/程序集 5 项通过。FirstStep.json 与 FirstStepTemplate.json 用生产模板生成，SecondLevelExample 用原命令生成空白独立示例。原 JSON 主资产 GUID 保留，其他缺失且无外部引用的运行时/数据 meta 重新生成。已在 tmp/source-snapshots 创建恢复后的源码快照。

编辑器聊天最终补充：Editor 源码经 `tmp/recover_editor_sources.py` 重放原成功补丁恢复，16 个补丁零 unmatched，重放记录位于 `tmp/editor-source-recovery/replay-report.json`。窗口脚本和材质 GUID 保留。破坏性模块 MoveAsset 测试已移除；新版在启动前仅搬迁独立副本，测试清理拒绝含 `.cs`/`.asmdef` 的目录。最终候选 `output/level-editor-source-20261014-120451.zip` 通过真实 Unity 20/20 EditMode，0 跳过、exitCode 0；报告 `docs/qa/level-editor-unity-results.json`。工作工程 Editor 与 Data 在测试结束后确认仍存在。
