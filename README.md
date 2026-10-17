# 听路街区升级

本目录是新版独立工程，原 `D:\听路` 保持只读。当前开发、运行入口和验证证据以 [街区状态](docs/upgrade/UPGRADE_STATUS.md)、[街区交接](docs/upgrade/RESUME.md) 为准。

Git 仓库包含 Unity 工程源码、资源、项目配置、工具和交付文档。克隆后用 Unity **2022.3.62f1c1** 打开仓库根目录，等待依赖解析，再打开下方的 `StreetCrossroads.unity` 场景。`Library` 等缓存和 `output` 下的试玩包、验证输出、交接快照保留在开发机，不纳入 Git；文档中的 `output/...` 路径是本地证据路径，克隆后需通过验证/构建脚本重新生成。Git 属性保留文件原始字节，方便复核已验证源码指纹。

2026-10-17 当前试玩：[听路街区交付复核版](output/听路试玩_2026-10-17_街区交付复核版/TingLu.exe)。请保留整个目录。当前源码通过 EditMode184/184、PlayMode2/2、Windows构建和1280×720/960×640可见Player。已完成灯态与坡道、重复探测碎片、真实音频池重试、短按输入及菜单裁字修复；235构建文件运行后哈希一致。实际设备出声、键鼠手感和真人无障碍仍单列验收，详见[本轮复核](docs/qa/upgrade/FINAL_RECHECK_2026-10-17.md)。

新版首场景为 `Assets/TingLu/Scenes/Levels/StreetCrossroads.unity`。WASD 或方向键移动，鼠标水平移动转身，右键按下一次敲杖；聆听被动生效，Esc 暂停，G 重听目标。镜头方向固定；空格与 Shift 不再触发旧玩法。新版存档使用独立产品名与命名空间。

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

新版试玩入口：[output/听路试玩_2026-10-14_配置版/TingLu.exe](output/听路试玩_2026-10-14_配置版/TingLu.exe)，无需打开 Unity。保留整个试玩目录；操作说明在同目录 `开始体验.txt`。旧试玩包另行保留。

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

零代码关卡编辑器（2026-10-14）：用 Unity 打开工程后，选择 **TingLu → 关卡编辑器**。中文工作台提供模板复制、对象拖放、盲道绘制、人物移动路线、对白/声音表单、撤销重做、草稿保存、检查和试玩。关卡内容进入既有游戏业务；试玩存档使用独立目录。操作步骤见 [关卡编辑器指南](docs/LEVEL_EDITOR_GUIDE.md)。本机 Unity 已可用；工作台鼠标操作与临时场景试玩仍待人工验收。

可分步验证导入、EditMode 测试和 Windows 开发构建：

```powershell
pwsh -NoProfile -File tools/Verify-Unity.ps1 -Stage Import
pwsh -NoProfile -File tools/Verify-Unity.ps1 -Stage EditMode
pwsh -NoProfile -File tools/Verify-Unity.ps1 -Stage PlayMode
pwsh -NoProfile -File tools/Verify-Unity.ps1 -Stage BuildWindows
```

非默认安装位置使用 `-UnityPath '完整路径/Unity.exe'`。先关闭打开本项目的编辑器再运行。每次生成独立日志和结果文件到 `output/unity-verification`；Windows 可执行文件保存在该次结果目录中。没有编辑器或项目被占用时记录 NOT_RUN，脚本以非零状态结束。若编辑器已写完结果但退出卡住，脚本停止自己启动的进程并在结果中记录 FORCED_AFTER_COMPLETED_STAGE；测试和构建证据仍单独校验。构建成功后仍需启动游戏检查实际声音和操作。

