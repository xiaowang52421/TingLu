# 街区 Player 完整目录打包工具交接

status: READY_FOR_REVIEW

role/model/effort: PACKAGE_STREET_PLAYER；请求配置 gpt-6-luna/xhigh。当前工具未独立提供实际 provider、运行模型或推理强度元数据，不能核实请求是否与实际运行一致。

contract_version: StreetContract 1.0.0

## 已完成

- 新增 PowerShell 7 工具 `tools/upgrade/Package-StreetPlayer.ps1`。接受 `SourceBuildDirectory` 与 `DestinationDirectory`；目标经正规化后必须位于本项目 `output` 子目录且尚不存在。
- 在源目录递归检查重解析点，拒绝符号链接、联接及设备项；也检查源路径、目标路径、`output` 及已有父路径上的重解析点。源和目标不能相同或互相包含。
- 确认 `TingLu.exe`、`UnityPlayer.dll`、`TingLu_Data/globalgamemanagers`、非空 `TingLu_Data`，并确认存在完整 Mono 或 IL2CPP 运行时。崩溃处理器及另一后端的组件按该构建实际清单作为可选项记录，不强制添加不存在的文件。
- 复制源目录全部文件和目录，包括隐藏项与空目录。逐个计算 SHA-256 和文件长度，比较源与完整临时副本；复制结束后重新检查源目录，避免把复制期间改变的输入当作稳定构建。只有全部核对成功才将暂存目录移动到目标目录。
- 输出 `package-manifest.json`，包含源文件清单、长度、SHA-256、构建组件、总字节数及验证范围；另生成 UTF-8 `开始体验.txt`，用面向玩家的中文说明 WASD、鼠标转身、右键敲杖、被动听觉、黑暗未知区、Esc/G、菜单操作、独立存档和环境音/真人听辨限制。
- 已有目标始终拒绝覆盖；脚本不删除源目录、旧包或失败时的暂存目录。目标父目录只会在项目 `output` 范围内创建。

## 修改文件

- `tools/upgrade/Package-StreetPlayer.ps1`
- `docs/handoffs/UPGRADE_PACKAGE_STREET_PLAYER.md`

没有修改 Verify 脚本、游戏生产代码、共享契约、场景、配置或其他角色文件。

## 验证与证据

- PowerShell 7.6.5 AST 语法解析：PASS。
- 合成 Windows Player 夹具复制：PASS。源含可执行文件、UnityPlayer、TingLu_Data/globalgamemanagers、Managed 与 MonoBleedingEdge 运行时；清单记录 6 个源文件、28 字节。
- 对输出目录内每个源构建文件独立重算 SHA-256：PASS；清单和中文说明字段核对：PASS。
- 对已存在目标再次运行：按预期拒绝，原夹具包 TingLu.exe 哈希保持不变：PASS。
- 向源夹具加入联接后重试：按预期以重解析点拒绝，且未创建目标包：PASS。
- 合成夹具包证据：`output/.package-script-check-3ffde21915414e3290d6eb54eea5ad8d/`；合成源夹具：`%TEMP%\TingLuPackageFixture-3ffde21915414e3290d6eb54eea5ad8d\Windows/`。二者均为测试数据，绝非新版构建或可试玩交付。删除测试夹具的命令被执行工具拒绝，因此当前仍留在上述明确命名位置；未触碰任何旧包或原项目文件。
- Unity、真实构建、实际画面、音频 Started 与真人听辨：本角色未运行；本轮等待总控提供已完成的新构建目录。

## 总控接入

总控完成新版 Windows 构建并核实其实际输出目录后，从项目根目录运行：

```powershell
pwsh -NoProfile -File tools/upgrade/Package-StreetPlayer.ps1 `
  -SourceBuildDirectory '<本次新版构建的完整Windows目录>' `
  -DestinationDirectory 'output/听路街区试玩_<版本或时间戳>'
```

脚本只证明目录结构检查、逐文件复制和 SHA-256 一致性。总控仍需独立核实输入构建确属本轮新版、运行入口与 Player 验证结果，并记录实际视觉/音频/真人体验缺口。不要把上述合成夹具当作真实构建，也不要将旧 `20261015-221003-16aa10b1` 构建输入本工具冒充新版。

limitations: 当前没有新构建输入，因此真实试玩包未生成。占位环境音与真人听辨尚未验证。实际 provider/model/effort 无工具元数据可核实。

contract_requests: none。

next_dependencies: 总控完成新版本 BuildWindows 并提供此次构建的 Windows 完整目录；随后由总控运行脚本、检查 `package-manifest.json` 和 `开始体验.txt`，并完成可执行文件的独立启动验证。
