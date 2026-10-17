# Street Player 暂停与恢复截图证据交接

status: READY_FOR_PLAYER_RUN
date: 2026-10-17
scope: `Assets/TingLu/Diagnostics/Upgrade/StreetScreenshotProbe.cs` 与 `tools/upgrade/Verify-StreetPlayer.ps1`

## 已完成的源码工作

- 探针仍只使用一个实际创建的 `StreetEntry.Session`；等待新入口完成后开始游戏，先执行一次生产右键探测，再记录 `Playing` 状态。
- 以 `StreetInputFrame.PausePressed=true` 调用 `StreetSession.Tick`，由生产 `AccessibleMenu.HandleInput` 进入 `StreetSession.Pause` 和 `MenuScreen.Pause`；再次发送同一暂停边沿，由生产 `TryResume` 调用 `StreetSession.Resume` 回到 `MenuScreen.Playing`。
- 暂停前、暂停后、恢复后立即、恢复后无输入推进 0.25 秒分别采集 session 对象身份、clock、World 捕获时间、玩家位置/朝向、信号相位、菜单屏幕、暂停标志和 Memory/World SHA-256 指纹。暂停断言要求这些值逐项不变；恢复立即快照要求仍与暂停前相同；推进断言要求模拟时钟和世界时间都至少增加 0.2 秒。
- 暂停和恢复截图通过 `ScreenCapture.CaptureScreenshot` + `WaitForEndOfFrame` 取得，仍保留渲染组件和 `AccessibleMenuPanel` 的 live OnGUI；生产 renderer 调用传入 `session.Signals` 的六参数重载，避免遗漏信号灯态。截图文件为 `street-crossroads-paused.png`、`street-crossroads-resumed.png`，另保留初始菜单、探索图、NS 信号灯探测坡道图和东侧落点图，共六张。
- 探针报告新增 `pauseEvidence`、四个状态快照、`screenshotScope` 及暂停/恢复路径说明。快照和路线证据保存 `SignalPhase`、NS 信号的 `IsGreen`、`ObservedAt`、`ExpiresAt` 与探测时刻。物理键盘或真人 Esc 输入不作声明；证据证明的是生产 `PausePressed` 菜单路径。
- 探针随后以生产 `StreetInputFrame` 步进同一玩家，经过西侧障碍绕行与西侧 checkpoint，在 `CaneReadyAt + 1.1` 且 `Focus > 45` 时朝 NS 灯探测，并继续穿过北侧斑马线到东侧落点。路线证据要求两个 checkpoint、非空 live signal 集合和未过期 `street.signal.ns`。
- 验证脚本默认分别运行 1280x720 和 960x640 两次，使用 popup window 请求这两个 Player backbuffer 尺寸，不强制 OS fullscreen；每次使用独立保存根和证据目录。两次都检查六张 PNG（菜单、探索、暂停、恢复、NS 信号灯探测坡道、东侧落点）的完整头、请求尺寸、8 像素采样非黑、SHA-256、暂停断言、路线断言和 Player 日志错误模式；主报告包含 `pauseEvidence`、`routeEvidence` 和 `alternateResolution`。

## 修改文件

- `Assets/TingLu/Diagnostics/Upgrade/StreetScreenshotProbe.cs`
- `tools/upgrade/Verify-StreetPlayer.ps1`
- `docs/handoffs/PAUSE_PLAYER_EVIDENCE_2026-10-17.md`

未修改生产 `StreetSession`、`StreetEntry`、输入、菜单、renderer、共享状态文档或其他角色文件；未运行 Unity、BuildWindows 或 Player。

## 当前断言

`StreetScreenshotProbe` 只在以下条件全部成立时将报告置为 `PASS`：

1. 初始菜单、探索、暂停、恢复、NS 信号灯探测坡道、东侧落点六张图存在。
2. 同一会话的菜单序列为 `Playing → Pause → Playing`，暂停时 `Clock.Paused=true`、恢复时为 `false`。
3. 四份快照来自同一 session 对象；暂停前后 clock、World 时间、玩家位置/朝向、SignalPhase、World 对象指纹和 Memory 指纹全部相同。
4. 恢复后的立即快照仍保持暂停前状态。
5. 恢复后 0.25 秒无输入 Tick 使 clock 和 World 时间各增加超过 0.2 秒。
6. 仅在 renderer 通过 `session.Signals` 的生产六参数重载后截图。

## 总控待运行验证

新版 Windows Player 构建完成后，在项目根目录运行：

```powershell
pwsh -NoProfile -File tools/upgrade/Verify-StreetPlayer.ps1 `
  -ExecutablePath '<新版完整Windows目录>\TingLu.exe' `
  -Visible
```

脚本会在 `output/street-verification/<timestamp>-<id>/` 下写入主分辨率证据和 `alternate-960x640/` 证据。需要总控检查 `results.json` 的 `status=PASS`、`pauseEvidence` 全部布尔值、`routeEvidence.routePassed=true`、`signalCount>0`、`nsSignalLive=true`、两组六张图的 `imageContentChecks` 尺寸/非黑采样与 SHA-256、六张截图的实际画面，以及 Player 日志无 shader/runtime 异常。Unity 编译、实际 Player、截图视觉和真人输入/听感仍为待运行项目证据。

