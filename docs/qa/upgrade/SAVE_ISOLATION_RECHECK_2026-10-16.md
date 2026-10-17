# 存档隔离与旧编辑器覆盖复核（2026-10-16）

status: PARTIAL
role: SAVE_ISOLATION_AUDIT（角色10/12复核）
model/effort: 请求目标 gpt-6-luna/xhigh；本次工具未提供可核实的实际 provider/model/effort 元数据。
contract_version: StreetContract 1.0.0；存档 schema 1

## 结论

默认启动链具有两层隔离：项目 `ProjectSettings.asset` 的 productName 是 `TingLuStreetUpgrade`（公司名 `TingLu`），与原项目 productName `TingLu` 不同；`StreetConfig.SaveNamespace` 默认又是 `TingLuStreetUpgrade`。未设置诊断覆盖变量时，街区档案由 `Application.persistentDataPath` 注入 `StreetSaveService`，落在 `persistentDataPath/TingLuStreetUpgrade/street-<levelId 的 SHA-256>.json`，备份为同路径 `.bak`。旧 M0 `LocalSaveService` 使用 `TingLu/resume.json`、`progress.json`、`settings.json` 等文件。按当前默认值，它们不共用文件或目录。

发现一个需要总控处理的隔离风险：`StreetEntry.Start()` 无条件读取环境变量 `TINGLU_STREET_SAVE_ROOT`，非空时直接替代 `Application.persistentDataPath`，然后创建 namespace 并交给新存档服务（`Assets/TingLu/Bootstrap/Runtime/StreetEntry.cs:42-47`）。验证脚本将该变量用于临时隔离目录是合理的，但生产入口没有 `UNITY_EDITOR`/`DEVELOPMENT_BUILD` 限定。若发布环境意外继承此变量，新版会写到被指定的任意根目录。当前默认 namespace 与旧版文件名不同，因此本次没有证据证明它会覆盖旧存档；本审计未读取原存档目录、未读取当前变量值，也未执行启动。此处记为 **隔离保护缺口（需总控修复/复核）**，不能表述为已发生旧档覆盖。

新版地图不落在旧编辑器可写的关卡 JSON 中。`StreetLevelFactory.Create()` 在运行时生成 `street.crossroads.v1`，`StreetEntry` 直接使用该工厂（`Assets/TingLu/Levels/StreetLevel.cs:7-50`、`Assets/TingLu/Bootstrap/Runtime/StreetEntry.cs:47`）。旧 `LevelEditorWindow` 只加载/保存 `LevelDocument`；`LevelCodec.Decode` 校验其自身 `version` 和字段，保存前也先解码已存在目标文件（`Assets/TingLu/Levels/LevelDocument.cs:84-152`、`Assets/TingLu/Levels/Editor/LevelEditorWindow.cs:432-463`）。当前没有新版地图 `.json` 被旧编辑器无声截断的实际对象。不要为并不存在的新格式 JSON 文件制造风险结论；若未来把地图迁为文件格式，应使用独立格式/扩展名并加旧编辑器拒绝用例。

## 静态核对结果

- **namespace 与 level 隔离：符合当前契约。** `StreetSaveService` 将传入根与通过单段字符校验的 namespace 组合成工作目录；levelId 不直接拼入路径，而是哈希后用作文件名（`Assets/TingLu/Persistence/StreetSaveService.cs:115-120, 431-434, 450-460`）。默认 productName 与 namespace 均和原项目不同。
- **未来版本：代码有主档及备份双重拒写。** `Load`、`Save` 都先检查主档和 `.bak`；任一识别为未来 schema/contract 时返回 `FutureVersion`，不回退读取较旧一份，也不替换任一文件（同文件 `:123-144, 196-222`）。当前既有 Role10 测试含主档/备份、schema/contract 四个组合。
- **坏档、备份与原子替换：源码路径保守。** 有效主档损坏时可回退有效备份；单独损坏的主档/备份不会被新档静默覆盖；有效主档写入先落同目录 `.tmp` 并 Flush，再 `File.Replace`；损坏主档可隔离到唯一 `.corrupt[.N]` 路径，保留有效备份。替换失败会清临时文件并报告 `IoFailure`。Role10 测试源码覆盖这些分支。
- **恢复数据：符合持久化边界。** DTO 显式保存当前与检查点玩家/交通状态、累计探索几何、碰撞计数、generation、已激活检查点和完成状态（`Assets/TingLu/Persistence/StreetSaveService.cs:20-36, 280-303`）。读回时保留探索面域/轮廓及观察时间，但将面和物体 `CurrentUntil` 设为 0；`StreetSession` 续玩使用完整探索，碰撞恢复将所有记忆标旧、清理感知并保留累计碰撞（`Assets/TingLu/Persistence/StreetSaveService.cs:395-428`、`Assets/TingLu/Bootstrap/Integration/StreetSession.cs:75-89, 128-144, 171-176`、`Assets/TingLu/Memory/StreetMemory.cs:122-148`）。活动声源不在存档 DTO 中，且 Active 不能作为几何记忆写入/读入（`StreetSaveService.cs:372-390, 410-426`）。
- **旧编辑器：没有对新版工厂地图的写路径。** 现有工具读写 `LevelDocument` 的 `.json` 文件；它无法把工厂生成的 `StreetLevel` 转成 `LevelDocument`。旧编辑器仍可编辑它认识的旧 JSON，但当前不会覆盖 `StreetLevelFactory` 的代码数据。

## 自动化证据与边界

本轮未启动 Unity、未构建、未访问原存档、未做任何运行时写入。读取到已有证据 `output/unity-verification/20261015-232743-a02fe7f5/results.json` 与 `editmode.xml`：当时 `TingLu.Tests.Upgrade` 共 113/113 通过，其中 Role10 的 13/13 通过；结果记录 `editorShutdown=FORCED_AFTER_COMPLETED_STAGE`，即测试结果已写出但 Unity 退出阶段曾被强制结束。该证据是既有历史运行，不是本次复测，也不能证明当前 Player 或 A26/A30 整链通过。设备、真人声音体验、A27 独立整项结论均未在本任务执行。

`docs/handoffs/UPGRADE_10.md` 列出的测试名/边界和本次静态源码检查相符；`docs/qa/upgrade/REQUIREMENTS_RECHECK_2026-10-16.md` 也将存档单元测试记为 13 个通过，同时明确 A26 端到端仍待复跑/独立复现。

## 阻断/下一步

1. 00检查并收窄 `TINGLU_STREET_SAVE_ROOT`：生产 Player 不应无条件接受任意继承环境变量。建议保留测试注入能力但将正式 Player 默认固定为 `Application.persistentDataPath`，或提供受限的显式诊断开关；由00负责代码所有权和集成验证。
2. 修复后在独立临时根复测启动/继续/保存，确认最终构建不因外部环境变量写入原产品目录；不要读取或写入 `D:\听路` 的存档做此验证。
3. 00在排他的 Unity 窗口运行 Role10 与恢复相关 Integration/Acceptance 用例；12在统一构建独立判定 A26/A27/A30。保留 `A27 = NOT_RUN/未独立整项判定`，不要把此局部审计升级成整项 PASS。

## 变更

本复核只新增/更新本 QA 报告与对应交接；没有修改生产源码、Unity 场景、配置、原存档或测试文件。

## 20261016 SAVE_VERSION_GUARD 后续复核

总控随后开放 `StreetSaveService.cs` 与 Role10 测试范围。发现并修正了审计原先未覆盖的版本标签情形：`TryVersion` 不能解析非空合同字符串（如 `2.0.0-beta`）时，旧逻辑会继续把它当作普通损坏档；若它位于一侧而另一侧是当前有效档，保存轮换可能把未知文件替换掉。现在 `Inspect` 在 checksum/payload 前把非空且不可解析的 contract 归类为 `FutureVersion`，并通过 `Error` 明确返回 `Unrecognized contract version; refusing to load or overwrite this save`。空 contract 仍归为 `Corrupt`；可解析的旧 `0.9.0` 仍按原逻辑归为 `Corrupt`；任意无法解析 JSON 仍是解析失败 `Corrupt`。未来主档/备份的对称路径由原实现覆盖，本次没有重复增加镜像测试。

新增 Role10 测试源码验证：有效当前主档 + `2.0.0-beta` 备份、未知 contract 主档 + 有效当前备份时，`Load` 与 `Save` 均返回 `FutureVersion`，并逐字节核对主/备份未变化；另验证空 contract 仍返回 `Corrupt` 且不覆写。测试名为 `UnrecognizedFutureContractInBackupBlocksLoadAndSaveWithoutChangingEitherFile`、`UnrecognizedContractInPrimaryBlocksLoadAndSaveDespiteValidBackup`、`MissingContractRemainsCorruptRatherThanFutureVersion`。

**本次补丁状态：NOT_RUN。** 按总控边界未运行 Unity、编译、NUnit 或其他测试。此前 2026-10-15 的 Role10 13/13 通过证据对应补丁前代码，不能当作这三项新增测试已通过。应由总控在 Unity 排他窗口运行 Role10；源和测试交接后冻结等待统一验证。

后续修改文件：

- `Assets/TingLu/Persistence/StreetSaveService.cs`
- `Assets/TingLu/Tests/Upgrade/Role10/StreetSaveServiceTests.cs`
- `docs/qa/upgrade/SAVE_ISOLATION_RECHECK_2026-10-16.md`
- `docs/handoffs/UPGRADE_SAVE_ISOLATION_AUDIT.md`

没有改动诊断根环境变量风险所属的 Bootstrap 文件，也没有访问任何原存档。
