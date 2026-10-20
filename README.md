# 听路街区升级

本目录是新版独立工程，原 `D:\听路` 保持只读。当前开发、运行入口和验证证据以 [街区状态](docs/upgrade/UPGRADE_STATUS.md)、[街区交接](docs/upgrade/RESUME.md) 为准。

Git 仓库包含 Unity 工程源码、资源、项目配置、工具和交付文档。克隆后用 Unity **2022.3.62f1c1** 打开仓库根目录，等待依赖解析，再打开下方的 `StreetCrossroads.unity` 场景。`Library` 等缓存和 `output` 下的试玩包、验证输出、交接快照保留在开发机，不纳入 Git；文档中的 `output/...` 路径是本地证据路径，克隆后需通过验证/构建脚本重新生成。Git 属性保留文件原始字节，方便复核已验证源码指纹。

2026-10-20 当前试玩：[从 GitHub Release 下载 Windows 试玩包与动态验证证据](https://github.com/xiaowang52421/TingLu/releases/tag/street-2026.10.20-preview.1)。也可直接下载 [Windows ZIP](https://github.com/xiaowang52421/TingLu/releases/download/street-2026.10.20-preview.1/TingLu-Street-Player-2026-10-20-Windows.zip) 和 [SHA-256 校验文件](https://github.com/xiaowang52421/TingLu/releases/download/street-2026.10.20-preview.1/TingLu-Street-2026-10-20.sha256.txt)。完整解压后运行 `TingLu.exe`，双击 `查看碰撞演示.cmd` 可重播自动演示；无需安装 Unity，请保留整个目录。Release 页面自动生成的 Source code 压缩包是源码。

本次提供中文移动/静止行人、汽车、自行车和电动车配置，所有真实玩家碰撞播放深红边缘光晕；只有碰撞区间实际运动中的车辆恢复检查点，普通实体保留阻挡且不扣值。验证包括完整 EditMode 267/267、专项 EditMode 247/247、PlayMode 2/2、Windows 构建，以及 13 类碰撞在 3 种窗口尺寸下的 39 组 Player 记录和连续画面检查。详细规则、参数和证据边界见 [本次修复说明](docs/upgrade/20261020_对象移动与碰撞反馈.md)。本版为 Pre-release：真人键鼠、设备音频和真人无障碍尚未验收；本机仍有中文语音合成不可用警告。历史版本继续保留在 Release 列表。

新版首场景为 `Assets/TingLu/Scenes/Levels/StreetCrossroads.unity`。WASD 或方向键移动，鼠标水平移动转身，右键按下一次敲杖；聆听被动生效，Esc 暂停，G 重听目标。镜头方向固定；空格与 Shift 不再触发旧玩法。新版存档使用独立产品名与命名空间。

## 新版街区关卡编辑器（v2）

用 Unity **2022.3.62f1c1** 打开本工程，选择 **TingLu → 关卡编辑器**（也可选“新版关卡编辑器”）。Windows 可双击根目录 [打开新版关卡编辑器.cmd](打开新版关卡编辑器.cmd)；工程已经打开时，按弹窗提示从 Unity 菜单进入。

从十字路口模板开始，在中文画布和属性表单中编辑道路、盲道、斑马线、坡道、建筑、临时障碍、车辆与行人路线、交通灯、出生点、检查点和终点。保存草稿、检查、隔离试玩、导出 JSON 和可玩 Unity 场景均由界面完成，不需要写代码或手改配置。操作见 [新版使用指南](docs/editor-v2/使用指南.md)，当前验证范围见 [新版验证结果](docs/editor-v2/验证结果.json)。旧版《第一步》编辑器移到 **TingLu → 旧版工具 → 第一步关卡编辑器**；旧 JSON 与新版 schema 2 分别处理。

开发验证：`pwsh -NoProfile -File tools/Verify-StreetEditorV2.ps1`。它会执行新版编辑器用例、新街区回归、PlayMode 和 Windows 构建，各阶段保存源码指纹；实际鼠标操作、设备声音和真人无障碍验收另行记录。上方 Release 是街区游戏试玩包，编辑器本身随 Unity 源码工程提供。

新版验证入口：

```powershell
pwsh -NoProfile -File tools/Verify-Project.ps1
pwsh -NoProfile -File tools/Verify-Unity.ps1 -Stage EditMode -TestFilter TingLu.Tests.Upgrade
pwsh -NoProfile -File tools/Verify-Unity.ps1 -Stage PlayMode -TestFilter TingLu.Tests.Upgrade.Acceptance
pwsh -NoProfile -File tools/Verify-Unity.ps1 -Stage BuildWindows
pwsh -NoProfile -File tools/upgrade/Verify-StreetPlayer.ps1 -ExecutablePath '<本次构建的TingLu.exe>' -Visible
```

Player 画面检查需使用可见窗口；本机隐藏窗口可能得到纯黑截图，验证工具会将其判为失败。测试与构建通过只证明各自检查范围。真实声音听辨、鼠标手感和视障玩家体验需分别记录。下方保留旧 M0 资料，不代表当前街区操作和交付状态。

# 旧 M0 工程资料

历史 M0 试玩包仅在开发机另行保留，未上传此仓库；其原本地路径为 `output/听路试玩_2026-10-14_配置版/TingLu.exe`，不作为 GitHub 下载链接。当前公开试玩请使用上方的 Release。

正式首关现在读取绑定的 `Assets/TingLu/Levels/Data/FirstStep.json`，人物路线按关卡配置运行；暂停冻结路线，续玩恢复其时间进度，旧首关存档保持兼容。程序已验证复制到另一盘的中文、空格目录并从不同工作目录启动。关卡资源通过 Unity 资产身份绑定，存档使用系统用户目录，开发工具自动发现指定版本的 Unity。

《第一步》的模块已接入 DemoSession，纯 C# 装配检查覆盖开始、移动、敲杖、暂停、失败恢复、续玩、收集和结算。2026-10-14 已使用本机 Unity 2022.3.62f1c1 完成 20 项 EditMode、3 项 PlayMode 和 Windows 开发构建。Windows 程序已做启动检查；中文语音兼容问题已修复，实际 Player 日志已确认合成与播放开始。证据与缺口见 [docs/STATUS.md](docs/STATUS.md)。原始设计材料保持原样。

公共契约 `0.2.0`：`Assets/TingLu/Contracts`。数值集中于 `Assets/TingLu/Config/Shared/GameplayConfig.cs`。候选编辑器 Unity 2022.3.62f1，Built-in，Input System 1.7.0；输入设置已写入 `ProjectSettings.asset`，仍需编辑器核对。

1. 用候选编辑器打开本目录，等待包解析；核对 Active Input Handling 为 Input System Package。
2. 打开 `Assets/TingLu/Scenes/Levels/FirstStep.unity` 后进入 Play。场景绑定 DemoEntry 和声纹材质，由运行入口创建相机、单一监听器和业务模块；Build Settings 已指向此场景。
3. 若场景缺失，执行 `TingLu/Create First Step Demo Scene`。生成器保留已有场景。旧 G0 入口只用于契约状态展示。
4. W/A/S/D 或方向键移动，Shift 聆听，空格敲杖，G 重听目标，Esc 暂停；菜单方向键选择/调整、回车确认、退格返回。手柄左摇杆移动、左肩聆听、A 敲杖/确认、Start 暂停、Y 重听目标、B 返回。触屏自动显示操作按钮；PC 可在 DemoEntry Inspector 启用 Show Touch Controls 测试适配。

无需 Unity 的检查：

```powershell
pwsh -NoProfile -File tools/Verify-Core.ps1
pwsh -NoProfile -File tools/Verify-AudioUi.ps1
pwsh -NoProfile -File Assets/TingLu/Tests/EditMode/PerceptionMemory/Run-PureChecks.ps1
pwsh -NoProfile -File Tests/Integration/Acceptance/Run-Acceptance.ps1
pwsh -NoProfile -File tools/Verify-Demo.ps1
pwsh -NoProfile -File tools/Verify-Project.ps1
pwsh -NoProfile -File tools/Verify-LevelEditor.ps1
```

这些检查不替代 Unity 编译、真实音频、HRTF、视障玩家或设备验收。音效为工程占位合成音；中文语音尝试 Windows System.Speech / SAPI，需要安装可用中文声音。耳机/扬声器选项目前保存偏好，未做设备路由；震动与系统读屏未接入。

旧版零代码关卡编辑器（2026-10-14）：用 Unity 打开工程后，选择 **TingLu → 旧版工具 → 第一步关卡编辑器**。此工作台仅处理 M0《第一步》数据，提供对象拖放、盲道折线、对白等旧版功能；历史操作步骤见 [旧版编辑器指南](docs/LEVEL_EDITOR_GUIDE.md)。制作新版街区请使用上方 v2 入口。

可分步验证导入、EditMode 测试和 Windows 开发构建：

```powershell
pwsh -NoProfile -File tools/Verify-Unity.ps1 -Stage Import
pwsh -NoProfile -File tools/Verify-Unity.ps1 -Stage EditMode
pwsh -NoProfile -File tools/Verify-Unity.ps1 -Stage PlayMode
pwsh -NoProfile -File tools/Verify-Unity.ps1 -Stage BuildWindows
```

非默认安装位置使用 `-UnityPath '完整路径/Unity.exe'`。先关闭打开本项目的编辑器再运行。每次生成独立日志和结果文件到 `output/unity-verification`；Windows 可执行文件保存在该次结果目录中。没有编辑器或项目被占用时记录 NOT_RUN，脚本以非零状态结束。若编辑器已写完结果但退出卡住，脚本停止自己启动的进程并在结果中记录 FORCED_AFTER_COMPLETED_STAGE；测试和构建证据仍单独校验。构建成功后仍需启动游戏检查实际声音和操作。

