# 验证工具独立复核（2026-10-16）

status: PARTIAL；源码审查完成，当前 Unity/Player/打包链待总控复测
scope: `Verify-Unity.ps1`、`Verify-StreetPlayer.ps1`、`Package-StreetPlayer.ps1`、`Save-UpgradeHandoff.py` 与街区截图/启动诊断探针
model/effort: 请求目标 gpt-6-luna/xhigh；当前执行器未提供可核实的实际 provider/model/effort 元数据
execution: 本复核只读脚本、探针和既有证据；未启动 Unity，未执行这些验证/打包/快照工具，也未删除或移动文件

## 结论

多数失败传播和隔离设计有明确的防假通过条件：Unity 验证超时会杀死子进程树并写 FAIL；结果已完成后 Unity 退出挂起会留下 `FORCED_AFTER_COMPLETED_STAGE` 和退出码，随后仍验证日志、XML、源码稳定性；街区 Player 运行失败/超时会进入 FAIL 报告；打包采用新临时目录、完整目录清单和逐文件 SHA-256 比较，且不覆盖现有目标。

发现一条**历史街区 Player false PASS 证据**：`output/street-verification/20261015-221422-507c4491/results.json` 与 `20261015-221736-17d34452/results.json` 均记录顶层 `status=PASS`、`speechBackend=FAIL`，Player 日志也记录 `Street audio failed: speech_...`。这不是当前复核到的汇总脚本逻辑：当前 `Verify-StreetPlayer.ps1` 在 `speechBackend` 非 `STARTED` 时抛错并将最终状态记为 FAIL（第51-59行）。历史报告没有记录 verifier 的哈希，且缺少当前脚本新增的 `executableSha256`/`verifierSha256` 字段，不能归因于当前文件版本；这两条旧 PASS 不得用作语音通过证据，需在冻结当前脚本后重跑 Player。

总控在本复核期间已经补强了 Unity 测试完整通过门（包括 `passed == total`、`skipped == 0`、`inconclusive == 0`），并为 Player 加入 EXE/verifier SHA-256、运行前后 EXE 哈希一致性和 PNG 签名/哈希。源码修复目前没有本轮运行证据。PNG 签名检查只检查文件头，不是完整 PNG 解码；`visualReview` 仍为 `PENDING_IMAGE_INSPECTION`，最终仍需打开图片确认画面。

## 按工具复核

### `Verify-Unity.ps1`

- **失败传播与超时：源码路径合理。** `$ErrorActionPreference='Stop'`；超时会 `Kill($true)` 并抛错，由外层 catch 写 `status=FAIL`、`reason` 后重新抛出（第9、117、139-143行）。Unity 自然以非零码退出也会失败（第119-120行）。找不到编辑器或项目已经打开时记录 `NOT_RUN` 并返回退出码2（第33-37、48-52行）。
- **完成后被强杀：不会单凭文件存在报通过。** 轮询等待 shutdown 标记和 stage 结果后才开始15秒宽限；强杀只杀本脚本子进程，并在后续继续走统一校验（第99-120行）。当前 EditMode/PlayMode 的结果门要求 XML 存在、根节点 `Passed`、非空，且 `passed == total`、失败/跳过/不确定数均为0（第125-130行）。构建门仍有一处待补：轮询中的强杀条件同时要求成功日志标记与 EXE（第103行），但自然退出后的最终门只查 EXE 是否存在（第132行）。建议最终门也要求 `unity.log` 包含 `TingLu Windows build succeeded:`；`WindowsDemoBuild.Build()` 仅在 `BuildResult.Succeeded` 后写此标记。总控已确认将在当前 EditMode 运行结束后补此门，本轮不改工具。
- **sourceFingerprint：同一环境下对当前清单确定，但覆盖范围有限。** 它将 `Assets`、`Packages`、`ProjectSettings` 下可枚举文件的相对路径和 SHA-256 排序、压缩成 JSON 后哈希；运行前后比较该 JSON，检测这些目录中途改变（第17-23、57-64、133-135行）。`Sort-Object` 使用当前文化排序，因此同一机器/文化下可重复，但不同语言区域下不保证相同路径顺序；若跨环境比较 fingerprint，应固定 ordinal 排序并固定序列化格式。它不包含工具自身哈希、独立根 `Tests`、`docs`、`tools`，因此不能证明具体 verifier 版本，也不能证明这些范围未改。`Get-ChildItem` 未加 `-Force`，隐藏文件不会进入清单；若这些目录中存在会影响导入/构建的隐藏文件，fingerprint 将漏掉它们。建议为清单遍历加 `-Force`，并在结果 JSON 加 verifier SHA-256。已存历史 Unity 结果（包括113/113的 `20261015-232743-a02fe7f5`）没有 `sourceFingerprint`、`sourceManifest` 或 `sourceUnchangedDuringStage` 字段；它们早于当前实现，不能当作当前指纹机制的运行验证。
- **隔离 PlayMode 存档：源码有单次运行目录注入。** PlayMode 子进程环境将 `TINGLU_STREET_SAVE_ROOT` 指向本轮 `output/unity-verification/<id>/isolated-saves`（第76-82行）。本次未运行验证，历史目录中没有当前脚本对应的隔离目录可证实新运行行为。

### `Verify-StreetPlayer.ps1` 与街区探针

- **失败门和取证链：当前源码已较历史记录加强。** 输入 EXE 先取绝对路径，报告记录 EXE 与 verifier SHA-256；在运行前后比较 EXE 哈希；进程必须在超时内以0退出；探针必须报告 `PASS`、活动场景为 `StreetCrossroads` 且 `running=true`；两张截图必须是文件，当前还检查 PNG 签名并记录各自 SHA-256；Player 日志中的指定 shader/runtime 错误会失败，语音必须检测到 `TingLu speech started;` 才记 PASS（第14-18、25-38、41-59行）。非零退出和超时会抛错，并在 finally 写 `results.json`（第30-34、56-60行）。
- **历史证据与版本边界：需重跑。** 两条旧报告显示 PASS 与语音 FAIL 并存；对应 Player 日志有语音后端失败，截图文件存在且分别为 22,459/33,567 字节。当前汇总脚本有语音门，但这些旧结果没有 verifier hash，不能证明由当前版本生成。其 `visualReview=PENDING_IMAGE_INSPECTION` 也表示机器状态 PASS 不等于图片已验收。旧街区探针 JSON 的自身 `status=PASS` 只覆盖场景/截图/会话字段，语音另列 `speechBackend=FAIL`；应以汇总 `results.json` 的最终状态和日志共同判读，不能把探针的 PASS 单独解释为音频通过。
- **截图真实性边界：仍需人工看图。** 当前检查至少拒绝缺失文件、空/过短文件和非 PNG 签名，并保存哈希，但没有解码 PNG、检查末尾块/CRC、核对实际像素尺寸或人工查看。最小下一步是使用图片查看器检查两张已哈希图；若要程序门，应增加完整 PNG 解码及期望尺寸核验。
- **隔离存档：参数链清楚，历史有隔离文件。** 汇总脚本把 `output/street-verification/<id>/isolated-saves` 同时放入环境变量和 `-tinglu-street-save-root` 参数（第25-28行）。`StreetScreenshotProbe.ConfigureIsolatedSaveRoot()` 要求绝对路径，拒绝与 `Application.persistentDataPath` 相同或位于其内部，并在场景启动前设置环境变量（探针第27-50行）。旧 run `20261015-221736-17d34452` 确有该隔离目录下主档和 `.bak`。这证明当时写入了隔离根，不证明新版脚本本轮运行结果；本次没有读取存档内容。当前检查脚本还在 `try` 之前解析 EXE 和读取哈希，坏路径/无法读取时可能只留下空证据目录而无 `results.json`（第12-19行）。最小改进是先初始化 NOT_RUN 报告，再将解析/哈希也放进 try/finally。

### `Package-StreetPlayer.ps1`

- **静态检查通过：打包复制链完整。** 目标被限制在项目 `output` 下的新目录；源和目标不能相同或互相包含；沿途重解析点被拒绝；目标已存在则拒绝覆盖（第296-315行）。清单遍历拒绝源中的重解析点/设备项，并排序记录路径、长度、SHA-256（第100-147行）。对源 Player 检查 EXE、`UnityPlayer.dll`、`TingLu_Data/globalgamemanagers` 和完整 Mono 或 IL2CPP 关键运行时（第191-253行）。暂存复制后逐文件比较、重读源以检查复制过程中变化，再验证最终包内构建文件与源清单一致，最后原子目录移动到新目标（第335-353、399-418行）。失败保留临时目录并抛错，不静默发布。
- **能力边界明确。** `package-manifest.json` 明确声明只证明目录复制与 SHA-256，不证明 Unity 构建、运行画面或玩法；体验指南也说明声音及真人听辨未完成（第355-397行）。该脚本不启动 Player、不生成截图，也不证明源 EXE 是刚由本轮 Unity build 产生。应由独立 `BuildWindows` 与 `Verify-StreetPlayer` 结果按 EXE 路径/哈希关联。当前可见的 `.package-script-check-*` 是小型输出目录，不能替代完整真实 build 打包和运行验收；本轮未执行打包工具。

### `Save-UpgradeHandoff.py`

- **快照与归档校验：源码路径合理。** 将项目内指定源码、工具、文档和验证输出形成文件清单，ZIP 后检查 CRC、逐文件 SHA-256，并在结束时重算源文件防止快照期间变化（第37-52、97-112行）。会将升级基线里的原项目源码和存档逐文件 SHA-256 比对；旧构建则只比较文件长度与写入时间（第67-85行）。
- **不要混淆两个状态。** summary 的 `status='SAVED_AND_VERIFIED'` 是归档内容及快照稳定性状态；原项目保护结果单列为 `originalBaselineProtectionPassed`（第113-125行）。如果该布尔值为 false，脚本仍会写出 `SAVED_AND_VERIFIED`。消费者必须同时检查该布尔值，不能只凭总 status 宣称原源码/存档/旧构建均保持不变。建议把该字段单独命名为归档状态，或在保护不通过时将总状态变为非通过。
- **历史快照：当时保护位为 true，但不能当作当前重测。** `output/upgrade-handoff/20261015-164432/summary.json` 记录 ZIP SHA/内容校验、`snapshotStable=true`、`originalBaselineProtectionPassed=true`；相应复核记录中源码246项、存档7项、旧 build 元数据464项都未报告差异。旧 build 的保护是长度+mtime而非文件内容哈希，强于不存在检查但弱于字节级证明。此为10月15日已有快照，不是本轮执行，也不是当前开发改动后的保护复核。

## 既有运行记录与判读

| 记录 | 现有结果 | 判读 |
| --- | --- | --- |
| Unity EditMode `20261015-150041-b5472da8` | 强杀后 XML 为 26 项、22 pass、4 fail；最终 FAIL | 失败正确传播，没有因 XML 文件存在而假 PASS。 |
| Unity EditMode `20261015-171729-08ca496d` | 强杀后 XML 为108项、101 pass、7 fail；最终 FAIL | 同上。 |
| Unity EditMode `20261015-232743-a02fe7f5` | `FORCED_AFTER_COMPLETED_STAGE`、exitCode=-1；XML 113/113 Passed；旧结果记 PASS | 强杀状态留档且 XML 完整通过。这个历史结果缺当前 source fingerprint 字段，不能认证当前源版本。 |
| Unity EditMode `20261015-145448-79fd6c2c` | 超时300秒，最终 FAIL | 超时传播为失败；结果无 exitCode 是因为超时在读取自然退出码之前抛错。 |
| Street Player `20261015-221422-507c4491`、`20261015-221736-17d34452` | 顶层 PASS，speechBackend FAIL，Player log 有语音失败 | 历史 false PASS；拒绝用作语音通过证据，冻结当前工具后重跑。 |
| 升级快照 `20261015-164432` | ZIP 内容和快照稳定均 true，原项目保护布尔值 true | 历史快照证据；不是本轮新代码的运行验收。 |

## 总控待办与限制

1. 总控已在当前 EditMode 结束后给 `BuildWindows` 最终 PASS 门补 success marker 条件；源码已补，等待本轮构建实际验证。
2. 用同一份冻结 build 运行当前 `Verify-StreetPlayer.ps1`，报告 `executableSha256` 与 build 输出可核对；确认两个截图哈希、完整 PNG 解码、人工画面检查及 Player log。语音 FAIL 必须保持顶层 FAIL。
3. 重跑 Save-UpgradeHandoff 快照，核对 `originalBaselineProtectionPassed`；仅将 `SAVED_AND_VERIFIED`解释为 ZIP/快照通过。不要把本复核的历史布尔值外推为本轮 Unity 运行后仍未改动原项目。
4. 硬化 source manifest：纳入隐藏文件、记录 Unity verifier SHA-256；以当前 Unity 运行报告确认 source fingerprint 与 `sourceUnchangedDuringStage` 实际产生且稳定。

本报告的源码结论与既有运行记录分列。除列出的历史报告外，本复核未执行 Unity、Player、打包或快照脚本，也没有解码或人工审阅图片。

