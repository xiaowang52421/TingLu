# 代码核心审查与建议书（第三方只读审查）

日期：2026-10-16
性质：**建议书，非指令**。本文档由第三方只读审查产出，供 00 主控决策参考。
**00 可采纳、可部分采纳、可不采纳；不采纳的建议须在 `docs/upgrade/` 下给出理由并留档。**
本文件不修改任何生产代码，不替代 `ACCEPTANCE.md` / `COMPLIANCE.md` / `RECHECK_AUDIT_*.md`，也不改变任何既有 PASS/FAIL/NOT_RUN 结论。

审查方式：4 个独立审查员并行深读源码，全程只读，未运行 Unity。
读取范围（诚实声明）见附件一。**未覆盖**：编辑器窗口、旧版 FirstStep 关卡内容、人工听感与实机体验。

---

## 第零部分 · 给主控的结论摘要

**一句话**：这个项目的**契约与纯逻辑内核质量很高，明显高于一般 Unity 项目**；问题不在设计意图，而在**跨层不变量没有单一真相**，以及**"代码完成"与"效果生效"之间出现了背离**。

**最需要主控注意的一条**：语音管线**运行时代码、生成脚本、hash 契约、测试全部齐备，唯独没有音频文件** → 缓存 100% miss → 动态合成在低完整性 Player 下**结构性必然失败** → **全盲玩家进入游戏后听不到任何提示，且失败是静默的**。这不是"待优化项"，是**当前不可用**。

**强烈建议（其余可议）**：第 1、2、3 项（生成语音语料、`FinishSpeech` 重试、4 个 asmdef 加 `noEngineReferences`）。三项合计工作量很小，但分别对应"游戏能否被盲人使用""提示是否会被永久丢弃""分层边界能否被一行代码破坏"。

---

## 第一部分 · 做得好的地方（建议保持，不建议改动）

以下 10 项经实际读码确认，是本项目最值钱的资产。

1. **不可变性 + 构造期校验贯穿全项目。** 所有 DTO 为 `sealed` + 全 `readonly` 字段 + `ReadOnlyCollection` 防御性拷贝（`StreetContracts.cs:36-43、60-67`）；`ContractGuard` 统一校验（`Model.cs:37-54`）；`Point2` 构造即拒绝 NaN/Inf（`Model.cs:28`）；`StreetRoute` 构造即拒非法速度/信号轴（`:77`）；`StreetProbeRequest` 拒非法扇形（`:120`）；`StreetKnownObject` **拒绝把 Active 对象存成几何记忆**（`:138`）。
   → **非法状态构造不出来**，这是模拟内核最值钱的一个决定。

2. **零 Unity 依赖是编译期强制的。** `TingLu.Contracts.asmdef:5`、`TingLu.Config.asmdef:5` 开启 `noEngineReferences: true`——不是口号。

3. **确定性到位。** 显式排序全覆盖（`StreetWorld.cs:74`、`StreetMemory.cs:622-633`、`StreetPerception.cs:160`、`:372-376`）；字典全 `StringComparer.Ordinal`；注入 `IGameClock`（`StreetPerception.cs:24`）；生产代码**零** `DateTime`/`UnityEngine.Random`/`Debug`。唯一残留是 `List.Sort` 不稳定，但相同输入仍可复现。

4. **`D04`/`R14` 在契约层被结构强制**，而非靠约定：`Model.cs:124-125` 直接抛异常（仅声音不得带轮廓、被动声只能是 SoundLocation）；`StreetRenderPlan.cs:111` 只渲染 `LocalGeometry`+`Static`；`StaticMemoryStore.cs:26` 排除 SoundLocation。

5. **纯 sim-time 计时让"暂停冻结一切"结构性成立**（`SimulationClock.cs:18` + 绝对时间戳），无需在车辆/信号/冷却/探测各处逐点判断。

6. **渲染层的数据流与泄露边界是三重保险**（渲染组实测）：
   - `StreetRenderPlan.Create` 输入仅 memory/perception/player/settings/now（`StreetRenderPlan.cs:60-61`），**全文件无** `Camera.main`/`Find`/`Resources`/`IStreetWorld`
   - 全工程 grep：`IStreetWorld`/`StreetWorldFrame` 只出现在 World/Perception/Player/Bootstrap/Tests，**Presentation 与 View 零引用**
   - `StreetEntry.cs:35` 与 `StreetRenderer.cs:49` 双重钉死 `cullingMask=1<<8`，全工程仅展示对象在该层
   - 阴影/探针全关（`StreetRenderer.cs:144-147、220-221`）+ shader 无光照 → **无阴影泄露通道**
   - 相机 yaw 冻结在契约常量（`StreetCameraRig.cs:111`）→ 玩家无法转向窥视未探索区

7. **恢复顺序中的关键细节写对了**：`SetPaused→Generation++→audio.Reset→playback.Reset`（`StreetSession.cs:134`）→`ImportTraffic+Clock.Reset`（`:135`）→`Restore`（`:136`）→`MarkStale+Clear`（`:137`）→**取消暂停后**才 `RefreshSenses`（`:138`）。取消暂停必须先于 `RefreshSenses`，否则 `ProjectAcoustics` 在暂停态返回空观测（`StreetPerception.cs:98-111`）。

8. **存档可靠性基础扎实**：校验完整（`StreetSaveService.cs:256-263`）、未来版本判定**先于** checksum（`:252`）、`Flush(true)` 临时文件（`:475-482`）+ `File.Replace` 轮转备份（`:179`）、损坏时保留好备份（`:166-171`）、命名空间真隔离（街道 `TingLuStreetUpgrade/` vs 试玩 `TingLu/`）、**无写死绝对路径**、浮点字符串化全 `InvariantCulture`（`:471、504、514、523`）。

9. **"宁可抛异常也不静默出错"**：`ConvexPieces` 遇不可三角化的自交多边形直接抛（`StreetPerception.cs:463`），零面积/退化处处拦（`:394、405、512、619`）。

10. **未发现任何 `if(test)` 类特例分支，没有为通过测试而写死的代码。** 诊断探针也不污染生产（`StreetScreenshotProbe.cs` 整文件 `#if`，`:1`、`:56`，且拒绝写 `persistentDataPath`）。

**建议**：以上 10 项在后续任何重构中**作为不变量保留**。特别是第 1、2、4、6 项，改动前应把它们写成回归断言。

---

## 第二部分 · 问题清单（按严重性）

### P0-1 语音管线空转 —— 当前不可用（头条）

| 部分 | 状态 |
|---|---|
| 运行时 `StreetSpeechCache.TryLoad` | 已就位（`UnityAudioPlayback.cs:668-683`） |
| 生成脚本 `tools/upgrade/Build-StreetSpeechCache.ps1` | 已就位 |
| hash 契约 | **两边完全一致**（脚本 `:45-52` 与 `:656-666` 均 SHA256(UTF-8) hex；脚本写 `AudioAssets/Resources/TingLuSpeech` `:139-141`，运行时读 `"TingLuSpeech/"+hash` `:653,673`） |
| **实际音频文件** | **`Assets` 下 0 个 wav/mp3；`AudioAssets` 仅一份 `StreetAudioPlaceholderManifest.txt`** |

**后果链**：缓存 100% miss → 动态合成接管 → **低完整性 Player 下必然全静音**。`UnitySpeechSelectionTests.cs:63-83` 断言缓存可加载，**该测试当前为红**。

**关键点**：失败是**静默**的——玩家永远不知道提示音为什么不响。

**建议 1（强烈建议采纳）**：执行 `Build-StreetSpeechCache.ps1` 生成 86 条语料。
理由：骨架、契约、加载、测试全就位，**只差内容**；直接决定游戏能否被盲人使用。工作量低。
若不采纳的理由需说明：例如"已完成但产物未入库"或"另有替代方案"。

**建议 2（强烈建议采纳）**：修改错误文案的产生方式——动态错误文案（`StreetSession.cs:143,162,176`）永不在预生成语料内，Player 中必然静音。改为固定文案或纳入语料。
理由：**出问题时恰恰什么都听不到**，对全盲用户是最坏情况。

---

### P0-2 径向黑缝的根因已实验复现（渲染组已验证，非推测）

**算法链路**：自写耳切 `Triangulate`（`StreetRenderer.cs:271-277`）→ **先** `RemoveRedundantVertices`（`:275/375-408`）→ `TriangulateRemaining`（`:279-325`，前置 `TrySplitRepeatedVertex` `:327-373`）。

**五处丢弃点**：
1. `:303` 只认**严格凸耳**
2. `:318` `if(!foundEar) return new List<int>()` → **把已切出的 result 全丢**
3. `:157` `localTriangles.Count==0 return` → 连顶点都不写，**整块地面消失**
4. `:419` `InsideTriangle` 严格 `>0.0`（边上点不遮挡）→ 跨 T 型接点的耳被切，余下走线变非简单 → 更易 stall
5. **`:275` 的精确共线删除（`:400` `cross==0.0&&dot>=0`）先把 `:354-358` 注释所说的"同一点表示两次"信号删掉，导致 `:285` `TrySplitRepeatedVertex` 永远没机会分裂**

**实验证据（关键）**：审查员把 `:271-408` **逐行移植到 PowerShell 复算**（仅把 float `Distance` 换成 double 平方距离，容差语义等价）：
- 8 顶点片 → 3 三角形，面积 3.116481 == shoelace ✓ 通过
- **两瓣共用一个顶点的 10 m² 多边形（CCW、signed area=2，能过 `CopyPolygon` 校验）→ 输出 0 三角形**
- `RemoveRedundantVertices` 把 8 顶点删成 6（两个共用点都删掉）后 stall；**不预先删共线点时** split 路径能渲出 6 m²

→ **缺片 = 共线点先行删除 + stall 全量丢弃**。径向外观来自扇区裁剪——被丢的片本身就是扇形瓣。

**测试为何没抓到**（三段式失效）：
- 唯一回归测试 `StreetRendererTests.cs:92-138` 的输入是从真实 183 顶点片"缩减"成的 **8 顶点人工多边形**（`:101-104` 注释自认），只覆盖"删共线点后仍简单"
- 断言用 `:136` `Mathf.Abs(StreetGeometry.Area(polygon))`，而 `Area` 是**有符号** shoelace，**弱简单多边形两瓣反向会抵消**（实测 hourglass shoelace=0）→ **面积断言对该类输入恒成立**
- 只查 `Mesh.triangles`，不查像素

**建议 3（建议采纳）**：把 `:275` 的清理挪到 split 失败**之后**；`TrySplitRepeatedVertex` 容差 `1e-5`（`:333`）提到世界尺度 `1e-3` 量级。
理由：**5 行改动，直击本轮 bug**，风险低。
注意：渲染组明确警告——方案 2（`:318` 返回 result + 残余扇面补齐）**会过绘/泄露未观测区**（实测 T9"双走零宽桥"多边形渲出 12 m² > 真实并集 10 m²），若采纳必须加"输出面积 ≤ 输入面积"硬断言。**渲染组不建议在本轮采纳方案 2。**

若不采纳的理由需说明，例如"已定位到上游 `StreetPerception` 一并修"或"本轮冻结渲染层"。

---

### P1-1 上游供给源：Perception 的 epsilon 错配

```
VertexRayOffsetDegrees   = 1e-4   (StreetPerception.cs:16)
AngleMergeEpsilonDegrees = 1e-6   (StreetPerception.cs:17)
```

`1e-4` 比 `1e-6` 大 **100 倍**。`:191-195`、`:248-250` 对**每个可见顶点**必插 `angle`、`angle±1e-4` 三个角 → **每个可见顶点必然产出 2 个角宽 1e-4° 的细长凸片**，三顶点（原点+3m+7m，夹角 1.745e-6 rad）**近乎共线**。面积 r=7 时约 **4.3e-5 m²**，为 `MinimumCellArea=1e-8`（`:19`）的 **4000 倍** → **任何面积过滤都拦不住**。

**不可靠调大 `MinimumCellArea` 修复**——调大反而丢真实几何。真正错配是两个 epsilon 的关系。

**第二供给源（Memory）**：`TryMerge`（`StreetMemory.cs:458-480`）只按距离去重（`:484`），**不删接缝共线点**；`EdgeKey` 量化后要求端点完全相等（`:696-704`）→ **T 型接缝根本合不上** → 送往渲染层的多边形**带 180° 顶点与未合并 T 接缝**。

**第三处**：`StreetGeometry.CopyPolygon` 只查总面积，**从不删共线顶点**（`StreetGeometry.cs:18`）。`RemoveCollinearVertices` 已存在但是 `private` 且只用于面。

**纠偏（防止查错方向）**：`AddAdaptiveCell` 在偏差不过时确实丢弃跨度（`StreetPerception.cs:274`），但每层折半，丢的是约 `1e-4°/2¹⁸` 的角度，**面积可忽略，不是可见缺片的根因**。

**建议 4（建议评估后采纳）**：统一 epsilon 语义（见建议 8），并把 `RemoveCollinearVertices` 在关卡装载期用于全部多边形。
理由：这是黑缝的**源源不断的供给**，只修渲染端是治标。
**风险提示**：改 Perception 的 epsilon **会改变覆盖语义**，波及 Memory 与渲染层。渲染组同时指出：更好的终局是"由记忆层保留凸片列表、渲染端只做凸多边形三角扇（无容差判断、永不失败）"——但那要动 Memory + 契约，属更大工程。
若不采纳的理由需说明，例如"本轮冻结覆盖语义，先只做建议 3"。

---

### P1-2 存档三个真漏洞

| 问题 | 位置 | 严重/难度 |
|---|---|---|
| **未来版本号不可解析即被判 Corrupt → 覆盖并隔离**。`IsFutureContract` 在版本串非 `x.y.z` 时返回 `false`（`:533、550-558`）→ `"2.0.0-beta"` 会被覆盖隔离，**正是 R01/R20 要防的丢字段场景** | — | 中/易 |
| **Save/Load 对"备份是未来版本"处理不对称**：Save 见即拒绝（`:137`），Load 因主档成功而返回（`:215`）→ **玩家从此永久无法保存，且无任何提示** | — | 中/易 |
| **`Save` 的 catch 吞掉全部异常**，只回 `IoFailure` 且**不记录原因** | `:185-188` | 中/易 |
| `.corrupt.*` 只增不删且无人读 | `:467-473` | 低 |
| ✅ **历史问题已修**：主档 Missing + 备份 Corrupt 在 `:143-144` 返回 Corrupt，不再写新主档；回归测试 `StreetSaveServiceTests.cs:140` | — | — |

**建议 5（建议采纳）**：补未来版本号解析的健壮性（非 `x.y.z` 一律视为未来/不可解析并拒绝写入）+ 让 Save/Load 对称 + 记录异常原因。
理由：三项都是"低工作量、防丢档"。若不采纳的理由需说明。

---

### P1-3 恢复事务与生命周期

| 问题 | 位置 | 说明 |
|---|---|---|
| **`Tick` 无 try/catch** | `StreetSession.cs:93-109` | 异常直穿 `StreetEntry.Update` |
| **恢复留半状态** | `:144` vs `:155` | finally 置 `recovering=false`；`Resume` 不检查上次恢复是否失败 → 可能在"车流已回滚、玩家未回滚"下继续 |
| **无无敌帧** | `:190-193` | `Restore` 清空 `vehicleContacts` 且代数++；检查点落在车道上会连续恢复。**两检查点位于 x=±5.5 人行道（`StreetLevel.cs:56-57`），实测风险低** |
| **`checkpointPlayer` 为合成值** | `:116` | "检查点圆心+检查点朝向"，**非玩家当时真实位姿** |
| ReturnToMenu 后 World/Memory/perceptionService 仍存活 | `:170` | — |
| delta 夹取只在 Entry | `StreetEntry.cs:69` | 夹到 0.1；非 Unity 调用方可传任意大 delta |

**已确认成立（不要动）**：固定子步正确（`:99`）；`Player.Tick` 以 `recoveryGeneration` 快照在四个回调后逐点复查（`StreetPlayerController.cs:129、174、236、275`）→ **未发现"一子步双处理"或跨子步漏检**；一次事故一命中靠 `vehicleContacts`+`failedGeneration==recoveryGeneration` 双层（`:229、237-239、279-280`），同帧多车在首个 true 处退出（`:148、160`），暂停时 Tick 提前 return（`:59`）→ **成立**；**D15 成立**（`StreetMemory.cs:137-150`）。

**建议 6（建议采纳）**：`Tick` 加异常兜底；恢复加帧保护与"上次恢复失败"检查。
理由：低工作量，防崩溃与半状态继续。

---

### P2 硬编码与脆弱点（按组别）

**P2-a 高/易**
- **菜单语音代数靠隐式包装**：`AccessibleMenu.generation` 只在 `Initialize` 赋一次（`AccessibleMenu.cs:97`），全靠 `StreetSession.cs:187` 重写代数才不被按 stale 丢弃（`UnityAudioPlayback.cs:217-221`）→ **任何传裸 `IAudioSpeech` 的调用（Role11 测试即如此）会让菜单语音静默全失**
- **普通碰撞无即时声反馈**：Session 只请求 `cane.tap`/`footstep.*`/`feedback.checkpoint`（`StreetSession.cs:70,74,117`）；`collision.bump`/`feedback.breath` **仅旧 `DemoSession` 使用**（`DemoSession.cs:255,143`）→ 对全盲玩家**"掉血无提示"**（间接发声路径未追踪，标为**推测**）
- **信号声音 ID 重复真相**：`"street.signal.ns"/"ew"` 写死于 World，与 `StreetCatalog.Sounds` 重复（`StreetWorld.cs:471-472` vs `StreetContracts.cs:34`）
- **`FinishSpeech` 池满时只 Emit Failed、不重试** → **整句提示永久丢弃**（`UnityAudioPlayback.cs:504-510`）

**建议 7（强烈建议采纳其中的 `FinishSpeech` 重试）**：给 `FinishSpeech` 加与 `Play` 同款的退避重试。
理由：现在会**永久丢弃整句提示**；退避机制本来已存在且有测试，复用成本极低。

**P2-b 中 severity**
- **两个版本常量并存，无法判断谁权威**：`"1.0.0"`（`StreetContracts.cs:7`）vs `"0.2.0"`（`Model.cs:7`）
- **配置校验与 DTO 不变量不一致**：`*_Angle` 只拒 `>360`（**=360 通过**），而 `StreetProbeRequest` 要求 `<360`（`GameplayConfig.cs:28` vs `StreetContracts.cs:120`）
- `MinimumArea=1e-8` 在 Memory 与 `MinimumCellArea` **同义不同名重复**（`StreetMemory.cs:10`）
- Perception 7 个常量全在实现里（`StreetPerception.cs:15-21`）
- **`StreetConfig.Validate` 用反射 + 字段名前缀豁免**（`StartsWith("CameraTargetOffset")`，`StreetConfig.cs:18-19`）→ 今后**任何新增 float 字段默认 0 都会让配置直接抛异常**
- **`GameplayConfig` 约 15 个旋钮无人读取**，其中 **`perceptionHz` 宣称的"感知限频 20Hz"根本没实现**（`StreetSession.cs:106` 每子步全量重算）→ **配置谎报能力**
- **遮挡射线不读 `blocker.Height`** → 矮障碍只要配 loss 就挡声（`UnityAudioPlayback.cs:106-133`）
- **`Occlusion` 是死参数**：存了但 `EffectiveVolume` 不用（`:582-586`；已并入 Discernibility，故不致命）
- `FeedbackPriority.Navigation` 从不使用 → `voiceprintIntensity` 对街景源无效（`:584`）
- **池 12 偏小** vs 约 20 个源，且同优先级不能互抢（后端 `:358` 仅"严格更低"可被抢占）
- **暂停菜单"从路口重来"实为回检查点**，且**无真正重开关卡入口 → 永久记忆不可重置**（`AccessibleMenu.cs:251` vs `StreetSession.cs:164-167`）
- **每帧新建 plan/HashSet/大量 StreetStroke，`Triangulate` 每多边形每帧新建 ≥3 个 List，mesh 全量清空重建，无缓存无上限**（渲染组）
- **`_BaseColor` 等属性名走字符串键**（`StreetRenderPlan.cs:178-182`）→ 改名静默失效

**建议 8（建议采纳）**：把"同一个量只有一处定义"作为整改原则，优先处理：版本常量、角度上界、`MinimumArea`/`MinimumCellArea`、信号声音 ID、材质属性名（改 `Shader.PropertyToID`）。
理由：这些是"单个看都是小事、合起来是一种模式"的问题，是后续 bug 的温床。

**P2-c 低 / 死代码**
- `ThreatReceiptGate` **全项目无生产调用点**
- `MoveObject`/`RemoveObject` 是**生产程序集里的测试钩子**（`StreetWorld.cs:324-339`）
- `1<<8` 图层写死两次（`StreetEntry.cs:35、55`）；`Wrap(...,3)` 写死显示模式数（`AccessibleMenu.cs:46`）
- **9 个 id 不在目录里**（`footstep.brick/metal/cardboard` 等），被 `StreetAcousticModel.cs:51` **静默过滤**
- **对"18 个 soundId 只有 6 个有映射"的旧结论已过时** ✅：`PlaceholderBank`（`:600-627`）已覆盖全部 18 个 + 10 个材质 `footstep.*`，`StreetAcousticModelTests.cs:351-362` 守住

---

### P3 两代系统共存（结构性）

- **升级新增生产文件 23 个；旧版遗留仍参与编译进构建的 32 个文件（约 6200 行）**
- 同一程序集内两套实现并存：`PlayerController`/`StreetPlayerController`、`PerceptionService`/`StreetPerception`、`StaticMemoryStore`/`StreetMemory`、`WorldRegistry`/`StreetWorld`、`PresentationFrameComposer`/`StreetRenderer`、`AcousticModel`/`StreetAcousticModel`、`LocalSaveService`/`StreetSaveService`、`UnifiedInputSource`/`StreetInputSampler`、`AccessibleMenuPanel`/`AccessibleMenu`
- **旧入口未隔离**：`DemoEntry` 是 `public sealed class DemoEntry : MonoBehaviour`，**无 `#if` 保护**，仍被 6 个旧程序集引用；`FirstStep.unity` 仍在
- **`Geometry2.cs` 与 `StreetGeometry.cs` 两套几何工具并存**
- **两套关卡管线并存**：`LevelDocument`+`LevelCompiler`+**`LevelValidator`**+JSON（旧）vs `StreetLevelFactory` 代码字面量（新）。**新管线完全不经过 `LevelValidator`**
- **`StreetConfig` 被两代共用**；**legacy 还被新测试反向锁定**（Role02 测试断言 `listenSpeed` 必须无效，`StreetPlayerControllerTests.cs:147-160`）

**建议 9（建议先冻结、不作为本轮必做）**：在 `docs/upgrade/` 里明确标注"哪些文件是活的、哪些是历史"，而不是立刻删除。
理由：A31 明确要求保留现有编辑器与原《第一步》作回归，**删除有验收风险**；但**不标注的代价是后续维护者读错代码**。标注是零风险的中间步骤。
若不采纳的理由需说明，例如"以 UPGRADE_*.md 交接文件已足够区分"。

---

### P4 编译期强制力不完整（低成本高收益）

- `Contracts`/`Config` 已开 `noEngineReferences: true` = **编译期强制**
- **`Perception`/`Memory`/`World`/`Player` 4 个 asmdef 未开** → 边界**只靠纪律**。已 grep 确认今日无一处 `UnityEngine/Time/Mathf/Random/Debug`，**但加一行即可破**
- **`TingLu.Audio` 同时装纯模型 + 1009 行播放器 + TTS** → 纯逻辑无法脱离 Unity 编译

**建议 10（强烈建议采纳）**：给 4 个业务 asmdef 加 `noEngineReferences: true`。
理由：**一分钟工作量，永久锁住分层边界**；当前边界是"今天干净"而非"结构上不可能脏"。
若不采纳的理由需说明，例如"某业务程序集确实需要 UnityEngine"。若确实需要，则应说明是哪一个、为什么，并考虑拆分。

---

## 第三部分 · 最难修复的四件事（供主控排期参考）

1. **`StreetSession` 状态集中**：193 行却有 **32 个状态位、14 个协作者、18 个方法**，单行多语句（`:64-69、134-141`）。现在抽 `RecoveryCoordinator` 很便宜；待多关卡/多失败类型/存档槽后，Tick↔Recover 组合会爆炸，需同改存档 DTO、音频代数语义与 6 组测试。

2. **`Generation` 被四处独立解读**：`UnityAudioPlayback.cs:242`、`StreetAudioController.cs:49/122`、`StreetPlayerController.cs:193` —— 全靠"只增不减"的约定对齐。**任一处允许重置即全盘失效。**

3. **双时间权威**：`World.time` 与 `Clock.Now` 各自累加，仅靠同一 step 序列保持逐位一致（`StreetWorld.cs:138`、`SimulationClock.cs:18`），而**存档只存前者却当时钟时间用**（`:79、135`）。现在合并便宜，越晚越贵。

4. **"相等"有四个定义**（渲染组）：`StreetGeometry.Epsilon=1e-5`（`:10`）/ `Clean` 用 `1e-7`（`:53`）/ `MergeEpsilon=Epsilon*2`（`StreetMemory.cs:9`）/ 共线判定用**精确 0**（`:400`）/ `TrySplit` 又用 `Epsilon`（`:333`）。**同一几何四层四义，改一层即破另一层假设。**

另：**覆盖几何表示契约不统一**（`StreetMemory.cs:205` 按任意凸片处理，`Model.cs:137` 注释只说 union of convex cells）、**Memory 无界碎片累积且已进存档 schema**（`StreetMemory.cs:643-654`、`StreetContracts.cs:176`）、**两代共存**三者亦属"现在改便宜、越晚越贵"。

**关于契约层的一个结构性观察（渲染组）**：`StreetGeometry` 只保证 **CCW + |面积|>1e-8**（`:17-18`），**不保证简单性**，而渲染端按**简单多边形**耳切；上游 `Subtract`/`HalfPlane`（`:31-36`）随时产出弱简单结果，且**失败是静默的**（无诊断、无计数）。在全黑画面下，缺片**没有任何可观测信号**——`ActiveSurfaceBatchCount`（`:118`）只数非空批次。

---

## 第四部分 · 建议清单（供主控逐条采纳/驳回）

| # | 建议 | 理由摘要 | 工作量 | 我的推荐 |
|---|---|---|---|---|
| 1 | 执行 `Build-StreetSpeechCache.ps1` 生成 86 条语料 | 骨架全就位，只差内容；决定盲人能否使用 | 低 | **强烈建议** |
| 2 | 错误文案改为固定文案或纳入语料 | 出问题时恰恰听不到，对全盲是最坏情况 | 低 | **强烈建议** |
| 3 | `StreetRenderer.cs:275` 清理挪到 split 之后 + 容差提到 `1e-3` | 5 行直击黑缝；**不采用会过绘的方案 2** | 极低 | **建议** |
| 4 | 统一"相等"的单一契约（见第三部分第 4 条） | 四层四义的容差是系统性风险 | 中 | 建议评估 |
| 5 | 给 4 个业务 asmdef 加 `noEngineReferences` | 一分钟，永久锁边界 | 极低 | **强烈建议** |
| 6 | `FinishSpeech` 加退避重试 | 现在整句提示会被永久丢弃 | 低 | **强烈建议** |
| 7 | `Tick` 加异常兜底 + 恢复帧保护 + 半状态检查 | 崩溃直穿入口；半状态可继续 | 低 | **建议** |
| 8 | 存档：不可解析版本号按未来处理 + Save/Load 对称 + 记录异常原因 | 防丢档；防"永久无法保存且无提示" | 低 | **建议** |
| 9 | 把"同一个量只有一处定义"作为整改原则，优先处理版本常量/角度上界/`MinimumArea`/信号声音 ID/材质属性名 | 后续 bug 的温床 | 中 | 建议 |
| 10 | Perception epsilon 错配（连带评估覆盖语义） | 黑缝的持续供给源；只修渲染是治标 | 中 | 建议评估后定 |
| 11 | 给 `StreetLevelFactory` 加构造期校验器（至少拦 R16 禁放项与点在多边形内） | 新关卡管线零校验 | 中 | 建议 |
| 12 | 在文档中标注"活代码 vs 历史代码"，**不立即删除** | A31 要求保留旧版；但维护者会读错 | 低 | 建议 |
| 13 | 把 `Generation` 统一为单一权威；合并双时间权威 | 趁存档未定版 | 中 | 建议排期 |
| 14 | 抽取 `RecoveryCoordinator`（在 `StreetSession` 撑爆前） | 32 状态位 God Object | 中 | 建议排期 |
| 15 | 渲染端增加"输入多边形面积 vs 输出三角形面积"自检 | 全黑下缺片无信号 | 中 | 建议评估（注意 signed-area 抵消污染） |

### 第四部分附表 · 渲染组补充证据（供建议 3 与建议 4 判断用）

**材质引用已实测无风险**：`StreetRenderPlan.cs:15` 的材质路径 + `StreetSurface.mat:11` 的 `m_Shader` GUID `0a71bdc0…` 与 `StreetSurface.shader.meta` **实测一致** → 21:30 的修复方向正确（Resources 显式引用，shader 必进包），**品红不会复发** ✅

**渲染端其余硬编码**：
| 项 | 位置 | 严重/难度 |
|---|---|---|
| 属性名字符串走 MaterialPropertyBlock，改名静默失效（建议改 `Shader.PropertyToID`） | `StreetRenderPlan.cs:178-182` | 中/易 |
| **Pattern 枚举 0-6 ↔ shader 阈值 0.5..5.5 ↔ mat `_Pattern:6` 三份真相** | `Plan:8-17` / `shader:63-93` / `mat` | 中/中 |
| `_UvScale 2.4` 三处重复；高度常量 `0.015/0.045/0.06` 被测试复刻 | `Plan:181`、`shader:8`、`mat:27` / `Plan:16-18`、测试 `72-84` | 低/易 |
| 层号 8 写死在调用点；**`StreetRenderer.cs:39` 与 `VoiceRenderer.cs:39` 争抢同一 camera 的 cullingMask/clearFlags**（后者生效者胜） | `StreetEntry.cs:35,55` | 中/中 |
| **`Shader.Find("Hidden/TingLu/VoiceLine")` 正是文档警告的写法**；`VoiceLine.mat` 实测不在 Resources；StreetEntry 路径不建 VoiceRenderer，`DemoEntry:57` 注入了材质 → **潜在雷（当前未触发）** | `VoiceRenderer.cs:42` | 中/中 |
| 每帧新建 plan/HashSet/stroke；`Triangulate` 每多边形每帧新建 ≥3 个 List；mesh 全量重建；**LineRenderer 池只增不减**；无规模上限 | `:156/273/293/351/359`、`:90-127`、`:206-226` | 中/中 |
| `65535` 阈值写死；**`indexFormat` 在已上传索引后切换的行为未验证**（推测）；`config` 是死字段 | `:110`、`:24/46` | 中/中·低/易 |

**测试失效的三段式**（解释为何 113/113 全绿仍有黑缝）：
1. 唯一回归测试 `StreetRendererTests.cs:92-138` 的输入是**从真实 183 顶点片"缩减"成的 8 顶点人工多边形**（`:101-104` 注释自认）
2. 断言用 `:136` `Mathf.Abs(StreetGeometry.Area(polygon))`，而 `Area` 是**有符号** shoelace，**弱简单多边形两瓣反向会抵消**（实测 hourglass shoelace=0）→ **该断言对这类输入恒成立**
3. 只查 `Mesh.triangles`，不查像素；`Triangulate` 是 `private`（`:271`），只能经 `Render` 间接喂几何

→ 缺"随机裁剪序列 × 面积守恒"属性测试，也缺**真实 183 顶点片原样入测**。

**建议 15 补充**：除面积自检外，建议增加**属性测试**（随机裁剪序列 × 面积守恒）与真实片回归。理由：当前测试结构**在原理上无法**发现此类丢片。

---

## 第五部分 · 决策记录表（请 00 填写）

**填写规则**：状态填 `采纳` / `部分采纳` / `驳回` / `延后`。**填「驳回」或「延后」时必须写理由**（这是用户明确要求）。理由请写事实与依据，不必写客套话。委派他人执行时填「执行角色」。

| # | 建议 | 状态 | 理由（驳回/延后必填） | 替代方案 | 复审触发条件 | 执行角色 |
|---|---|---|---|---|---|---|
| 1 | 生成 86 条语音语料 | | | | | |
| 2 | 错误文案改为固定/纳入语料 | | | | | |
| 3 | Renderer `:275` 清理挪到 split 之后 + 容差 `1e-3` | | | | | |
| 4 | 统一"相等"的单一契约 | | | | | |
| 5 | 4 个业务 asmdef 加 `noEngineReferences` | | | | | |
| 6 | `FinishSpeech` 加退避重试 | | | | | |
| 7 | `Tick` 异常兜底 + 恢复帧保护 + 半状态检查 | | | | | |
| 8 | 存档版本号解析 + Save/Load 对称 + 记录异常原因 | | | | | |
| 9 | "同一个量只有一处定义"整改（版本常量/角度上界/`MinimumArea`/信号声音 ID/材质属性名） | | | | | |
| 10 | Perception epsilon 错配（连带评估覆盖语义） | | | | | |
| 11 | `StreetLevelFactory` 加构造期校验器 | | | | | |
| 12 | 文档标注"活代码 vs 历史代码"（不立即删除） | | | | | |
| 13 | 统一 `Generation` 权威；合并双时间权威 | | | | | |
| 14 | 抽取 `RecoveryCoordinator` | | | | | |
| 15 | 渲染端面积自检 + 属性测试 + 真实片回归 | | | | | |

**记录位置建议**：本表就地填写，或另建 `docs/upgrade/ARCHITECTURE_ADVICE_DECISIONS.md` 并在此处留链接。
**为什么不建议只在聊天里回**：本项目已有"动态提示词未及时落盘"被记为流程缺口的先例；建议的驳回理由若不落盘，后续接手者无法区分"没想到"与"想过但不做"。

---

## 附件一 · 审查范围与诚实声明

---

## 附件一 · 审查范围与诚实声明

| 组 | 范围 | 实际读取 |
|---|---|---|
| A 契约与模拟内核 | Contracts(5文件678)、Config(2文件54)、StreetPerception(645)、StreetMemory(718)、StreetWorld(742)、StreetPlayerController(338) | 全文 + 6 个 asmdef + grep 取证 |
| B 装配与持久化 | StreetSession(193)、StreetEntry(96)、Bootstrap/Core、StreetSaveService(583)、AccessibleMenu(316)、StreetHudPresenter(137)、StreetConfig、StreetScreenshotProbe(282) | 全文；为验证恢复链另读 StreetPlayerController/ThreatReceiptGate/StreetAudioController/StreetLevel/LocalSaveService/GameplayConfig |
| C 音频与关卡内容 | StreetAcousticModel(142)、StreetAudioController(251)、UnityAudioPlayback(**1009**)、StreetLevel(234)、StreetCrossroads.unity(47)、UnitySpeechSelectionTests(109)、Build-StreetSpeechCache.ps1 | 全文；部分读 StreetAcousticModelTests/StreetWorld(440-569)/Model(80-134)/StreetSession(130-193) |
| D 渲染与表现层 | StreetRenderPlan(272)、StreetRenderer(504)、StreetCameraRig(120)、StreetSurface.shader(104)、StreetSurface.mat(30)、Role08 两测试全文、StreetGeometry(55)、StreetMemory 部分、StreetPerception(375-454)、VoiceRenderer(20-99)、StreetEntry(15-94) | 全文 + 全工程 grep；**未读** StreetHudPresenter |

**未覆盖**：编辑器窗口、旧版 FirstStep 关卡内容、人工听感、实机画面、无障碍体验、设备适配。
**未复核项**：453 次 `voice_limit_no_lower_priority_voice` 告警（`Logs/` 仅有 shadercompiler 日志；相关结论标为**推断**）。
**本报告未运行 Unity、未修改任何文件**；所有结论均为静态审查，可能存在的实测行为差异以实机证据为准。
**不改变任何既有结论**：`ACCEPTANCE.md` 的 A01–A31 状态、`COMPLIANCE.md` 的 A32–A34、`RECHECK_AUDIT` 与本报告互不替代。

## 附件二 · 相关文件

- `docs/qa/upgrade/ARCHITECTURE_AUDIT_2026-10-16.md` —— 同一批审查的详细版（含完整行号证据）
- `docs/qa/upgrade/RECHECK_AUDIT_2026-10-15.md` —— 项目内 12 角色运行链复核
- `docs/qa/upgrade/ACCEPTANCE.md` / `COMPLIANCE.md` —— 官方验收记录
