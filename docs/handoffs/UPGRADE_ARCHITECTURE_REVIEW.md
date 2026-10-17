# 架构审查交接

status: READY_FOR_00_REVIEW / runtime verification NOT_RUN  
date: 2026-10-16  
contract: StreetContract 1.0.0; save schema 1  
workspace: `D:\听路_街区升级` (升级副本；原项目只读)

## 结果

逐项复核与当前证据见 [`docs/qa/upgrade/ARCHITECTURE_REVIEW_FINDINGS_2026-10-16.md`](../qa/upgrade/ARCHITECTURE_REVIEW_FINDINGS_2026-10-16.md)。主控已有的 `docs/upgrade/ARCHITECTURE_ADVICE_DECISIONS.md` 未改。

当前源码中可确认：89 条语音缓存资源（manifest 与 WAV 数量一致）；固定通用错误语音；扫描线 renderer 与新的面积/边界测试源码；四业务 asmdef 已启用 `noEngineReferences`；池满语音退避；Session 运行/恢复故障锁及碰撞声；未知合同版本存档保护；`lowFocusAngle=360` 配置拒绝。东/西车道修正和对应 Role09 断言也已入源码。以上均为静态源码/文件事实。

## 验证状态

- READY：只读检查与报告落盘。
- NOT_RUN：当前源码 Unity 编译、Role00/06/08/09/10 与 Integration 测试、统一 EditMode/PlayMode、BuildWindows、当前 Player 画面、当前语音 Started、设备输出及真人听辨。
- 最近既有 Unity EditMode 证据是 2026-10-15 的 113/113；不认证本轮修改。
- 新 WAV 的旧 Player 测试在语音/播放器和 integration 近期源文件更新前生成。不得把旧图像、旧声学日志、旧 Player 结果当作当前源码表现。

## 读取覆盖与变更

读取了两份架构意见、主控决策表、动态审查提示词、升级契约与相关 QA/handoff，并对当前 Contracts/Config/Perception/Memory/World/Player/Presentation/Audio/Persistence/Levels/Bootstrap/UI 关键源码以及 Role00/06/08/09/10/Integration 测试做静态核对。没有运行 Unity、改生产代码或测试、触碰原项目/存档。

本 Agent 唯一写入：

- `docs/qa/upgrade/ARCHITECTURE_REVIEW_FINDINGS_2026-10-16.md`
- `docs/handoffs/UPGRADE_ARCHITECTURE_REVIEW.md`

源码仍并行变化；本次观察到最晚的相关文件时间为 `StreetSessionIntegrationTests.cs` 2026-10-16 01:25:55。文件时间不是统一源码指纹，主控冻结后应重新核对再运行。

## 后续验证顺序

1. 源码冻结后统一运行 Unity 编译及 Role00/06/08/09/10 和 Integration 测试；任何失败先修复再重跑。
2. 用同一源码指纹生成 BuildWindows，复验语音资源可加载、菜单与错误固定句 Started、池满释放后重试/取消、碰撞反馈、恢复失败锁与重试、存档未知版本主备字节保持，以及 Role08 复杂片画面。
3. 将设备实际发声、听辨与真人无障碍体验单列记录，不从编译或自动化测试推导通过。

provider/model/effort 的实际运行元数据本工具不可见，故未作推测性声明。

## 性能版只读复核追加（2026-10-16）

主控要求核对 `StreetPerception` 的 prepared surface 缓存、`StreetSession` 的 `cane.tap` 提交顺序及 `StreetRenderer` surface cache。仅作静态源码复核，未运行 Unity/测试，未修改 Assets。

- `StreetPerception.PrepareSurfaces` 的缓存失效键是输入列表长度与逐项对象引用。`StreetSurface` 为 sealed 不可变值：ID、材质、优先级只读，顶点复制到只读集合；`StreetWorldFrame` 复制 surface 列表，World 持有固定定义，且每次 `StreetSession.Start` 新建 `StreetPerception`。列表增删、重排或替换 surface 都会失效；仅重复包装相同不可变 surface 引用可安全复用。缓存只保存排序、凸分片和 bounds；probe cells、交集、优先级遮挡和结果仍逐次重算，因此 request 时间/位置无需成为几何缓存键。
- prepared 列表含全关卡 surface 定义，但字段私有；每份已知表面仍先与本次 `ProbeCell` 求交，再被更高优先级的已处理面裁减后才放入 `StreetProbeResult`。不会把未探测 cell 的地面写入记忆或 renderer；未发现许可信息泄漏。边界条件是输入定义仍须保持 `StreetSurface` 的不可变约束，当前实现满足。
- 玩家只在主动状态、冷却结束且同 tick 接触处理后创建 probe；`StreetSession` 先提交通用 `cane.tap`，再调用 Perception 和 Memory。声音只携带 request 原点及固定声音 ID，不包含材质、对象或几何，因此是即时动作确认，不泄漏探测结果。若播放调用本身抛错，后续 probe 不会执行；若 Probe/Apply 抛错，声音可能已经提交，但 `Tick` 会故障暂停，且 session fault 下拒绝保存。角色已消耗本次敲杖冷却/专注；因此该提示应理解为“已触发敲杖”，不能代表记忆提交成功。
- 相关边界风险：`StreetMemory.Apply` 先清空/替换内部 surface 列表，后续再合并 object 并最终重建公开 `Current`；若合并中途异常，公开快照可能仍是旧值，而内部列表已部分变更。故障锁阻止直接 Save，但 Retry 的 `Memory.MarkStale` 会从现存内部列表重建并保存，不能保证回滚到故障前记忆。这是既有的 Memory 事务性风险，不由缓存引入；正常 Probe/Apply 未见新数据越权，若要求异常恢复严格保留最后好记忆，应单独为 `Apply` 加事务/回滚或失败后回载快照并加注入故障验证。
- `StreetRenderer` surface cache 依赖不可变 `StreetMemoryFrame` 引用、Palette、Display、`worldToLocalMatrix` 及已缓存/当前时刻。已知 surface 当前分类只在 `ObservedAt`（含）与 `CurrentUntil`（不含）两处变化；`CrossedBoundary(prev, now, boundary)` 检测 `boundary > prev && boundary <= now`，覆盖边界恰好到达及跨过两边界的时间跳步；倒退时间强制重建。材质批 key 另含材质和 current/stale，其他 settings 只影响 strokes，故未见漏项。
- 复核主控委派的 renderer 异常清理修复：重建在成功缓存提交前使缓存失效；进入时统一禁用 renderer、清空 Mesh 和四类 staging buffers；`finally` 清 scratch，失败则再次禁用并清 Mesh、归零活动数量、维持 cache 失效；`Clear()` 也走相同清理。可避免失败 rebuild 的 staging 几何在后续 session/新 memory frame 中混入并跨许可边界复现。当前 Role08 源码覆盖时间、设置、矩阵、frame identity 和 Clear，并断言 Clear 会清空 scratch；未见强制注入 mesh commit 中途异常的测试，因此异常 `finally` 行为是源码判断，仍需在统一 Role08 验证时覆盖/确认。

本项只读复核已完成，状态 READY；上述 Unity/Role08 运行验证仍 NOT_RUN。独立结论已回报主控，Memory.Apply 事务性风险保留为独立决策项。
