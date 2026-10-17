# 集成只读审阅（2026-10-17）

## 范围与证据边界

初次审阅只读检查 `docs/upgrade/DECISIONS.md`、`docs/upgrade/RESUME.md`、`docs/qa/upgrade/current-stage-results.json`、`docs/qa/upgrade/DELIVERY_GAPS_RECHECK_2026-10-16.md`，以及当前街区入口、会话、信号/高程呈现、存档和 Player 探针源码。随后按主控授权修改 `StreetRenderPlan.cs` 与 `StreetRendererTests.cs`，并同步本报告；没有启动 Unity 或 Player。

当前已落盘的基准是源码指纹 `92FA7E62070F6F1DF16EBE8CAD5A59472FED768AE814DB4610752D91C4A0802A`，EditMode 169/169、PlayMode 2/2、Windows Build PASS，Player 两档窗口启动 PASS；这些是 2026-10-16 检查点结果，不自动认证随后文件变化。此次现场发现 `StreetInputSampler.cs`、其 Role01 测试和 `StreetScreenshotProbe.cs` 在 2026-10-17 有更新；探针与输入的新内容不在 04:39 handoff ZIP 中，且没有对应新 Player 结果。因此本文不把 92FA 结果视为这些变化的验证，也不评价输入角色正在修改的代码。

## 发现的问题与修复状态

### 已知高程地面上的玩家朝向标记被地面遮挡（已修复待验证）

**修复前影响：** 玩家站在盲道/人行道等升高的已知面上时，地面朝向箭头低于地面网格，可能被网格深度遮住；坡道高侧也受影响。该标记用于黑暗地图中的当前位置与朝向辨认，丢失后用户会看不到玩家指向。

**修复前根因：** 出生点 `(-5.5,-16)` 位于盲道面（`Assets/TingLu/Levels/StreetLevel.cs:159-160,128-130`），盲道 Surface 的高度系数是 0.15（`:233-254`）。Renderer 将已知面顶点画在 `0.015 + HeightAt(point)`，出生面因此为 y=0.165（`Assets/TingLu/Presentation/StreetRenderer.cs:235-255`）。此前 Player strokes 的 elevation 为 0，Renderer 对 Player stroke 只取固定基准 y=0.06（`StreetRenderer.cs:67-76`），低于地面网格；地面材质 `ZWrite On`（`Assets/TingLu/Shaders/StreetSurface.shader:15`），所以地面可能遮住箭头。

**当前实现：** `StreetRenderPlan` 只遍历 `StreetMemoryFrame.Surfaces`，对包含玩家位置的已知面计算局部 `HeightAt()` 并取最高已知高程，给三条 Player strokes 设置该抬升值；没有已知面覆盖时保持原高度，不查询 World。信号色轮廓则按已知灯具自己的 `Height` 加 0.005m 绘制在灯顶上方，避免与已知地面深度冲突。

**回归：** `StreetRendererTests.PlayerArrowUsesKnownGroundHeightAndSignalColorSitsAboveKnownFixture` 检查实际 LineRenderer：出生箭头三段位于 y=0.21，灯色轮廓位于 y=2.05；移除玩家脚下的已知地面后，箭头回到 y=0.06。`ColorBlindSignalRendererDistinguishesFreshGreenAndRedThenClearsExpiredColor` 检查青蓝/橙黄两态和过期清空。回归先按预期 y 定位实际渲染线段，再验证颜色；颜色以 Color32 单通道最多 1 字节的量化误差比较，避免浮点存储差异误报，同时保留线段位置、数量和目标颜色约束。测试源码已添加，仍待主控统一运行 Unity EditMode 并检查新 Player 画面。

### 启动初始化异常的恢复/退出入口（已修复待验证）

**历史影响：** 初始化异常时曾只显示黑底错误文字，`Update()` 返回且故障标签没有交互，玩家无法从游戏内退出。

**定位：** 初始故障位于 `Assets/TingLu/Bootstrap/Runtime/StreetEntry.cs:31-62,64-67,91`，材质缺失可由 `StreetRenderer.Configure()` 抛错（`Assets/TingLu/Presentation/StreetRenderer.cs:47-49`）。

**当前状态：** 主控修改后 `StreetEntry.OnGUI()` 已提供“退出游戏”按钮与 Esc/Return 快捷键（当前 `StreetEntry.cs:91-104`），故障退出不保存部分初始化状态。源码层问题已闭合，IMGUI 实际退出路径仍待当前树 Player 验证。
## 接线与兼容性核对

- **信号链路静态正确，灯色高度与色盲区分修复待验证。** `StreetSession.Start()` 将 `Player.Probed` 结果交给感知与记忆，并把许可信号观察更新到短时列表（`StreetSession.cs:72-73`）；感知只对已确认信号对象生成到相位边界或 0.8 秒为止的观察（`StreetPerception.cs:369-385`）；Session 在模拟时间前进时过期清理（`StreetSession.cs:196-199`）；`StreetEntry.Update()` 把 `Session.Signals` 送入 Renderer（`StreetEntry.cs:75-79`）；RenderPlan 只用这些观察叠加红/绿轮廓（`StreetRenderPlan.cs:93-107`）。彩色轮廓按记忆中对应灯具的 Height 抬到顶部以上 0.005m；ColorBlind 调色为绿灯使用青蓝、红灯使用橙黄，Standard 调色映射保持原值；全程不从未知 World 获取灯态或几何高度。Role08 已加实际 LineRenderer 高度、两态颜色切换及过期清除回归，尚未运行；“真实灯箱和清空相位可辨”的 Player 专项画面仍待当前树验收。
- **高程数据链路存在，但路沿立面呈现有未确认风险。** `StreetLevel.AddSurface()` 对人行道/路沿设 0.15m，并为 ramp 建立 0→0.15 线性高度（`StreetLevel.cs:233-254`）；Perception/Memory 保留高度；Save DTO 写读 base/slope（`StreetSaveService.cs:77-79,380-383,422-426`）；Renderer 依据 `HeightAt()` 只抬高已知面顶点（`StreetRenderer.cs:235-255`）。RenderPlan 为高物件生成竖向细节，但没有为相邻 `StreetKnownSurface` 的高度差生成侧立面（`StreetRenderPlan.cs:74-90`）。所以坡面顶点可正确倾斜，普通路沿是否形成清楚、连续的立面仍不能从代码断言。建议最小闭环是仅在双方均已知时生成公共边高度面，并以坡道专项断言和新 Player 视觉检查确认；这属于当前未验的呈现风险，尚无证据称其为已复现运行故障。
- **schema-1 可选字段兼容逻辑成立。** 高程字段是 `IsRequired=false` 的 float（缺失时默认为 0），纹理轴缺失时显式回退到世界 X，旧临时状态仍被置为 stale；保存时照常校验原 payload checksum。现有 Role10 夹具覆盖“缺少 textureAxis”，没有专门删掉三项高度字段的旧档夹具；但静态解码路径显示旧平面记忆可按高度 0 读取。此结论是源码检查，不是本轮运行测试。

## Player 探针真实性与边界

已保存的 `20261016-042922-7f5de651` / `20261016-042932-2fccbbcc` 是可见 Development Player，日志及结果分别记录窗口尺寸、非黑 PNG、场景入口、单 Camera/AudioListener、启动后一次静止敲杖探测，以及软件音频 receipt 到 `STARTED`。源码通过反射调用生产 `StreetSession.Tick` 与 renderer/HUD；为确定性截图会停用 `StreetEntry.Update()`，所以它不是键鼠驱动的实机操作，也没有跑完整路口路线、碰撞恢复、Continue 存档、信号相位或坡道路线。`STARTED` 只表示软件提交播放，不能证明设备出声或真人听辨；结果中的 `humanListening`、`humanAccessibility` 均为 `NOT_RUN`。

当前 `StreetScreenshotProbe.cs` 新增了 `PausePressed` 路径、状态指纹和暂停/恢复截图逻辑，但这些改动晚于上面 Player 结果，且当前报告 JSON 仍没有 `pauseEvidence` 字段。不能用旧 Player PASS 宣称新增暂停探针已跑，更不能把确定性 `PausePressed` 帧写成物理键盘输入证据。主控可在下一次构建后检查其暂停截图与结果 JSON。

## 结论

玩家箭头按已知脚下地面高程抬升、彩色灯态轮廓按已知灯具高度抬至顶部以上、ColorBlind 下两种灯态使用不同颜色的源码修复及 LineRenderer 回归已纳入当前工作树；测试尚未运行，新的 Player 图也未检查，不能宣称运行验证通过。启动异常无退出入口已由主控补充故障退出 UI，代码层闭合但未实机验证。其余集成接线与 schema-1 缺省读取在源码层相符；路沿立面、灯态画面、坡道存档往返和 10/17 探针更新仍需当前树证据。未把真人设备/无障碍验收缺失记成软件 bug。旧 92FA PASS 仍只证明其当时的检查点。

## 主控修复复核

StreetEntry 启动故障退出 UI 已通过源码静态复核，启动异常时不会调用存档写入。详见上文“启动初始化异常的恢复/退出入口”。当前树 Unity/Player 结果仍缺，未在此执行验证。
