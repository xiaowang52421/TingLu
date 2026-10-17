# 第三方审查建议汇总（第二轮）

日期：2026-10-16
性质：**建议书，非指令**。本文件由第三方只读审查产出，供 00 主控决策参考。
**可采纳、可部分采纳、可不采纳；不采纳的条目请在下方决策表填写理由。**
本文件不修改任何生产代码，不替代 `ACCEPTANCE.md` / `COMPLIANCE.md` / `RECHECK_AUDIT_*` / `ARCHITECTURE_ADVICE_DECISIONS.md`。

## 审查方式与边界

- 只读审查。**未运行 Unity、未运行测试、未采集 Profiler、未修改任何文件。**
- 覆盖范围：生产源码、共享契约与几何、验证工具链、`docs/` 与 `docs/qa/` 记录、`output/` 下的验证产物与截图。
- **未覆盖**：编辑器窗口内部行为、旧关卡内容逐项核对、设备音频输出、无障碍体验。
- 所有结论均为静态审查 + 既有证据复核；**性能数字全部注明来源与口径**。
- 与既有报告的关系：本文件是 `ARCHITECTURE_ADVICE_2026-10-16.md`（第一轮 15 项）之后的**增量建议**，不重复已采纳且已落实的条目。

---

# 第零部分 · 给主控的三句话摘要

1. **性能阻塞已解除且已量化**：最慢探针 Perception 752 ms（Memory 未跑完）→ **max 14.5 ms**；`Memory.Apply` 259 ms → **max 10.7 ms**（来源：`20261016-030510-2781e2d0` 的 `probe-performance-*.json`）。**这一条已闭环，不需要再行动。**
2. **当前最值得做的单件事**：把剩余阶段（PlayMode / BuildWindows / Player / 语音 Started）**在同一指纹上跑完**。目前 EditMode 161/161 已带指纹 `CAD6588…`，其余仍缺。
3. **其余建议里，有 3 条属于"会反复咬人"的类型**：`StreetMemory.Apply` 无事务性（B1）、验证工具清单的三个硬化点未落地（E1–E3）、`ACCEPTANCE.md` 结论过时（F1）。这三条成本都很低，但会持续污染后续判断。

---

# 第一部分 · 已经解决或已确认无问题的（不建议再动）

记录在此，避免重复投入。

| # | 项 | 现状 |
|---|---|---|
| A1 | 探针性能阻塞 | ✅ **已解决**：Perception max 14.5 ms、p95 14.51 ms；`Memory.Apply` max 10.7 ms。13 次连续探针全程跑完。 |
| A2 | "先合并后差集"的错误优化 | ✅ **已被回归拦下并撤回**；静止重复探测 8 次面积恒为 `164.2862`（小数点后第 5 位才动），**证明未引入重叠**。 |
| A3 | 静止玩家被车穿过不命中 | ✅ 根因（`radius + DistanceEpsilon` 与 `radius` 不一致造成的浮点边界缝 + `activeVehicleContacts` 去重放大）已定位并修；before/after 复现证据在案。 |
| A4 | `Area()` 浮点灾难性抵消 | ✅ 已修为先平移 + double 累加，**单位与阈值未变**，未放宽 `CopyPolygon` 守卫。 |
| A5 | 历史 Player 假 PASS（顶层 PASS + `speechBackend=FAIL`） | ✅ 验证脚本已加语音门（非 `STARTED` 即 `throw`），**该缺陷已在工具层堵住**。 |
| A6 | 语音语料缺失 | ✅ 已生成 89 条（以 `manifest.json` 条目为准）。 |
| A7 | `noEngineReferences` 编译期锁边界 | ✅ Player/World/Memory/Perception 四份 asmdef 均已设置。 |
| A8 | 渲染 Surface 缓存 | ✅ 已实现，含完整失效规则 + 异常清理 + `SurfaceBatchRebuildCount` 可观测计数。 |
| A9 | 三阶段同指纹验证机制 | ✅ `sourceFingerprint` + 运行前后双取 manifest 已落地并实测有效。 |

---

# 第二部分 · 建议清单

编号规则：字母为类别，数字为序号，便于在决策表中逐条引用。

## A. 几何与数值（共享契约层）

### A-A1 · 为 `Area()` 数值修复补一条"远离原点薄片"回归 ⭐
- **现状**：`StreetGeometry.Area()` 已修为平移 + double。此前的 `Degenerate polygon` 异常正是"薄片远离原点导致面积被抵消成 0"。
- **建议**：在 Role00 契约测试中加一条固定用例——**多边形质心远离原点（如 x≈1000）且极薄**，断言面积非零且与解析值一致。
- **理由**：现有测试用的是原点附近的小矩形，**测不出这个类别的缺陷**。这条回归成本极低，但能锁住一整类问题。
- **成本**：极低。

### A-A2 · 为四个 epsilon 写"单位与误差预算"注释表（不合并数值）
- **现状**：`StreetGeometry.Epsilon=1e-5`、`Clean` 用 `1e-7`、`MergeEpsilon=Epsilon*2`、共线判定用**精确 0**、`TrySplit` 又用 `Epsilon`。四层四义。
- **建议**：**不要把它们统一成一个数**（量纲不同，主控此前的反驳成立），而是在定义处加一张小表，逐条写明：**量纲（长度/角度/面积）、用途、对应代码位置、误差预算**。
- **理由**：当前风险不是"数值不对"，而是**改一处不知会破坏哪一层假设**。文档化后，后续任何调整都能自查影响面。
- **成本**：低（纯注释）。

### A-A3 · Perception 的 `VertexRayOffsetDegrees` / `AngleMergeEpsilonDegrees` 维持暂缓，但补证据
- **现状**：`1e-4` 比 `1e-6` 大 100 倍，每个可见顶点必然产出 2 个极窄径向凸片。主控判定"offset > merge epsilon 本身不足以证明错误"，选择暂缓——**这个判断在方法上成立**。
- **建议**：维持暂缓，但把**需要的证据类型**写进待办：一个**遮挡反例**（细片导致可见性边界出错的具体场景）+ 一份**覆盖对照**（改 offset 前后 `coverageArea` / `observedArea` 的差集）。
- **理由**：避免这条变成"永久悬空"。有了具体的证据要求，它就是一个可关闭的条目。
- **成本**：中（取证本身）。

---

## B. 记忆层正确性（`StreetMemory`）

### B-B1 · `StreetMemory.Apply` 缺事务性 —— 建议加"失败即回滚" ⭐⭐
- **证据**：`StreetMemory.Apply` **先清空/替换内部 surface 列表**，之后才合并 object 并重建公开 `Current`。**若合并中途异常，公开快照仍是旧值，而内部列表已部分变更。** 故障锁会阻止直接 Save，但 **Retry 路径的 `MarkStale` 会从现存内部列表重建并保存，不能保证回滚到故障前记忆。**（来源：`UPGRADE_ARCHITECTURE_REVIEW.md:47`）
- **影响**：直接威胁 R10「永久探索持续保留」——**一次中途异常可能把半合并的记忆持久化**。
- **建议（择一）**：
  1. `Apply` 在临时结构上完成全部合并，**成功后才一次性提交**到内部列表与 `Current`；
  2. 或失败时**回载最后一次良好快照**。
  并补一条**注入故障验证**（在合并中途抛错，断言内部列表与 `Current` 均等于故障前）。
- **理由**：这是当前记忆层唯一被明确记录的事务性缺口，且它影响的是**不可逆的存档内容**。
- **成本**：中。

### B-B2 · 静止重复探测每次 +5 表面、面积零增长 —— 建议记入待办并设观察阈值
- **证据**：`probe-performance` 的 8 次 `stationary-repeat`：`memorySurfacesBefore/After` 为 206→229、229→234、234→239、239→244、244→249、249→254、254→259、259→264；而 `memoryArea` 恒为 `164.2862…`。
- **判读**：**每次重复探测稳定 +5 片、面积零增长**。这不是之前那种爆炸（317→19,502），但说明"同一已探索区域被反复探测"会累积**零面积增量的重复凸片**，随敲杖次数线性增长。
- **建议**：不紧急处理，但**记入待办并设阈值**（例如：静止重复 20 次后 surface 增量应趋近 0；或设定"surface 数 / 已探索面积"的上限比）。若超阈值再处理。
- **可能的根因方向**（供参考，不要求本轮改）：`SurfaceKey` 把 `ObservedAt` / `CurrentUntil` 纳入身份，导致跨观察永不合并。
- **成本**：低（先设观察阈值）；改则中。

### B-B3 · `cane.tap` 在探测结果提交前播放 —— 建议明确语义或调整顺序
- **证据**：`StreetSession` **先提交通用 `cane.tap`，再调用 Perception 和 Memory**。若 `Probe`/`Apply` 抛错，**声音可能已经提交**。（来源：`UPGRADE_ARCHITECTURE_REVIEW.md:46`）
- **影响**：对视障玩家，"敲杖声"通常被理解为"这次探测生效了"，但实际可能什么都没记下。**Tick 会故障暂停且拒绝保存**，所以不会静默错误，只是**提示语义偏乐观**。
- **建议（择一）**：
  1. 保持顺序，但在文档/交接中明确该提示的语义是**"已触发敲杖"，不代表记忆提交成功"**；
  2. 或在 `Apply` 成功后再播放（代价：反馈延迟一帧，需评估是否影响手感）。
- **成本**：低（选项 1 是纯文档）。

---

## C. 性能（剩余项）

### C-C1 · 渲染 Surface 缓存需要一次"路径前后计时"才有结论 ⭐
- **现状**：缓存已实现，失效规则完整。但复核报告明确写着：**"此修改目前证明的是代码路径可跳过，不是毫秒收益"**，且明确拒绝把 Perception/Memory 的 472 ms 归因于 Renderer。
- **建议**：在同一源码指纹下，对**同一静态记忆快照多帧重复绘制**，分别计时 `RenderPlan` 与 Surface batch 的 **cache-hit / rebuild** 两条路径，覆盖：初次 render、30 帧稳定、边界到期、恢复、AudioOnly 切换。报告 **帧数、顶点/三角形规模、cache hit/rebuild 数、中位/P95/max**。
- **理由**：Role08 的 `RecordsInitialRebuildAndThirtyStableRenderTimings` 插桩已经写好（输出 p50/p95/max，并断言稳定阶段 `SurfaceBatchRebuildCount` 不增长），**只差把它跑起来并读出数字**。
- **成本**：低（测试已备）。

### C-C2 · 给探针性能定一个可回归的门槛
- **现状**：当前有一次性测量（max 14.5 ms），但**没有断言**。未来任何改动都可能悄悄把它推回去。
- **建议**：在集成测试中加一条宽松但明确的上限断言（例如：单次探针 `Perception` 与 `Memory.Apply` 均 < 50 ms，取一个远高于当前值、但能拦住数量级回归的阈值），并把当前 p50/p95/max 作为**基线数字记入文档**。
- **理由**：**尖峰才是玩家会感觉到的卡顿**。这次是"离线诊断发现 752 ms"，下次如果没人测，就会变成"上架后才发现"。
- **成本**：低。

---

## D. 遗留代码与内容管线

### D-D1 · 给旧版生产文件加文件头 LEGACY 标记（**不删除**）⭐⭐
- **现状**：**39 个旧版生产文件仍在编译并进构建（约占生产代码 63%）**，与新版 `Street*` 实现同程序集并存，且**代码层面零标记**——"哪个是活的"只写在 `ARCHITECTURE_ADVICE_DECISIONS.md` 的文档里。
- **建议**：给这 39 个文件各加一行文件头注释，指向决策文件，例如：
  `// LEGACY：非新版运行入口 — 见 docs/upgrade/ARCHITECTURE_ADVICE_DECISIONS.md`
- **理由**：决策文件已明确"旧资源保留，不做大批删除"（符合 A31/R20），**这个折中是对的**；但半年后接手者打开程序集仍会看到两套实现。**文档标记 + 代码标记，成本差几分钟，收益差很多。**
- **成本**：极低。

### D-D2 · 清理旧代码前，必须先解耦两处硬引用
- **证据（实测）**：
  - **`StreetEntry.cs:47` 使用旧类 `LocalSaveService`** 构造设置存储：`new LocalSaveService(Path.Combine(root, configuration.SaveNamespace, "Options"))`。
  - **`Geometry2.cs`（17 行）仍被引用**（约 14 处命中），不是空壳。
- **建议**：把这两处列为"删除旧运行时的前置条件"。`LocalSaveService` 要么提升为共享基础设施、要么由 11 号角色提供新实现；`Geometry2` 的调用点迁到 `StreetGeometry`。
- **理由**：这是"迟早要拆"路径上**唯一的两个硬阻塞**。先把它们解掉，将来拆分就只是机械操作。
- **成本**：中。

### D-D3 · 为新建关卡管线补一个最小构造期校验器 ⭐
- **现状**：新关卡由 `StreetLevelFactory` 以代码字面量构造，**完全不经过 `LevelValidator`**（后者只服务旧 JSON 管线）。实测 `StreetLevel.cs` 对 `Validator` 零命中。
- **建议**：在 `StreetLevelFactory.Create()` 出口加一个轻量校验，至少覆盖：
  1. **R16 禁放项**：公交站 / 店铺 / 树池 / 电线杆 / 井盖；
  2. **零碰撞、零高度的活动声源不得落在任何 `Solid` 多边形内部**；
  3. 出生点 / 检查点 / 终点不在实心区域内。
- **理由**：第 2 条**已经真实发生过**——见 D-D4。
- **成本**：低（十几行断言）。

### D-D4 · `animal.bird.ne` 落在实心建筑内部
- **证据**：`animal.bird.ne` 位于 `(12.8, 18.2)`，**落在实心 `building.ne.east` 矩形 `x=10.7..24, y=7..24` 内部**（该对象无碰撞多边形、高度 0）。
- **建议**：确认设计意图。若代表街外鸟鸣 → 移到开放区域（如东北小巷）；若代表室内/屋顶声源 → 需要能表达该意图的高度或空间说明。
- **理由**：当前是**确定的实体/声源占位重叠**，且没有任何校验会拦住同类问题。
- **成本**：低。

### D-D5 · 可见红绿灯与路沿/坡道高度：请明确定界
- **现状**：
  - 红绿灯**只有相位声源**，`AddSignal` 创建空多边形、零高度的声音对象；**没有可见灯体或灯色呈现**。
  - `StreetSurface` 只有 `Id / MaterialId / Polygon / Priority`；`MaterialAt` 只返回材质 ID，移动是纯二维平面碰撞。**`ramp.*` 与 `curb.*` 只是贴了不同材质的平面，没有高度或坡度数据。**
- **建议**：这两项都触及**共享契约**，需要 00 明确本轮对 **R15 / D12 / A20** 的物理与展示语义边界，再决定是否冻结最小高度字段并派 03/09/08。**若判定本轮不做，请在文档中明确写为"本轮范围外"，而不是留在 NOT_RUN 里悬空。**
- **成本**：定界为低；实现为中。

### D-D6 · 信号声音 ID 在 World 中重复定义
- **证据**：`StreetWorld.cs:490-491` 直接比较字面量 `"street.signal.ns"` / `"street.signal.ew"`，与 `StreetCatalog.Sounds` 重复真相。
- **建议**：提升为契约层常量（或 `StreetCatalog` 上的命名常量），World 引用常量。
- **理由**：这是主控此前列为"维护债"的条目之一，改动极小。
- **成本**：极低。

### D-D7 · 菜单"从路口重来"文案与行为不符
- **证据**：`AccessibleMenu` 的该选项实际执行**回检查点**（`StreetSession` 对应分支），且**没有真正的重开关卡入口** → 永久记忆不可重置。
- **建议**：择一——改文案使其准确，或补一个真正的重开关卡入口。
- **理由**：这是个**可复现的文案/行为不一致**（属 A25 相邻范围）。
- **成本**：低。

---

## E. 验证工具链硬化（复核已提，实测**均未落地**）

以下四条来自 `VERIFICATION_TOOLS_RECHECK_2026-10-16.md`。我逐条核实了当前脚本源码，**四条都还没改**。

### E-E1 · manifest 排序改用 ordinal
- **证据**：`tools/Verify-Unity.ps1:22` 仍是 `Sort-Object { $_.path }`，**受当前区域性影响**。
- **影响**：同一机器可重复，但**跨语言环境不保证同一顺序** → 指纹不可跨环境比较。
- **建议**：改 ordinal 排序并固定序列化格式。
- **成本**：极低。

### E-E2 · 清单遍历加 `-Force`
- **证据**：`tools/Verify-Unity.ps1:19` 是 `Get-ChildItem -LiteralPath ... -Recurse -File`，**无 `-Force`**。
- **影响**：隐藏文件不进清单；若存在影响导入/构建的隐藏文件，指纹会漏掉。
- **建议**：加 `-Force`。
- **成本**：极低。

### E-E3 · 结果 JSON 记录 verifier 自身 SHA-256
- **证据**：Player 侧已有 `executableSha256` / `verifierSha256`；**Unity 侧结果 JSON 只有 `sourceFingerprint`，没有 verifier 哈希**。
- **影响**：无法证明一份 Unity 结果出自哪个版本的验证脚本——**这正是历史假 PASS 无法归因的同一条问题**。
- **建议**：在 Unity 结果中加 `verifierSha256`。
- **成本**：极低。

### E-E4 · `Save-UpgradeHandoff.py` 的总状态蕴含了它不保证的东西 ⭐
- **证据**：`tools/Save-UpgradeHandoff.py:116` **无条件**写 `'status': 'SAVED_AND_VERIFIED'`；`:120` 的 `'originalBaselineProtectionPassed'` 只是**并列字段**。
- **影响**：当 `originalBaselineProtectionPassed=false` 时，总状态仍显示"已保存并验证"。**消费者只读总状态就会误判"原项目未改动"**。
- **这与已被定性的 Player 假 PASS 属于同一类错误：总状态声明了它并不保证的东西。**
- **建议**：保护不通过时让总状态变为非通过，或把总状态改名为纯归档状态。
- **成本**：低。

### E-E5 · Player 验证脚本的失败路径留证
- **证据**：`tools/upgrade/Verify-StreetPlayer.ps1` 在 `try` 之前解析 EXE 并读取哈希；**坏路径或不可读时可能只留下空证据目录而没有 `results.json`**。
- **建议**：先初始化 `NOT_RUN` 报告，再把路径解析与哈希读取也放进 `try/finally`。
- **理由**：我实测发现 `output/unity-verification/` 下**存在三个完全空的运行目录**（`20261016-023336`、`023430`、`023510`）——**空目录无法说明那次运行发生了什么**。
- **成本**：低。

### E-E6 · 报告里记录的截图路径应校验存在性
- **证据**：`results.json` 记录了 `"screenshot": ".../first-step-menu.png"`，但**该文件实际不存在**（实测 `Test-Path` 为 False，两次运行均如此）。
- **建议**：写入报告前校验文件存在，或去掉未生成的路径字段。
- **理由**：否则后续无法区分"有截图"与"没截图"。
- **成本**：极低。

### E-E7 · `Assets/InitTestScene*.unity` 会污染指纹
- **证据**：PlayMode 运行后在 **`Assets/` 根目录**留下 `InitTestScene639277135299285861.unity`（14 KB，Unity Test Framework 生成）。文件名含时间戳。
- **影响**：`Assets` 在 `sourceFingerprint` 监控范围内（`Verify-Unity.ps1:18`）；该文件每次运行名字不同，**可能触发 `sourceUnchangedDuringStage=false`，把一次干净运行判成 FAIL**。
- **建议**：在验证脚本运行前后清理该模式文件，或将其排除出指纹清单。
- **成本**：低。

### E-E8 · `ProjectSettings` 在"必须逐字节不变"清单内，存在偶发假 FAIL 风险
- **证据**：`Verify-Unity.ps1:18` 将 `ProjectSettings` 纳入清单；`:135` 有"运行期间源码变化即抛错"的守卫。而实测 **`ProjectSettings/TimeManager.asset` 在本会话被 Unity 重写至少三次**（01:29、01:42、02:23 等）。
- **影响**：若某次重写正好落在"运行前采样"与"运行后采样"之间，**一次本应全绿的运行会被判 FAIL**。本轮尚未触发，属潜在风险。
- **建议**：择一——把 `ProjectSettings` 移出"必须逐字节不变"的清单（改为只监控 `Assets`），或先做一次预热运行让 Unity 稳定该文件。
- **成本**：低。

---

## F. 文档与证据卫生

### F-F1 · `ACCEPTANCE.md` 结论已过时，且包含基于旧截图 FAIL 的条目 ⭐⭐
- **现状**：`ACCEPTANCE.md` 最后更新 **22:24**。其 A08 / A12 判定为 **FAIL**，依据是 **20:25 / 20:26** 的截图（品红材质 + 无地面纹理）。但**21:30 已修 shader**，22:10 构建、22:14 之后的多张截图**已无品红、地面纹理正确**。
- **建议**：用**同版 Player 重新截图**并逐项复核 A08 / A12；在文档中保留旧 FAIL 记录与来源，但标注"已由后续证据推翻/待复核"。
- **理由**：这是当前**最容易误导外部读者**的一份文档——它会把已修好的问题读成未修好。
- **成本**：低（证据已存在，只差复核与更新）。

### F-F2 · 为每个 PASS 记录对应的 `sourceFingerprint`
- **建议**：在 `ACCEPTANCE.md` / `STAGE_BASELINE` 的每一行证据旁记录该次运行的 `sourceFingerprint`。
- **理由**：现在指纹机制已经可靠，但验收文档还没用它。**读者无法一眼判断某个 PASS 属于哪版源码。**
- **成本**：极低。

### F-F3 · 标记已失效的历史证据
- **建议**：对以下记录加"**不得用作当前证据**"标注（保留原文）：
  - `20261015-221422` / `20261015-221736` 的 Player 报告（顶层 PASS + `speechBackend=FAIL`，工具已改，旧 PASS 不可用）；
  - 缺 `sourceFingerprint` 的历史 Unity 结果（如 `20261015-232743` 的 113/113）。
- **理由**：未标注的旧 PASS 会被后来者误引。
- **成本**：极低。

### F-F4 · `UPGRADE_STATUS.md` 首页已落后
- **现状**：其"当前实测"段仍写 **"108 项、7 失败"**，而实际已是 **161/161**（指纹 `CAD6588…`）。
- **建议**：更新首页实测段，并注明旧数字属历史。
- **成本**：极低。

### F-F5 · 相同 SHA-256 的截图不得计作两个独立证据（**该纪律建议沿用**）
- **现状**：主控已自行发现 `a30-real-collision-recovery.png` 与 `a30-continued.png` **SHA-256 相同**（`2A32093B…`），并主动声明"不以截图数量佐证视觉状态"。**这个处理是正确的，建议作为固定纪律写进验证规范。**
- **补充建议**：我在核对时发现这两文件**字节数亦相同**（81,154）。建议在验证脚本中**自动对截图集合做去重并报告唯一图数**，把这条纪律变成机制。
- **成本**：低。

---

## G. 无障碍功能缺口（不涉及真人测试）

### G-G1 · 普通碰撞的声反馈已接入，但"掉血无提示"的完整性需复核
- **现状**：`StreetSession.cs:71` 已在非车辆接触时播放 `collision.bump`。这是从"只有旧 `DemoSession` 使用"→"新版接入"的真实修复。
- **建议**：核对**所有**会导致玩家状态受损的路径是否都有声反馈（不只普通碰撞），并确认 `feedback.breath` 等目录内 ID 是否仍无新版使用者。
- **成本**：低。

### G-G2 · 语音覆盖自检与"可见失败"
- **现状**：已生成 89 条语料，错误提示改为固定句并已入 manifest。
- **建议**：加一个**构建期或启动期自检**——把代码中可能请求的全部文案 key 与 manifest 的 hash 集合比对，**不一致时明确失败（日志 + 字幕）**。
- **理由**：语料缺失在 Player 中的表现是**静默无声**；对依赖声音的玩家，这是最坏的失败模式。自检把它变成可见失败。
- **成本**：中。

### G-G3 · `FeedbackPriority.Navigation` / `voiceprintIntensity` 的使用确认
- **现状**：审查曾指出街景源只用到 `CriticalThreat` 与 `Ambience` 两级，`Navigation` 从未使用；`Occlusion` 存入但 `EffectiveVolume` 不直接使用（已并入 Discernibility）。
- **建议**：确认这是有意设计（则可删去或注明），还是遗漏（则接线）。**不要让配置项长期处于"看着能用实际无效"的状态。**
- **成本**：低（确认）；中（若需接线）。

---

# 第三部分 · 建议处置顺序（按投入产出比）

| 顺序 | 条目 | 理由 | 成本 |
|---|---|---|---|
| 1 | **跑完剩余阶段**（PlayMode / Build / Player / 语音 Started）在同一指纹 | 当前唯一阻塞"可交付"的动作 | 低 |
| 2 | **D-D1** 旧文件加 LEGACY 标记 | 零风险，立即可见收益 | 极低 |
| 3 | **E-E4** 修 `Save-UpgradeHandoff.py` 总状态 | 与已定性的假 PASS 同类，别让它成为下一个 | 低 |
| 4 | **F-F1** 复核并更新 `A08 / A12` | 当前最易误导人的文档 | 低 |
| 5 | **A-A1** `Area()` 远离原点薄片回归 | 锁住一整类几何缺陷 | 极低 |
| 6 | **E-E1 / E-E2 / E-E3** 清单三个硬化点 | 一次改完，指纹从此可跨环境 | 极低 |
| 7 | **B-B1** `StreetMemory.Apply` 事务性 | 唯一影响不可逆存档的记忆层缺口 | 中 |
| 8 | **D-D3** 新关卡最小校验器（含 D-D4 鸟源） | 已经真实发生过一次 | 低 |
| 9 | **C-C1 + C-C2** 渲染计时 + 探针性能门槛 | 把一次性数字变成可回归约束 | 低 |
| 10 | **D-D2** 解耦 `LocalSaveService` / `Geometry2` | 为将来拆分旧运行时铺路 | 中 |
| 11 | **D-D5** 红绿灯 / 坡道高度定界 | 需要 00 先做范围决定 | 低（定界） |
| 12 | **G-G2** 语音覆盖自检 | 把静默失败变成可见失败 | 中 |
| 13 | 其余（B-B2 / B-B3 / D-D6 / D-D7 / E-E5–E-E8 / F-F2–F-F4 / G-G1 / G-G3） | 按维护窗口批量处理 | 低–中 |

---

# 第四部分 · 决策记录表（请 00 填写）

**填写规则**：状态填 `采纳` / `部分采纳` / `驳回` / `延后`。
**填「驳回」或「延后」时请写理由**（事实与依据即可，不必客套）。委派他人执行时填「执行角色」。

| # | 建议 | 状态 | 理由（驳回/延后必填） | 替代方案 | 复审触发条件 | 执行角色 |
|---|---|---|---|---|---|---|
| A-A1 | `Area()` 远离原点薄片回归 | | | | | |
| A-A2 | 四个 epsilon 的单位/误差预算注释表 | | | | | |
| A-A3 | Perception offset/merge epsilon 补遮挡反例与覆盖对照 | | | | | |
| B-B1 | `StreetMemory.Apply` 加事务/回滚 + 注入故障验证 | | | | | |
| B-B2 | 静止重复探测 +5 片/零面积：设观察阈值 | | | | | |
| B-B3 | `cane.tap` 语义澄清或调整顺序 | | | | | |
| C-C1 | 渲染 Surface 缓存的前后计时测量 | | | | | |
| C-C2 | 探针性能可回归门槛 | | | | | |
| D-D1 | 旧版生产文件加 LEGACY 文件头标记 | | | | | |
| D-D2 | 解耦 `LocalSaveService` / `Geometry2` | | | | | |
| D-D3 | 新关卡最小构造期校验器 | | | | | |
| D-D4 | `animal.bird.ne` 与建筑重叠的意图确认 | | | | | |
| D-D5 | 可见红绿灯 / 路沿坡道高度范围定界 | | | | | |
| D-D6 | 信号声音 ID 提升为常量 | | | | | |
| D-D7 | 菜单"从路口重来"文案/行为一致 | | | | | |
| E-E1 | manifest 排序改 ordinal | | | | | |
| E-E2 | 清单遍历加 `-Force` | | | | | |
| E-E3 | Unity 结果记 `verifierSha256` | | | | | |
| E-E4 | `Save-UpgradeHandoff` 总状态反映保护位 | | | | | |
| E-E5 | Player 验证失败路径也写 `results.json` | | | | | |
| E-E6 | 截图路径写入前校验存在 | | | | | |
| E-E7 | 清理/排除 `Assets/InitTestScene*.unity` | | | | | |
| E-E8 | `ProjectSettings` 移出"逐字节不变"清单或预热 | | | | | |
| F-F1 | 复核并更新 `ACCEPTANCE.md` 的 A08/A12 | | | | | |
| F-F2 | 每个 PASS 记录对应 `sourceFingerprint` | | | | | |
| F-F3 | 标记已失效的历史证据 | | | | | |
| F-F4 | 更新 `UPGRADE_STATUS.md` 首页实测段 | | | | | |
| F-F5 | 截图 SHA-256 去重纳入验证脚本 | | | | | |
| G-G1 | 全路径受损声反馈复核 | | | | | |
| G-G2 | 语音覆盖自检 + 可见失败 | | | | | |
| G-G3 | `Navigation` / `voiceprintIntensity` 使用确认 | | | | | |

**记录位置建议**：本表就地填写，或另建 `docs/upgrade/ARCHITECTURE_ADVICE_DECISIONS_2.md` 并在本文件留链接。
**为什么不建议只在聊天里回**：本项目已有"动态提示词未及时落盘"被记为流程缺口的先例。建议的驳回理由若不落盘，后续接手者无法区分"没想到"与"想过但不做"。

---

# 第五部分 · 事实索引（便于核对每一条的依据）

| 事实 | 依据 |
|---|---|
| 探针性能改善 | `output/unity-verification/20261016-030510-2781e2d0/street-acceptance/probe-performance-20261015-190521-6564-ecba1a6f.json` |
| EditMode 161/161 + 指纹 | `output/unity-verification/20261016-030510-2781e2d0/results.json` |
| 静止重复 8 次面积恒定 | 同上 `probes[]` 中 `phase=stationary-repeat` |
| 三阶段同指纹全绿（旧） | `docs/qa/upgrade/STAGE_BASELINE_2026-10-16.md`，指纹 `98C682…` |
| 内存合并热点分解 | `output/pure-probe/MemoryInstrumented-route.txt`、`MemoryInstrumented-groups.txt` |
| 内存插桩分组倒挂（删 15 个花 235ms / 删 233 个花 9ms） | `output/pure-probe/MemoryInstrumented-groups.txt` |
| 静止玩家车撞根因与修复 | `docs/qa/upgrade/STATIONARY_VEHICLE_DIAG_2026-10-16.md`、`output/pure-probe/RootVehicleEdgeDiagnostic-before/after.txt` |
| `Memory.Apply` 事务性缺口、`cane.tap` 顺序 | `docs/handoffs/UPGRADE_ARCHITECTURE_REVIEW.md:46-47` |
| 渲染缓存无收益结论 | `docs/qa/upgrade/RENDER_PERFORMANCE_REVIEW_2026-10-16.md:33` |
| 验证工具四个未落地硬化点 | `docs/qa/upgrade/VERIFICATION_TOOLS_RECHECK_2026-10-16.md:22`，并实测 `tools/Verify-Unity.ps1:19,22`、`tools/Save-UpgradeHandoff.py:116,120` |
| 三个空证据目录 | `output/unity-verification/20261016-023336-fa2c4e02`、`-023430-37955fea`、`-023510-34f5162b`（实测 0 文件） |
| 截图 SHA-256 相同 | `STAGE_BASELINE_2026-10-16.md` 内 `2A32093B…` 记录；实测两文件均 81,154 字节 |
| `InitTestScene*.unity` | `Assets/InitTestScene639277135299285861.unity` |
| 旧代码 39 文件 / 新 23 文件 | 按 `Assets/TingLu` 下生产 `.cs` 的最后修改时间与升级启动时间（2026-10-15 13:57）分类统计 |
| `LocalSaveService` 仍被新入口使用 | `Assets/TingLu/Bootstrap/Runtime/StreetEntry.cs:47` |
| `Geometry2.cs` 仍被引用 | 全生产代码检索命中（约 14 处） |
| 新关卡不过 `LevelValidator` | `Assets/TingLu/Levels/StreetLevel.cs` 对 `Validator` 零命中 |
| 信号 ID 写死在 World | `Assets/TingLu/World/StreetWorld.cs:490-491` |
| 鸟源与建筑重叠 | `docs/qa/upgrade/MAP_TRAFFIC_RECHECK_2026-10-16.md:15` |
| 红绿灯无灯体、坡道无高度 | `docs/qa/upgrade/MAP_TRAFFIC_RECHECK_2026-10-16.md:12-13` |
| 历史 Player 假 PASS | `docs/qa/upgrade/VERIFICATION_TOOLS_RECHECK_2026-10-16.md:12` |
| `ACCEPTANCE.md` 过时 | `docs/qa/upgrade/ACCEPTANCE.md:10,28,32`（更新于 22:24，引用 20:25/20:26 截图） |
| `UPGRADE_STATUS.md` 首页落后 | `docs/upgrade/UPGRADE_STATUS.md` 首页实测段（写 108 项 / 7 失败） |

---

# 第六部分 · 本文件未覆盖的事项（明确声明）

- **未运行 Unity、未运行测试、未采集 Profiler**：所有性能与行为结论均引自既有证据文件，或为静态源码判读。
- **未逐项核对旧关卡内容**：除 `MAP_TRAFFIC_RECHECK` 已记录的缺口外，未重新遍历旧 `FirstStep` 内容。
- **未核实模型 / provider / effort 元数据**：本工具不可见，不作推测。
- **未评估编辑器窗口内部行为**。
- **设备音频输出、无障碍体验、发布级认证**：不在本审查范围。

以上边界适用于本文件全部条目。
