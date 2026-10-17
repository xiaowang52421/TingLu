# 原项目与原存档保护复核交接

status: READY_FOR_REVIEW
role/model/effort: 原件保护复核；请求目标 gpt-6-luna/xhigh；工具未提供可独立核实的实际 provider/model/effort 元数据。
contract_version: 不适用（保护基线复核）

## completed

- 按清单实际字段读取 `output/upgrade-baseline/baseline.json`、`source-sha256.json`、`protected-saves.json`、`protected-builds.json`。清单计数分别为 246 源文件、7 存档、464 构建文件；汇总计数一致。
- 逐项对 `D:\听路` 下 246 个源文件重算 SHA-256，全部匹配；0 缺失、0 跳过、0 reparse point。
- 对清单中的 7 个原存档文件逐项重算 SHA-256，全部匹配；0 缺失、0 跳过、0 reparse point。没有输出存档文件名、哈希或内容。
- 对原试玩包清单中的 464 个文件逐项比较长度与 `LastWriteTimeUtc` ticks，全部匹配；0 缺失、0 跳过、0 reparse point。此清单只提供元数据，因此未将其表述为内容哈希验证。
- 整体结果 PASS，精确计数、异常列表、检查时点与清单引用见 `output/recheck-original-protection-final.json`。

## changed_files

- `output/recheck-original-protection-final.json`
- `docs/handoffs/UPGRADE_ORIGINAL_PROTECTION_FINAL.md`

`D:\听路`、原存档、原试玩构建均只读；未启动 Unity、未构建或修改任何原件。

## tests

- 本项为只读 SHA-256/文件元数据对比，不启动 Unity。
- 检查时间：`2026-10-15T17:24:25.0310655Z`（北京时间 2026-10-16 01:24:25）。
- 基线清单：`output/upgrade-baseline/baseline.json`；源 SHA 清单：`output/upgrade-baseline/source-sha256.json`；存档 SHA 清单：`output/upgrade-baseline/protected-saves.json`；构建元数据清单：`output/upgrade-baseline/protected-builds.json`。
- 最终逐文件结果：`output/recheck-original-protection-final.json`。存档文件名及存档内容未写入 JSON 或本交接。

## limitations

- 原试玩构建仅复核清单记录的文件长度与 UTC 修改时间；未提供内容 SHA，因此不声称构建内容逐字节一致。
- 原项目源码与原存档按基线 SHA-256 核对一致；该结论只对应检查时点。
- 工具没有独立提供实际模型/provider/effort 元数据。

## contract_requests

- 无。

## next_dependencies

- 无需后续操作；如之后有写入原项目、存档或原试玩包，应重新生成保护清单并复核。
