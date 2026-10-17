# 验证工具路径可移植性修整

status: READY_FOR_REVIEW
contract_version: 0.2.0

## 完成内容

- `Verify-Unity.ps1` 从 `ProjectSettings/ProjectVersion.txt` 读取项目版本，自动查找版本匹配的 Unity Editor。候选来源包括 `Unity`/`Unity.exe` PATH 命令、当前运行的 Editor 进程、Unity Hub 的 `editors.json`，以及系统 `ProgramFiles`、`ProgramFiles(x86)` 和 `ProgramW6432` 环境目录。
- 保留 `-UnityPath` 显式入口，但现在也验证其为与项目版本匹配的 `Editor/Unity.exe`，拒绝 Unity Hub 目录里的同名启动器和其他版本。未发现 Editor 时继续记录 `NOT_RUN`，不会尝试从机器绝对路径猜安装位置。
- `Verify-Player.ps1` 默认超时改为 180 秒，并将超时、可见/隐藏运行方式、截图证据和布局验证状态写进报告。隐藏窗口会明确记录 `NOT_VERIFIED_HIDDEN_WINDOW`；可见窗口的截图也不自动代表布局已人工验收。
- 只有日志包含 `TingLu speech started; backend=...; frames=<正数>` 才会把 `speechVerified` 设为 true。没有诊断时启动检查仍可报告 PASS，但语音验证保持未确认。
- 新增可独立运行的发现/证据规则与临时目录测试，不依赖当前机器安装位置。

## 修改文件

- `tools/Verify-Unity.ps1`：接入版本发现和 Editor 路径校验。
- `tools/UnityEditorDiscovery.ps1`：可移植的 Unity 版本解析、Hub 数据读取、候选筛选。
- `tools/Verify-Player.ps1`：启动证据和语音/布局状态记录，默认超时 180 秒。
- `tools/PlayerEvidenceRules.ps1`：成功语音日志与布局状态判定。
- `Tests/Tools/PortabilityChecks.ps1`：使用临时假安装目录验证 Unity Hub、版本选择、启动器拒绝、语音日志与隐藏窗口状态。
- `docs/handoffs/PORTABILITY_2026-10-14.md`：本交接。

## 验证

- `Tests/Tools/PortabilityChecks.ps1` 通过：项目版本解析、Hub `editors.json` 查找、匹配版本选择、拒绝 Hub 启动器、只认可成功 started 语音日志、隐藏窗口不宣称布局通过、工具无机器用户路径依赖。
- PowerShell Parser 对以上两个 Verify 脚本、两个共享规则脚本及测试脚本均未报告语法错误。
- 未运行 `Verify-Unity.ps1` 或 `Verify-Player.ps1`，也未启动 Unity、Player 或编辑器进程，以免与总控的运行/搬迁验证冲突。

contract_requests: none
