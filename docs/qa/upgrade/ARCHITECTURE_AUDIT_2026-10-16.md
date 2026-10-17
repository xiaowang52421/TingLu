# 街区升级架构审查报告（只读）

日期：2026-10-16。审查方式：**4 个独立只读审查员**并行深读，未修改任何生产文件、未运行 Unity。
本报告由**第三方审查**（非项目内 00/12 角色）产出，不替代 `ACCEPTANCE.md` / `COMPLIANCE.md` / `RECHECK_AUDIT_*.md`。

## 审查分组与读取范围（诚实声明）

| 组 | 范围 | 实际读取 |
|---|---|---|
| A 契约与模拟内核 | Contracts(5文件678)、Config(2文件54)、StreetPerception(645)、StreetMemory(718)、StreetWorld(742)、StreetPlayerController(338) | 全文 + 6 个 asmdef + grep 取证 |
| B 装配与持久化 | StreetSession(193)、StreetEntry(96)、Bootstrap/Core、StreetSaveService(583)、AccessibleMenu(316)、StreetHudPresenter(137)、StreetConfig、StreetScreenshotProbe(282) | 全文；为验证恢复链另读 StreetPlayerController/ThreatReceiptGate/StreetAudioController/StreetLevel/LocalSaveService/GameplayConfig |
| C 音频与关卡内容 | StreetAcousticModel(142)、StreetAudioController(251)、UnityAudioPlayback(**1009**)、StreetLevel(234)、StreetCrossroads.unity(47)、UnitySpeechSelectionTests(109)、Build-StreetSpeechCache.ps1 | 全文；部分读 StreetAcousticModelTests/StreetWorld(440-569)/Model(80-134)/StreetSession(130-193) |
| D 渲染与表现层 | StreetRenderer/StreetRenderPlan/StreetCameraRig/StreetSurface.shader/Role08测试 | **本轮尚未回传**（见文末待补） |

**未评价**：编辑器窗口、旧版 FirstStep 关卡内容、人工听感与实机体验。

---

## 一、总评

| 层 | 评价 |
|---|---|
| 契约 / 纯逻辑内核 | ★★★★★ 不可变 DTO、编译期强制零依赖、构造期拒绝非法状态、用依赖方向守住泄露边界 |
| 装配 / 生命周期 | ★★★ 可用，但 `StreetSession` 为 32 状态位 God Object，恢复事务无保护 |
| 内容与资产管线 | ★★ 最薄弱：两套关卡管线、语音缓存空转、无校验器 |
| 跨层不变量 | ★★ 系统性缺失：同一概念在 2–4 处独立定义，靠"约定"对齐 |

**核心判断**：地基是本项目最值钱的资产；但**"代码写得好"与"东西真能用"之间存在背离**，最典型证据是语音管线——运行时代码、生成脚本、hash 契约、测试全部齐备，唯独没有音频文件，导致效果归零且静默。

---

## 二、结构设计：做得好的部分（建议保持，不要动）

1. **不可变性 + 构造期校验**贯穿全项目。`ContractGuard` 统一校验 ID/时间/NaN/Inf，非法状态**构造不出来**。
2. **零 Unity 依赖是编译期强制的**：`TingLu.Contracts.asmdef:5`、`TingLu.Config.asmdef:5` 开启 `noEngineReferences: true`。
3. **确定性到位**：显式排序全覆盖（`StreetWorld.cs:74` `objectIds.Sort(Ordinal)`、`StreetMemory.cs:622-633` 四元组比较、`StreetPerception.cs:160`、材质按 Priority+Ordinal 排 `:372-376`）；字典全 `StringComparer.Ordinal`；注入 `IGameClock`（`StreetPerception.cs:24`、`StreetPlayerController.cs:23`）；生产代码**无** `DateTime`/`UnityEngine.Random`/`Debug`。
4. **`D04`/`R14` 在契约层被结构强制**：`Model.cs:124-125` 直接抛异常（仅声音不得带轮廓、被动声只能是 SoundLocation）；`StreetRenderPlan.cs:111` 只渲染 LocalGeometry+Static；`StaticMemoryStore.cs:26` 排除 SoundLocation。
5. **纯 sim-time 计时让"暂停冻结一切"结构性成立**：`SimulationClock.cs:18` + 绝对时间戳，无需在车辆/信号/冷却/探测各处逐点判断。
6. **恢复顺序的关键细节写对了**：`SetPaused→Generation++→audio.Reset→playback.Reset`（`StreetSession.cs:134`）→`ImportTraffic+Clock.Reset`（`:135`）→`Restore`（`:136`）→`MarkStale+Clear`（`:137`）→**取消暂停后**才 `RefreshSenses`（`:138`）。取消暂停必须先于 RefreshSenses，否则 `ProjectAcoustics` 在暂停态返回空观测（`StreetPerception.cs:98-111`）。
7. **存档可靠性基础扎实**：校验完整（`:256-263`），未来版本判定先于 checksum（`:252`）；`Flush(true)` 临时文件（`:475-482`）+ `File.Replace` 轮转备份（`:179`）；损坏时保留好备份（`:166-171`）；命名空间真隔离（街道 `TingLuStreetUpgrade/` vs 试玩 `TingLu/`）；**无写死绝对路径、无 ProductName 依赖**；浮点字符串化全 `InvariantCulture`（`:471、504、514、523`）。
8. **几何层"宁可抛异常也不静默出错"**：`ConvexPieces` 遇不可三角化的自交多边形直接抛（`StreetPerception.cs:463`），零面积/退化处处拦（`:394、405、512、619`）。
9. **诊断探针不污染生产**：`StreetScreenshotProbe.cs` 整文件 `#if DEVELOPMENT_BUILD||UNITY_EDITOR`（`:1`），无参数时 `:56` 直接 return，且拒绝写 `persistentDataPath`。
10. **未发现任何 `if(test)` 类特例分支或"为通过测试而写死"的代码。**

---

## 三、P0：语音管线空转（本报告头条）

**「随包预生成语音缓存」是空转半成品——代码全好，内容为空。**

| 部分 | 状态 |
|---|---|
| 运行时 `StreetSpeechCache.TryLoad` | 已就位（`UnityAudioPlayback.cs:668-683`） |
| 生成脚本 `tools/upgrade/Build-StreetSpeechCache.ps1` | 已就位 |
| hash 契约 | **两边完全一致**（脚本 `:45-52` 与 `:656-666` 均 SHA256(UTF-8) hex；脚本写 `AudioAssets/Resources/TingLuSpeech` `:139-141`，运行时读 `"TingLuSpeech/"+hash` `:653,673`） |
| **实际音频文件** | **`Assets` 下 0 个 wav/mp3；`AudioAssets` 仅一份 `StreetAudioPlaceholderManifest.txt`** |

**后果链**：缓存 100% miss → 动态合成路径接管 → **低完整性 Player 下必然全静音**。
`UnitySpeechSelectionTests.cs:63-83` 断言缓存可加载，**该测试当前为红**。

### 3.1 错误提示永远发不出声

动态错误文案为**运行期拼串**（`StreetSession.cs:143,162,176`），**永不在预生成语料内** → Player 中必然静音。
失败仅由 `StreetEntry.cs:54` 的 `Debug.LogWarning` 接住。**对全盲玩家即"出问题时什么都听不到"。**

### 3.2 这不是偶发，是目标环境的结构性必然

低完整性 Player 下四条路径全部被阻断（已由 `output/street-verification/20261016-003323-speech-context/` 日志证实）：

| 路径 | 阻断原因 |
|---|---|
| `System.Speech` | 桌面 .NET Framework 程序集，Player 默认不带 → `assembly not found` |
| SAPI ProgID COM | 低完整性禁用 → `Unmanaged activation is not supported` |
| PowerShell fallback | `WindowsPowerShell\v1.0` 硬路径（`:888-891`） |
| 非 Windows | **无任何回退**（`:710-711`） |

**架构结论**：把核心无障碍通道**外包给用户机器的组件完整性、已装音色与沙箱策略**（同机 medium 可用、low 不可用即证据）。
**正确形态：随包 WAV 为唯一保证路径，动态合成仅作可选增强。**

### 3.3 相关缺陷

| 问题 | 位置 | 严重/难度 |
|---|---|---|
| **文案双份真相**：语料硬编码于 PS `:12-33` 与 C#（`StreetLevel.cs:127`、`StreetSession.cs:139/180`）两处；hash 由文案决定 → **改一字即 miss 即静音**；仅"开始新游戏"一条有测试守 | — | 高/中 |
| **`FinishSpeech` 池满时只 Emit Failed、不重试** → **整句提示永久丢弃** | `UnityAudioPlayback.cs:504-510` | 高/易-中 |
| **菜单语音代数靠隐式包装**：`AccessibleMenu.generation` 只在 `Initialize` 赋一次（`AccessibleMenu.cs:97`），全靠 `StreetSession.cs:187` 重写代数才不被按 stale 丢弃（`UnityAudioPlayback.cs:217-221`）→ 任何传裸 `IAudioSpeech` 的调用（Role11 测试即如此）会让菜单语音**静默全失** | — | 高/易 |
| **普通碰撞无即时声反馈**：Session 只请求 `cane.tap`/`footstep.*`/`feedback.checkpoint`（`StreetSession.cs:70,74,117`）；`collision.bump`/`feedback.breath` 仅旧 `DemoSession` 使用（`DemoSession.cs:255,143`）→ 对全盲玩家"掉血无提示"（间接发声路径未追踪，**推测**） | — | 高/易 |
| meta 由脚本手写（`:69-92`）：`loadType:0`(DecompressOnLoad)+`preloadAudioData:1`+Vorbis，86 条语料（`:12-33`）**全部启动解码常驻**，`TryLoad`(`:673-680`) 每句再拷一份 `float[]` | — | 内存量级需实测 |
| `Provider` 硬写 `Huihui`（与实际音色无关） | `:654` | 低 |

---

## 四、P1：模拟内核缺陷

### 4.1 径向黑缝的上游供给源（重要）

**两个 epsilon 的数量级关系是错的**：

```
VertexRayOffsetDegrees   = 1e-4   (StreetPerception.cs:16)
AngleMergeEpsilonDegrees = 1e-6   (StreetPerception.cs:17)
```

`:191-195`、`:248-250` 对**每个可见顶点**必插 `angle`、`angle±1e-4` 三个角 → **每个可见顶点必然产出 2 个角宽 1e-4° 的细长凸片**，其三顶点（原点+3m+7m，夹角 1.745e-6 rad）**近乎共线**。面积 = 0.5·r²·sin(1e-4°)，r=7 时约 **4.3e-5 m²**，为 `MinimumCellArea=1e-8`（`:19`）的 **4000 倍** → **任何面积过滤都拦不住**。

**不可靠调大 `MinimumCellArea` 修复**——调大反而丢真实几何。真正错配是两个 epsilon 的关系。

**第二供给源（Memory）**：`TryMerge`（`StreetMemory.cs:458-480`）合并时只按距离去重（`:484`），**不删接缝共线点**；`EdgeKey` 量化后要求端点完全相等（`:696-704`），**T 型接缝根本合不上** → 送往渲染层的多边形**带 180° 顶点与未合并 T 接缝**。

**第三处**：`StreetGeometry.CopyPolygon` 只查总面积，**从不删共线顶点**（`StreetGeometry.cs:18`）→ 关卡多顶点路缘原封不动进入事件插入（每顶点 3 角）、`NearestBlockerDistance`（每射线 O(V)）与 Memory 逐 cell 裁剪；一次探测 O(角数×V)，Memory 再乘凸片数。`RemoveCollinearVertices` **已存在但为 private 且只用于面**。

**纠偏（防止查错方向）**：`AddAdaptiveCell` 在偏差不过时确实丢弃跨度（`StreetPerception.cs:274`），存在"无覆盖"路径，但每层折半，丢的是约 1e-4°/2¹⁸ 的角度，**面积可忽略，不是可见缺片的根因**。

### 4.2 覆盖几何的表示契约不统一

隐式约定是"从原点出发的三角扇 + 1e-4° 侧翼"，但 Memory 按"任意凸片"处理（`StreetMemory.cs:205`），Contracts 注释只说是 union of convex cells（`Model.cs:137`）——**三方理解不一致**：共线/共径向/微缺片谁都不负责。
波及 `StreetPerception.cs:145-284`、`StreetGeometry.cs:23-47`、`StreetMemory.cs:402-489` 与渲染层。**现在改便宜（语义未序列化）**。

### 4.3 Memory 无界碎片累积（已进存档 schema）

`SurfaceKey` 把 `ObservedAt`/`CurrentUntil` 当作**身份的一部分**（`StreetMemory.cs:643-654`）→ 跨探测**永不合并**；"永久探索保留"因此单调增长；`MergeAdjacentConvex` 每次全量重扫（`:402-455`，每合并一次就从头再来）。
**已写入 `StreetSaveData.Memory`（`StreetContracts.cs:176`）→ 晚改须动存档 schema。**

### 4.4 恢复事务与生命周期

| 问题 | 位置 | 说明 |
|---|---|---|
| **`Tick` 无 try/catch** | `StreetSession.cs:93-109` | 异常直穿 `StreetEntry.Update` |
| **恢复留半状态** | `:144` vs `:155` | finally 置 `recovering=false`，`Resume` 不检查上次恢复是否失败 → 可能在"车流已回滚、玩家未回滚"下继续 |
| **无无敌帧** | `:190-193` | `Restore` 清空 `vehicleContacts` 且代数++；检查点落在车道上会连续恢复。两检查点位于 x=±5.5 人行道（`StreetLevel.cs:56-57`），**实测风险低** |
| **checkpointPlayer 为合成值** | `:116` | "检查点圆心+检查点朝向"，**非玩家当时真实位姿** |
| **ReturnToMenu 后对象仍存活** | `:170` | World/Memory/perceptionService |
| delta 夹取只在 Entry | `StreetEntry.cs:69` | 夹到 0.1；非 Unity 调用方可传任意大 delta |

**已确认成立**：固定子步正确（n=⌈delta/0.02⌉，step=delta/n，`:99`）；`Player.Tick` 以 `recoveryGeneration` 快照在 Stepped/Probed/Contacted/Failed 四个回调后逐点复查（`StreetPlayerController.cs:129、174、236、275`）→ **未发现"一子步双处理"或跨子步漏检**；一次事故一命中靠 `vehicleContacts`(HashSet by ObjectId) + `failedGeneration==recoveryGeneration` 双层（`:229、237-239、279-280`），同帧多车在首个 true 处退出（`:148、160`），暂停时 Tick 提前 return（`:59`）→ **成立**；**D15 成立**：`MarkStale` 只清 `CurrentUntil`、保留几何（`StreetMemory.cs:137-150`）。

---

## 五、P1：存档真漏洞

| 问题 | 位置 | 严重/难度 |
|---|---|---|
| **未来版本号不可解析即被判 Corrupt → 覆盖并隔离**。`IsFutureContract` 在版本串非 `x.y.z` 时返回 false（`:533、550-558`），故 `"2.0.0-beta"` 会被覆盖隔离 —— **正是 R01/R20 要防的丢字段场景** | — | 中/易 |
| **Save/Load 对"备份是未来版本"处理不对称**：Save 见即拒绝（`:137`），Load 因主档成功而返回（`:215`）→ **玩家从此永久无法保存，且无任何提示** | — | 中/易 |
| **`Save` 的 catch 吞掉全部异常**，只回 `IoFailure` 且不记录原因 | `:185-188` | 中/易 |
| **`.corrupt.*` 只增不删且无人读** | `:467-473` | 低 |
| lock 仅进程内 | — | 低 |
| **历史问题已修**：主档 Missing + 备份 Corrupt 在 `:143-144` 返回 Corrupt，不再写新主档；回归测试 `StreetSaveServiceTests.cs:140` | — | ✅ |

---

## 六、硬编码与脆弱点汇总

### 高 / 易
- 菜单语音代数隐式包装（见 3.3）
- 普通碰撞无即时声反馈（见 3.3）
- 缓存内容缺失（见第三节）
- **信号声音 ID 重复真相**：`"street.signal.ns"/"ew"` 写死于 World，与 `StreetCatalog.Sounds` 重复（`StreetWorld.cs:471-472` vs `StreetContracts.cs:34`）

### 中 / 易
- **两个版本常量并存，无法判断谁权威**：`StreetContract.Version="1.0.0"`（`StreetContracts.cs:7`）vs `ContractVersion.Current="0.2.0"`（`Model.cs:7`）
- **配置校验与 DTO 不变量不一致**：`*_Angle` 只拒 >360（**=360 通过**），而 `StreetProbeRequest` 要求 angle**<360**（`GameplayConfig.cs:28` vs `StreetContracts.cs:120`）
- `MinimumArea=1e-8` 在 Memory 与 `MinimumCellArea` **同义不同名重复**（`StreetMemory.cs:10`）
- Perception 7 个常量全在实现里：`ArcStepDegrees=1`、`MaxAdaptiveDepth=18`、各 epsilon（`StreetPerception.cs:15-21`）
- World 的 `ContactSkin=0.002`、`pass<4`、`iterations>100000`、`radius+0.0001f`（`StreetWorld.cs:17、153、348、631`）
- `FeedbackPriority.Navigation` 从不使用 → `voiceprintIntensity` 对街景源无效（`UnityAudioPlayback.cs:584`）
- **遮挡射线不读 `blocker.Height`** → 矮障碍只要配 loss 就挡声（`:106-133`）
- **`Occlusion` 是死参数**：存了但 `EffectiveVolume` 不用（`:582-586`；已并入 Discernibility，故不致命）
- **`FinishSpeech` 丢句不重试**（`:504-510`）
- **暂停菜单"从路口重来"实为回检查点**，且**无真正重开关卡入口 → 永久记忆不可重置**（`AccessibleMenu.cs:251` vs `StreetSession.cs:164-167`）
- 脚步间隔 `FootstepInterval=0.72f` 是玩法数值却不在 `GameplayConfig`（`StreetPlayerController.cs:12`）

### 中 / 中
- **`GameplayConfig` 约 15 个旋钮无人读取**，其中 **`perceptionHz` 宣称的"感知限频 20Hz"根本没实现**（`StreetSession.cs:106` 每子步全量重算）→ **配置谎报能力**
- **`StreetConfig.Validate` 用反射 + 字段名前缀豁免**（`StartsWith("CameraTargetOffset")`，`StreetConfig.cs:18-19`）→ 今后**任何新增 float 字段默认 0 都会让配置直接抛异常**
- **池 12 偏小** vs 约 20 个源，且同优先级不能互抢（后端 `:358` 仅"严格更低"可被抢占）
- **Save 在子步内同步落盘**（`StreetSession.cs:117、139`）
- 街景源只用到 2 级优先级（车辆 `CriticalThreat`、其余全 `Ambience`，`StreetAudioController.cs:171-173`）

### 低
- `1<<8` 图层写死两次（`StreetEntry.cs:35、55`）；`Wrap(...,3)` 写死 3 种显示模式（`AccessibleMenu.cs:46`）；"现在还没有存档"对所有禁用项通用（`:268`）
- `maxDistance=26`、采样率 `22050`、`duck 0.28`、超时 `20000ms`、`referenceDistance=radius*0.18`
- 硬写字符串：`"TingLuSpeech/"`（`:653`）、Provider `Huihui`（`:654`）、`"speech_"` 前缀协议（`:541`）、env 名（`:589`）、PS 片段（`:957-977`）
- **9 个 id 不在目录里**（`footstep.brick/metal/cardboard` 等），会被 `StreetAcousticModel.cs:51` **静默过滤**
- 唯一全局状态：`TINGLU_STREET_SAVE_ROOT` 环境变量（`StreetEntry.cs:43` ← `StreetScreenshotProbe.cs:50`）；生产路径无 `DontDestroyOnLoad`/`FindObjectOfType`

### 死代码 / 测试钩子
- **`ThreatReceiptGate` 全项目无生产调用点**
- `MoveObject`/`RemoveObject` 是**生产程序集里的测试钩子**（`StreetWorld.cs:324-339`，有异常守卫）
- **对"18 个 soundId 只有 6 个有映射"的旧结论已过时**：`PlaceholderBank`（`:600-627`）已覆盖全部 18 个 soundId + 10 个材质 `footstep.*`，`StreetAcousticModelTests.cs:351-362` 守住 ✅

### 两代系统共存
- **升级新增生产文件 23 个；旧版遗留仍参与编译进构建的 32 个文件（约 6200 行）**
- 同一程序集内两套实现并存：`PlayerController`/`StreetPlayerController`、`PerceptionService`/`StreetPerception`、`StaticMemoryStore`/`StreetMemory`、`WorldRegistry`/`StreetWorld`、`PresentationFrameComposer`/`StreetRenderer`、`AcousticModel`/`StreetAcousticModel`、`LocalSaveService`/`StreetSaveService`、`UnifiedInputSource`/`StreetInputSampler`、`AccessibleMenuPanel`/`AccessibleMenu`
- **旧入口未隔离**：`DemoEntry` 是 `public sealed class DemoEntry : MonoBehaviour`，**无 `#if` 保护**，仍被 6 个旧程序集引用；`FirstStep.unity` 仍在
- **`Geometry2.cs` 与 `StreetGeometry.cs` 两套几何工具并存**
- **两套关卡管线并存**：`LevelDocument`+`LevelCompiler`+**`LevelValidator`**+JSON 数据（旧）vs `StreetLevelFactory` 代码字面量（新）。**新管线完全不经过 `LevelValidator`**
- **`StreetConfig` 被两代共用**；**legacy 还被新测试反向锁定**：Role02 测试断言 `listenSpeed` 必须无效（`StreetPlayerControllerTests.cs:147-160`）
- 同一概念两套调参源：`ProbeAngle`（`StreetConfig.cs:9`）vs `walkAngle/listenAngle`（`GameplayConfig.cs:8`）

### 编译期强制力不完整（低成本高收益）
- `Contracts`/`Config` 已开 `noEngineReferences: true` = **编译期强制**
- **`Perception`/`Memory`/`World`/`Player` 4 个 asmdef 未开** → 边界**只靠纪律**；已 grep 确认今日无一处 `UnityEngine/Time/Mathf/Random/Debug`，**但加一行即可破**
- **`TingLu.Audio` 同时装纯模型 + 1009 行播放器 + TTS** → 纯逻辑无法脱离 Unity 编译

---

## 七、最难修复的四件事

1. **`StreetSession` 状态集中**：193 行却有 **32 个状态位、14 个协作者、18 个方法**。现在抽 `RecoveryCoordinator` 很便宜；待多关卡/多失败类型/存档槽后，Tick↔Recover 组合会爆炸，需同改存档 DTO、音频代数语义与 6 组测试。
2. **`Generation` 被四处独立解读**（`UnityAudioPlayback.cs:242`、`StreetAudioController.cs:49/122`、`StreetPlayerController.cs:193`），全靠"只增不减"约定对齐；**任一处允许重置即全盘失效**。
3. **双时间权威**：`World.time` 与 `Clock.Now` 各自累加，仅靠同一 step 序列保持逐位一致（`StreetWorld.cs:138`、`SimulationClock.cs:18`），而**存档只存前者却当时钟时间用**（`:79、135`）。现在合并便宜，越晚越贵。
4. **检查点＝圆心 + 合成 `PlayerState`**（`:116`）：既是"无帧保护也不炸"的隐含前提，也是关卡编辑器一旦开始生成检查点就会暴露的设计债。

另：**覆盖几何表示契约**（4.2）、**Memory 无界碎片已进 schema**（4.3）、**两代共存**（第六节）三者亦属"现在改便宜、越晚越贵"。

---

## 八、建议优先级（投入产出比排序）

| # | 动作 | 理由 | 预估 |
|---|---|---|---|
| 1 | **生成 86 条语音语料**（跑 `Build-StreetSpeechCache.ps1`） | 骨架、hash 契约、加载、测试全就位，**只差内容**；直接决定盲人能否使用 | 低 |
| 2 | **`FinishSpeech` 加退避重试** | 现在整句提示会被永久丢弃 | 低 |
| 3 | **给 4 个业务 asmdef 加 `noEngineReferences`** | 一分钟，永久锁住分层边界 | 极低 |
| 4 | **文案单一真相 + 构建期语料覆盖校验 + 启动自检 + 缺失时可见失败** | 消除"改一字即静音"与"静默全失" | 中 |
| 5 | **修 Perception epsilon 错配**（连带评估覆盖语义，勿只调 `MinimumCellArea`） | 径向黑缝源头 | 中 |
| 6 | **统一 `Generation` 解读 / 合并双时间权威** | 趁存档未定版 | 中 |
| 7 | **给 `StreetLevelFactory` 加构造期校验器**（至少拦 R16 禁放项与点在多边形内） | 新管线零校验 | 中 |
| 8 | **补未来版本号解析与 Save/Load 对称性** | `"2.0.0-beta"` 丢字段；永久无法保存无提示 | 低 |
| 9 | **`Tick` 加异常兜底；恢复加帧保护与半状态检查** | 崩溃直穿入口；半状态继续 | 低 |
| 10 | **清理两代共存**（需先确认旧路径无引用，注意 A31 要求旧版保留作回归） | 6200 行死代码误导后续维护者 | 中 |

---

## 九、待补

- **渲染与表现层审查（D 组）尚未回传**：`StreetRenderer.cs` 三角化退化（共线顶点被拒）的最后一环、`Shader.Find` vs 显式引用的 Player 失效风险、mesh 复用/GC、`StreetRenderPlan` 泄露后门复核。
- 本报告**未运行 Unity、未改任何文件**，所有结论均为静态审查；人工听感、实机画面、设备与无障碍体验仍属 `NOT_RUN`。
- 已知未复核项：453 次 `voice_limit_no_lower_priority_voice` 告警（`Logs/` 仅有 shadercompiler 日志，无法亲自复核，相关结论标为推断）。
