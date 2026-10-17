# UPGRADE_MAP_TRAFFIC_AUDIT 交接

status: PARTIAL

role/model/effort: 地图与交通静态复核；请求配置 `gpt-6-luna/xhigh`；本工具未独立返回实际provider/model/effort元数据，故不可核实。

contract_version: StreetContract 1.0.0

## completed

- 按动态提示只读复核升级入口、01–04、实现契约、03/09/12完整角色提示词、`StreetWorld`、`StreetLevel`、Role03/Role09测试、关卡场景及相关信号/音频状态源码。
- 记录R15/R16设施、稳定ID和Point2坐标映射；逐项核对路网、过街、盲道/占用/绕行、建筑/小巷、活动对象、检查点和终点。
- 修正东西向路线侧别：东行南侧`y=-1.35`、西行北侧`y=+1.35`；稳定ID、EW相位、停止线、尺寸不变。
- 在Role09路线测试中加入方向向量推导右手侧的初始/停车/放行位置断言，并检查闭环回程线留在可玩核心外及既有建筑间隙。
- 记录红绿灯只有相位/声音源而无可见灯具；路沿/坡道只有材质区域、没有高程/坡度字段。
- 标出`animal.bird.ne`声源位于`building.ne.east`实心建筑范围内的坐标关系；按总控指示保留为设计意图复核项，未移动。

## changed_files

- `docs/qa/upgrade/MAP_TRAFFIC_RECHECK_2026-10-16.md`
- `docs/handoffs/UPGRADE_MAP_TRAFFIC_AUDIT.md`
- `Assets/TingLu/Levels/StreetLevel.cs`
- `Assets/TingLu/Tests/Upgrade/Role09/StreetLevelFactoryTests.cs`

没有修改源码、测试、契约、asmdef、Bootstrap、Config、场景或任何原项目文件。

## integration

- 东西向修正已在09所有权内完成；00需在排他Unity窗口运行Role09断言及测试程序集，验证停止线、信号放行和非接近回环路径。
- 信号灯呈现由09提供关卡数据、08消费合法已知展示帧；需00核对信息许可，不得全图泄漏灯态。
- 路沿/坡道高度若属于本轮验收语义，应由00冻结共享契约后再分派03/09/08。本审计没有修改该接口。
- 地图异常鸟源请09确认意图；如为街外鸣声，可移至开放地面；如为室内/屋顶声源，需要明确空间高度/遮挡约定。

## tests

- NOT_RUN：未启动Unity、未构建、未运行Role03/Role09测试；遵循审计提示中“不开Unity”限制。
- 新增的右侧通行断言覆盖当前Route初始位置、停止线位置、NS相位移动、EW相位放行及闭环回程段；仅经源码检查，结果仍为NOT_RUN。
- 静态证据：`StreetLevel.cs`的路网/信号/对象与路线定义；`StreetWorld.cs`的相位、SoundActive同步、路线停车和碰撞；`StreetContracts.cs`的Surface/Object字段边界；Role03/Role09测试源码与handoff记录。具体映射及行号见QA复核文档。
- 不能将`UPGRADE_STATUS.md`所载的旧113/113 EditMode结果用于本次冻结修复源码。Role03/09 handoff均没有当前版本Unity测试通过证据。

## limitations

- 没有可见Player画面、声卡播放、设备或真人听辨证据；信号SoundActive同步仅是源码状态。
- 不据静态审核单独宣布A20/A21或A25/A30通过；新源码冻结后需统一运行与重新判定。
- 车道方向已依据中国普通道路右侧通行修正；当前仍缺Unity测试结果。
- 鸟源与建筑重叠是确定的坐标关系，但当前数据未声明声源是在室内、屋顶还是街外，因此只报告为位置异常待确认。

## contract_requests

- 不直接改契约。建议00明确`StreetSurface`是否需增加高度/坡度信息及查询，才能核验D12“路沿高度、坡道连接”；目前仅有材质面和二维移动/碰撞。
- 如本轮“红绿灯”要求画面可见，需要定义信号灯状态如何经合法探索帧供08呈现，避免渲染绕过感知直接读取完整世界。

## next_dependencies

- 00确认并协调上述三项实现/语义决策，按目录所有权派09、08或03处理。
- 00统一冻结修复源码后，在排他Unity窗口运行Role03/09程序集；12基于新版本证据重评相关验收项。
- READY后本审计停止写入。

## follow_up_boundary_review (2026-10-16)

- **R15可见灯态仍缺失，现有提示音/相位逻辑不构成可见红绿灯。** `StreetLevelFactory.AddSignal` 将两个信号声源配置为 `StreetKind.Active` 且空 Polygon；`StreetPerception.BuildKnownObjects` 跳过活动对象，`StreetRenderPlan` 也没有交通灯显示分支。`StreetWorld` 仅按 NS/EW/clearance 相位同步 `SoundActive`，当前渲染不消费世界相位。结论限于地图里没有可见灯具/灯态，不代表交通提示音或相位缺失。
- **D12有材质片和二维通行，但没有可验证的高度/坡度表达。** `StreetLevel` 配置了 `curb` 与 `ramp` 独立材质区域；08 将 ramp 映射为斜纹，curb 由默认 Plain 样式显示。`StreetWorld.MaterialAt` 返回材质 ID，二维移动不因表面边界受阻，因此走过这些区域在平面上可达。与此同时，`StreetSurface` / `StreetKnownSurface` 没有高度字段，08 的已知地面网格统一落在固定 SurfaceHeight；目前的斜纹和材质边界不能证明路沿有高差或坡道把两侧高程连起来。
- D12 本身只要求路沿“表现高度与材质边界”、坡道“提供顺畅连接”；本复核不将其扩写为角色必须具有垂直运动、坡度碰撞或重力模拟。当前只能确认平面可走和材质语义，视觉高度部分未完成。
- **最小后续建议（未实现、需00冻结共享字段）：** 为灯具保留独立的永久局部几何，并让04仅在真实遮挡裁剪后的许可覆盖命中该灯具时生成带有效期的灯态观察；08只在05已有该灯具的合法几何记忆且灯态观察仍有效时绘制。灯态不进永久Memory，也不由08查询World。为D12给 `StreetSurface` / `StreetKnownSurface` 增加紧凑的局部高度轮廓（路沿可用常高，坡道用连接两侧高度的平面梯度）；04只把对应许可面片及其轮廓传给05，05复制该值，08只按记忆面片生成局部高低网格，不补未知区域。既有二维移动可保留，除非00另行明确角色高程语义。
- 后续验证至少覆盖：03的灯态与NS/clearance/EW相位一致；04墙后/未覆盖区不输出灯态；05保存/复制地面高度但不持久化动态灯态；08缺少许可几何或有效灯态时不画灯、已知坡面仅在许可片内起伏、AudioOnly无画面；09核对路沿/坡道相邻端点高度连续；最后保存Player画面证据供12重评A20/A29。上述均为建议验证，当前未改契约、未实现、未运行。

## minimum_interface_proposal (2026-10-16)

以下是供00冻结和拆分工作的具体方案；本审计仍只写handoff，没有改Assets或共享契约。

### R15 信号灯几何与短时灯态

- 让现有 `street.signal.ns` / `street.signal.ew` 保持同一稳定ID和声源相位语义，同时由09把信号对象改为 `Permanent` 几何对象，提供小型灯具外轮廓和正高度；保留其 `SoundId` 与随相位变化的 `SoundActive`，并保持 `Solid=false`、`BlocksProbe=false`、`VehicleHazard=false`。03当前的 `SyncSignalSources` 和06的声学查询都按信号SoundId/`SoundActive`工作，不要求声源一定是 `Active` Kind。永久几何进入05记忆，动态颜色不进入对象记忆。
- 03在 `StreetWorldFrame` 的采样快照增加全局 `NextSignalChangeAt`（由同一模拟时刻的 `NextPhaseChange(time)` 得出；当前NS/EW两灯在每个相位边界至少有一灯变色），与现有 `SoundActive` 同一采样时刻。04只有在该信号对象的灯具轮廓通过真实遮挡裁剪后的ProbeCell确认时，才输出临时 `StreetSignalObservation { ObjectId, Position, IsGreen, ObservedAt, ExpiresAt }`；`Position`用于本次已许可灯态绘制，`IsGreen`取该帧`SoundActive`，`ExpiresAt`不晚于`NextSignalChangeAt`。这样NS/EW和clearance边界不会沿用过期颜色；若灯态由非Probe途径观测，必须另经00冻结许可来源。
- 04把 `StreetSignalObservation` 放在 `StreetProbeResult` 的独立短时字段中；05只复制/应用地面和永久几何，忽略该字段；它不得进入 `StreetMemoryFrame`、`StreetSaveData`、DTO或长期观察。当前 `StreetSession` 的Probe回调只把结果交给 `Memory.Apply`，`StreetEntry` 又把 `PerceptionFrame` 交给渲染器，因此00还需接线：`StreetSession` 按ObjectId暂存最新观察、每帧剔除过期值并在会话重置时清空，`StreetEntry`把该短时列表一并传到 `StreetRenderer`/`StreetRenderPlan`。08将有效观察按ObjectId关联到05记忆中已知的灯具轮廓：无合法几何或观察已过期时不显示红/绿，最多显示已记住的中性灯具外壳；存在当前合法观察时才着色。标准/NoUI可呈现，AudioOnly渲染计划为空；08不读World或相位。
- 状态色暂按现有模型映射：`SoundActive=true`为对应方向绿，false为红（包含全红clearance）。若策划要求clearance呈黄灯或黄闪，需要单列状态枚举并由03快照提供，不能从false推断。默认14/3/14/3秒周期保持由StreetWorld配置唯一决定。

### D12 地面高度面

- 在 `StreetSurface` 与 `StreetKnownSurface` 增加 `HeightBase`、`HeightSlopeX`、`HeightSlopeY` 三个有限值，约定世界坐标高度 `h(x,y)=HeightBase + HeightSlopeX*x + HeightSlopeY*y`；三值全零表示旧的平面地面，坡度单位为高度/世界单位。无需扩展 `MaterialAt` 或二维移动接口。
- 04按既有优先级裁剪材质面时原样复制其高度面参数到每个许可交集片；裁剪改变Polygon但不改世界坐标上的平面。05的 `CopySurface`、重叠替换和邻接合并必须保留高度，并将三个参数加入 `SurfaceKey` 的相等/哈希，避免把不同高程/坡度的面片合并。08只对05记忆里的每个已知面片逐顶点求高度并构网，不根据完整StreetLevel补未知地面；AudioOnly仍无网格。不同高度面的公共边若高度不连续，需由08在双方均已知的位置补竖向路沿侧面；连续坡道端点不生成侧墙。
- 09地图建议将道路/斑马线高度定为0，人行道和路沿顶面定为总控冻结的 `H>0`，所有无特殊高度区域默认0。当前八片坡道按所在方向使用同一连续平面（每侧两片）：北侧 `H*(y-4.2)/2`，`y∈[4.2,6.2]`；南侧 `H*(-4.2-y)/2`，`y∈[-6.2,-4.2]`；西侧 `H*(-4.2-x)/2`，`x∈[-6.2,-4.2]`；东侧 `H*(x-4.2)/2`，`x∈[4.2,6.2]`。每个公式在车道端为0、在人行道端为H；`H`当前需求未定，不能由审计擅自填值。
- 存档的 `StreetSurfaceMemoryDto` 增加对应三个可选高度成员（`DataMember(IsRequired=false)`，缺省为0）；Encode/Decode及05的stale/Import操作保留高度，只把`CurrentUntil`过期。兼容方案建议将 `StreetContract.Version` 与 `StreetSaveService.CurrentContract` 同步升到1.1.0、schema仍为1，并让旧1.0.0无高度字段的存档显式按零高度平面迁移；10需在Inspect中认可1.0.0、按包内原contract值校验checksum，解码后下次保存写1.1.0。当前Inspect会拒绝旧contract，需明确增加兼容分支并测试旧档。若不接受该兼容路径，则必须冻结另一项显式的存档迁移方案，不能静默丢弃旧存档。灯态不加DTO字段。
- 此字段只让地形顶面可呈现高差；不增加角色Z、高程状态、重力、坡度碰撞或垂直移动要求。R15/D12之外的对象高度语义保持原状。

### 最小涉及边界与验收

- 契约/关卡/世界：`StreetContracts.cs`、`StreetLevel.cs`、`StreetWorld.cs`；对应03、09所有权。感知/记忆/会话/入口/存档/呈现：`StreetPerception.cs`、`StreetMemory.cs`、`StreetSession.cs`、`StreetEntry.cs`、`StreetSaveService.cs`、`StreetRenderPlan.cs`、`StreetRenderer.cs`；对应04/05/10/08并需00接线。需先由00统一冻结字段名、灯具尺寸和H，再分派，避免各角色临时造字段。
- 自动验证至少覆盖：03信号快照的灯态和valid-until在NS/clearance/EW边界正确；04墙后、未覆盖或只有声源但未确认灯具几何时没有状态观察；05保留坡面参数、按参数隔离合并且不持久化灯态；08只按许可记忆片和未过期状态绘制、坡面网格顶点符合函数、未知区不补面、AudioOnly无图；09逐一核验八片坡道的端点值及与道路/人行道连续，路沿台阶侧面只出现在已知且有高度差的公共边；10验证新档高度往返、读档后高度保留而显影stale、旧1.0.0档按零坡面加载且checksum仍有效。
- 需另保存实际Player画面并由12重评相应验收。以上仍是接口建议，未改源码、未运行Unity或测试；当前总控阻断与本交接无关，不能据此宣称R15/D12通过。
