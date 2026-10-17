# 关卡编辑器资产路径迁移

2026-10-14。编辑器通过 `LevelEditorWindow` 脚本的稳定 GUID 查找当前 MonoScript 资产位置，再推导所属关卡模块目录。默认 `Data` 和 `Editor/PreviewTemporary` 随模块移动，不再依赖 `Assets/TingLu` 的固定位置。移动模块时应保留内部 `Editor` 目录结构及所有 `.meta` 文件。

声纹显示材质通过原资产的稳定 GUID 解析，材质可独立移动或改名；丢失材质或其 `.meta` 时，试玩会给出中文错误并停止，不会静默使用空材质。

文件 IO 的绝对项目路径由 `Application.dataPath` 推导，资产目录创建不依赖进程当前工作目录。`AssetDatabase` 与场景保存仍使用 Unity 规定的 `Assets` 相对路径；文件面板依然支持项目外的绝对保存路径。工程可移动到其他盘符或用户目录，无需修改这些编辑器源码。

新增公共路径入口为 `EditorProjectPaths`，仅在 Editor 程序集中编译。移植时需要保留窗口脚本及声纹材质的资产身份；GUID 是资产身份标识，不是电脑路径。

窗口保存的关卡文件位置现在区分项目内外：项目内部使用项目相对路径，`Assets` 下的关卡同时序列化其资产 GUID。整个工程搬迁后相对路径从新项目根解析；关卡资产或整个模块通过 `MoveAsset` 改位置后，普通保存通过同一 GUID 找到新文件，不再写回旧目录。项目外的文件保持用户选择的绝对路径，即使有陈旧 GUID 也不重定向。

- `StoreFilePath(path)`：项目内部返回相对路径，项目外返回绝对路径。
- `FileAssetGuid(path)`：项目 `Assets` 内已导入的文件返回 GUID，其他文件返回空值。
- `ResolveFilePath(storedPath, assetGuid)`：相对资产优先按 GUID 找到当前绝对位置；外部绝对路径保持目标。已有 GUID 却资产丢失时，报中文错误，普通保存被阻止，用户仍可另存为。
- `SameFilePath(first, second)`：按解析后的绝对路径比较身份；Windows 忽略路径大小写。另存为仅在目标改变时生成独立关卡身份。

安全验证应在隔离的工程副本运行：保存一份临时 JSON → 断言窗口保存相对路径和 GUID → 只移动这份 JSON → 普通保存 → 确认仅新位置变化且 GUID/关卡 ID 不变；再将工程副本搬迁并从新位置验证相对路径解析。外部临时文件另测普通保存保持原绝对位置。任何移动整个模块/材质的验证必须位于可丢弃的工程副本，恢复失败时不得递归删除包含源码或 asmdef 的目录。核心 Unity 测试由实现 Agent 补充，避免重复改动其测试文件。

本子 Agent 未启动 Unity；实际迁移与编辑器编译由总控串行执行 Unity 测试。源码已移除授权两文件内的 `Assets/TingLu` 路径写死。此前独立执行现有 `Verify-LevelEditor.ps1`，编辑器 C# 语法解析及 7 组纯 C# 逻辑验证通过；本次恢复与相对文件路径补丁的两份 C# 源码再次通过语法解析。总控已报告本轮专项 9 组 PASS，实际 Unity 结果由总控补充；语法解析不替代 Unity 资产迁移实测。

总控最终补充：2026-10-14 实际 Unity 2022.3.62f1c1 在 C 盘中文空格目录的独立工程副本通过 20/20 EditMode（9 项编辑器专项、0 跳过），模块及材质在启动前已移动。JSON 测试只移动其专属临时文件，普通保存跟随原 GUID 到新路径并断言旧路径未重建；项目与 Data 不依赖 cwd，真实试玩及恢复通过。报告 `docs/qa/level-editor-unity-results.json`，日志/XML 位于 `output/level-editor-unity-verification/20261014-120454-75b2348f`。源码恢复证据见 `docs/handoffs/RECOVERY_COORDINATION_2026-10-14.md`，最终候选快照 `output/level-editor-source-20261014-120451.zip`。
