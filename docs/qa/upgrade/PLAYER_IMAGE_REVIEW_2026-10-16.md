# Player 与 PlayMode 截图独立复核

日期：2026-10-16  
状态：只读目检摘要；不构成最终验收，也未启动 Unity。

## 证据范围

- 两档可见 Player：`output/street-verification/20261016-031133-9b4cc852`（1280×720）与 `output/street-verification/20261016-031204-e17f083b`（960×640）。两组都来自 `output/unity-verification/20261016-030826-3a9d9ce5/Windows/TingLu.exe`，报告的 EXE SHA-256 均为 `CC858EB9A7E2DB2D414632600041100BD4840DD2F016593A242B145AFE6C5941`，状态为 PASS、可见窗口、`StreetCrossroads`、`speechBackend=STARTED`。
- PlayMode：`output/unity-verification/20261016-030730-a823f3ed`，指纹 `CAD6588E71932B4BD20F21B7B562391B9F7A95141AC5D7F73138FFCC5B6F0733`，2/2 PASS。`street-acceptance/a25-a30-results.json` 列出五张 1280×720 的相机世界帧图，截图范围说明为 `Camera.Render`，不含 HUD 或菜单。
- Player JSON 的 `humanListening` 与 `humanAccessibility` 都是 `NOT_RUN`。`speechBackend=STARTED` 仅说明软件已提交 `AudioSource.Play`，不证明设备出声或真人听见。

## 目检结果

两档主菜单都能读出“听路”“开始新游戏”“继续上次”“设置”“重听目标”“退出游戏”；按钮未见裁切或全黑捕获。两档探索图都显示左上角勇气/专注 HUD、纯黑未知区域、灰色已知路面、金色路线/角色标记和右上角听觉图标。已知区域确有纹理填充，画面不是全黑，也未见品红材质。

两档探索图的地面格纹在相邻道路/多边形区域间出现明显方向不连续，呈现斜转或碎片感。这个可见问题与已报告的 Renderer 碎片 `LongestEdgeAxis` 方向缺陷相符；CAD 版截图不能通过最终纹理方向验收。截图可证明渲染出已知表面与 HUD，不能证明贴图方向正确或完整地图设施齐全。

PlayMode 五图确认了相机捕获中能出现已知路面、道路标记、路线扇区和终点区域，但不作为 UI/菜单目检：

- `a30-start-explored.png`：出生处局部已知路面与路线扇区。
- `a25-paused.png`：画面仍是道路世界；按报告约定它是 `Camera.Render` 帧，不能用来判断暂停菜单是否正确显示。
- `a30-real-collision-recovery.png` 与 `a30-continued.png` 的 PNG SHA-256 完全相同，均为 `324CA98C15F32F2F5F9F65CB17EB2C188C8F4069109B62E5A5B89C307B0EE84C`。同一结果 JSON 记载 `collisions=1`、`recoveries=0`、`finished=true`。因此这对图没有展示两个不同的画面状态，标签本身不足以证明碰撞恢复后的画面变化。
- `a30-finished.png`：可见一段新的已知道路/终点区域；黑色部分仍是未探明区域。该世界帧不含 HUD 或结算 UI。

另在 PlayMode XML 中计到 53 条 `Street audio failed: voice_limit_no_lower_priority_voice`。这表明语音池在相应时刻拒绝了没有更低优先级可抢占声道的请求；当前日志没有足够上下文证明这些请求全是可丢弃环境声，因此不能写成“无丢声”。这条限制不改变限定范围内的渲染/性能测试状态；设备听辨仍待做。

## 当前判断与待补证

CAD 版自动化 EditMode、PlayMode、BuildWindows 与两档可见 Player 有可追溯证据；本复核只确认上述截图的视觉事实和图像缺口。图像支持“菜单可见、探索画面含已知表面、未知区为黑”的有限结论；格纹错向问题仍在。新 `TextureAxis` 跨 Contracts、Perception、Memory、Save、Renderer 的修复尚未经过统一同指纹验证，所以本记录不对修复版作结论。

后续需要在新指纹下重跑统一 EditMode、PlayMode、BuildWindows 与两档可见 Player，重新检查纹理方向；同时保留 Player 软件 Started 与真人/设备听辨的证据边界，并调查上述声道池拒绝日志的事件上下文。不要用 CAD 版图像覆盖修复后的目检，也不要把 `recoveries=0` 的这组图当作恢复画面证据。
