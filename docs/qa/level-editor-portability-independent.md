# Unity 发现工具独立复核

2026-10-14。只读审查 `UnityEditorDiscovery.ps1`、`Verify-Unity.ps1`、`Tests/Tools/PortabilityChecks.ps1`；未启动 Unity，未修改共享验证脚本。

执行 `pwsh -NoProfile -File Tests/Tools/PortabilityChecks.ps1`，进程退出码 0，6 项 PASS：项目版本及 Editor 路径、Hub JSON、版本候选选择、实际 started 语音证据规则、隐藏窗口布局保持未验证、工具相对工程路径。

当前 PortabilityChecks 的 Editor 文件是文本 fixture，并仅注入 PE metadata 读取函数来检验版本逻辑，因此不能证明真正 PE 产品版本读取、自定义安装目录或注册表/环境变量发现；需结合总控独立真实文件验证。

审查发现 ProductVersion 条件曾重复，首段提前返回 exact 版本，遮蔽了后段 cN 规范化比较。已通知总控并确认重复块删除。修复后再次读取本机真实文件 `2022.3.62f1c1_b0109b07edb8` 并以 `2022.3.62f1` 查询，返回 True；再次运行 PortabilityChecks，6 项 PASS。

环境变量和卸载注册表候选已接入。安装根通过二进制产品版本识别，不再依赖安装根文件夹名称；内部 `Editor/Unity.exe` 结构仍用于排除 Unity Hub 启动器。最终实现不再使用缺失 metadata 时的目录版本名回退，不能仅以目录名冒充有效 Editor 版本。
