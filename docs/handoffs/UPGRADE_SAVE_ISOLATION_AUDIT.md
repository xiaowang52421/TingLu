# UPGRADE_SAVE_ISOLATION_AUDIT：存档隔离复核交接

status: READY_FOR_UNITY
role/model/effort: 角色 SAVE_ISOLATION_AUDIT；请求目标 gpt-6-luna/xhigh；实际 provider/model/effort 无工具元数据可独立核实。
contract_version: StreetContract 1.0.0；save schema 1

## completed

- 只读核对 `StreetSaveService`、`StreetMemory`、`StreetSession`、`StreetEntry`、旧 `LocalSaveService`、旧 `LevelDocument`/Editor 打开与保存链、新 `StreetLevelFactory` 及既有 Role10 测试/Unity 结果。
- 默认 productName + SaveNamespace 两层目录隔离有源码证据：升级副本 productName=`TingLuStreetUpgrade`、namespace=`TingLuStreetUpgrade`；原项目配置的 productName=`TingLu`。主档按 levelId 哈希命名，不复用旧 `LocalSaveService` 的档名。
- 确认一项要交00处理的实际隔离风险：`StreetEntry.Start()` 对 `TINGLU_STREET_SAVE_ROOT` 做无条件环境变量覆盖，生产 Player 也可被继承变量重定向到任意写入根。当前未见证据证明变量设置或旧存档被覆盖；默认 namespace/文件名也不同于旧 M0 档案。未读取变量当前值，未访问原档。
- 未来 schema/contract 对主档与备份拒绝读写；损坏档、备份回退、隔离坏主档、同目录临时文件及失败清理均在源码/Role10 测试中有覆盖。
- 永久探索可续存，读档/检查点恢复会将所有旧 `CurrentUntil` 归零；尝试累计碰撞保留；活动声源不进记忆 DTO。
- 新路口由 `StreetLevelFactory.Create()` 代码生成，旧 Editor 只保存 `LevelDocument` JSON；当前不存在新地图格式文件可被旧 Editor 静默覆盖。
- QA 报告已写入 `docs/qa/upgrade/SAVE_ISOLATION_RECHECK_2026-10-16.md`。

## changed_files

- `docs/qa/upgrade/SAVE_ISOLATION_RECHECK_2026-10-16.md`
- `docs/handoffs/UPGRADE_SAVE_ISOLATION_AUDIT.md`

没有更改 `Assets/`、`ProjectSettings/`、任何存档或其他文档。

## integration

- 00需收窄 `Assets/TingLu/Bootstrap/Runtime/StreetEntry.cs:42-47` 的诊断根覆盖；本角色不拥有 Bootstrap，未修改该文件。
- 推荐诊断覆盖只在 Editor/Development Build 或测试专用入口有效。最终产品验证使用临时测试根并确认 Player 不会因残留环境变量写入其他路径。
- 当前 `StreetLevelFactory` 与旧 `LevelDocument` 不共用数据文件，故无编辑器迁移/扩展请求。

## tests

- **本角色执行：NOT_RUN。** 按派发限制未启动 Unity、未构建、未运行任何测试或 Player；未写原存档。
- **既有证据（非本轮复测）：** `output/unity-verification/20261015-232743-a02fe7f5/results.json` 记录 `TingLu.Tests.Upgrade` 113/113 通过；同目录 `editmode.xml` 中 Role10 为 13/13 通过。该次 Unity 退出阶段 `FORCED_AFTER_COMPLETED_STAGE`。结果覆盖服务局部测试，不能证明最新整合 Player、生产环境变量路径或 A26/A27/A30 整链。
- `docs/qa/upgrade/REQUIREMENTS_RECHECK_2026-10-16.md` 对 Role10 既有测试状态与以上一致，并标注 A26 整链待复跑。

## limitations

- 没有读取当前 `TINGLU_STREET_SAVE_ROOT` 的值；没有查询/访问原存档目录；因此不能断言当前启动会重定向或已有旧档被改写。
- 没有在目标 Player 验证 `Environment.GetEnvironmentVariable`、目标平台 `File.Replace`、Settings 读写或真实退出续玩。
- A27 为局部源检查与历史 Role10 服务证据，不是角色12独立 PASS；A26/A30 不在本角色结论内。
- 本次工具没有返回可核实的实际模型/provider/effort。

## contract_requests

- 无公共契约请求。向00提交一项 Bootstrap 范围的隔离保护修复建议。

## next_dependencies

1. 00修复/明确诊断根覆盖策略，并排他运行保存隔离和恢复 Integration 用例。
2. 00针对最终 Windows Player 用独立临时保存根验证新旧路径隔离与续玩；不访问原存档。
3. 12在统一新构建独立判定 A26/A27/A30，并将未验证设备/真人体验保持为 NOT_RUN。

## SAVE_VERSION_GUARD 后续补丁

- 按总控开放的唯一范围，补上非空、无法由本版本 `TryVersion` 解析的 contract 标签保护：`Inspect` 返回 `FutureVersion` 并带明确的 unrecognized-contract 错误。主档/备份都经同一 `Inspect`，因此两边都不会被轮换覆盖。
- 保持空 contract / 缺失字段及旧的可解析版本为 `Corrupt`，未把 JSON 解析失败扩大成 `FutureVersion`，未改 schema/API。
- 新增测试 `UnrecognizedFutureContractInBackupBlocksLoadAndSaveWithoutChangingEitherFile`、`UnrecognizedContractInPrimaryBlocksLoadAndSaveDespiteValidBackup` 和 `MissingContractRemainsCorruptRatherThanFutureVersion`。前两项执行 Load/Save，并逐字节比较主档和备份；此前既有的未来 schema/数字 contract 主备对称测试不重复。
- 当前 Role10 预期 16 个 test cases；这三项 **NOT_RUN**，留由00在排他 Unity 窗口运行。本轮没有编译、没有运行 NUnit/Unity。
- 本次变更文件：`Assets/TingLu/Persistence/StreetSaveService.cs`、`Assets/TingLu/Tests/Upgrade/Role10/StreetSaveServiceTests.cs`、本 QA 与交接文档。Bootstrap 环境变量风险已精确限定并上报00，留其处理。

