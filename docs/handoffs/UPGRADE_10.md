# UPGRADE_10 存档与检查点交接

status: READY_FOR_UNITY
role/model/effort: role10；目标 gpt-6-luna/xhigh；当前执行环境未提供可核实的实际 provider、model 或 effort 元数据，故实际值不可核实。
contract_version: StreetContract 1.0.0；save schema 1

## completed

- 补齐 `StreetSaveService` 编译语法风险；遇到未来 schema/contract 的主档或备份时，读写均返回 `FutureVersion`，不加载较旧另一份存档或覆盖任何一份。
- 当主档缺失、仅有损坏备份时拒绝创建新主档；只有有效主档/备份可恢复或轮换时才继续写入。损坏主档且备份有效时隔离保留坏主档，并保留有效备份。
- 对旧 schema/contract 返回 `Corrupt` 并给出 unsupported older contract 诊断；未实现跨 schema 迁移。
- 增加显式 DTO round-trip、主备恢复、checksum、未来/旧版保护、命名空间和 level 隔离、损坏档保护、Active 拒绝及记忆过期测试。
- 为生产脚本和新增测试脚本添加 Unity `.meta`。

## changed_files

- `Assets/TingLu/Persistence/StreetSaveService.cs`
- `Assets/TingLu/Persistence/StreetSaveService.cs.meta`
- `Assets/TingLu/Tests/Upgrade/Role10/StreetSaveServiceTests.cs`
- `Assets/TingLu/Tests/Upgrade/Role10/StreetSaveServiceTests.cs.meta`
- `docs/handoffs/UPGRADE_10.md`

未改公共契约、任何 asmdef、Config、Bootstrap、旧 `LocalSaveService` 或 Role10 asmdef。

## integration

- 类型/API 不变：`TingLu.Persistence.StreetSaveService(string persistentRoot, string saveNamespace)`、`Save(StreetSaveData)`、`Load(string levelId)`。
- 主档与 `.bak` 均检查未来版本；读取未来备份时不会返回旧主档。`CurrentUntil` 读取为 0，活动声源不进入 DTO，活动几何记忆不能加载。
- 00 仍负责注入隔离根/namespace及检查点恢复事务。本角色未触碰真实持久化目录。

## tests

- 已新增 NUnit 测试源码；所有文件测试使用 `Path.GetTempPath()` 下随机专属目录，fixture 不读取 `Application.persistentDataPath`。
- 覆盖当前与检查点玩家/交通往返、合法记忆几何和 `CurrentUntil=0`、损坏主档备份恢复、损坏主档隔离与备份保留、损坏 checksum 拒绝覆盖、仅有损坏备份时拒绝新档、原子替换失败后旧主档可读且临时文件清理、未来 schema/contract 位于主档或备份时拒绝读写、旧 schema 诊断、namespace/level 隔离及 Active 记忆拒绝。
- NOT_RUN：本角色未启动 Unity、未编译、未运行 NUnit。需由00在排他 Unity 验证窗口运行 `TingLu.Tests.Upgrade.Role10`。

## limitations

- `File.Replace` 和同目录原子替换行为尚未在目标 Unity/Windows runtime 实测；测试源码包含主备轮换路径，但仍需实际执行。
- 旧 schema 目前作为不兼容数据报告，不做迁移。05/03/09真实实现的恢复联调未进行；不代表 A23/A24/A25/A26/A30 通过。

## contract_requests

- 无。

## next_dependencies

- 00排他导入并编译当前源码，运行 Role10 定向 NUnit；失败先保留证据并由本范围修复。
- 在目标 Unity/Windows runtime 核验连续保存的 `.bak` 轮换、`File.Replace` 失败时原主档保持可读，以及 FutureVersion 文件逐字节不变。
- 后续与05/03/09及00接线联调检查点恢复；A项整链由12独立判定。
