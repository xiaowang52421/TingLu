# SESSION_BOUNDARIES 交接

- status: READY；本任务文件已冻结，等待总控统一导入与验证。
- contract_version: StreetContract 1.0.0。
- 模型请求：沿用 `audit_runtime` 的 `gpt-6-luna/xhigh` 请求配置；工具未独立返回实际 provider/model/effort，故不将请求记作已核实事实。
- changed_files:
  - `Assets/TingLu/Tests/Upgrade/Integration/StreetSessionIntegrationTests.cs`
  - `docs/qa/upgrade/REQUIREMENTS_RECHECK_2026-10-16.md`
  - `docs/handoffs/UPGRADE_SESSION_BOUNDARIES.md`

## 新增真实 Session 用例

`A24_RealVehicleContactBeforeAnyCheckpointRestoresSpawnAndStaysSafe` 使用 `StreetLevelFactory.Create()` 与正式 `StreetSession`。从正式出生点 `(-5.5, -16)` 通过玩家移动输入进入 `vehicle.ns.southbound` 的真实路线 `x=-1.35`，保持 `CheckpointId == "spawn"`，等待世界路线产生车辆扫掠接触。测试核对失败对象、一次碰撞/恢复、精确回到 `Level.Spawn`，并继续 5 秒固定模拟步确认没有立即重复接触。未直接调用恢复、构造碰撞或移动车辆。

`A22_CrossingWithRedNorthSouthTrafficRemainsFreeAndKeepsTrafficRunning` 沿生产关卡的可行绕行路线抵达 `checkpoint.west.approach`，再沿 `crosswalk.ns.north` 从西向东穿过南北向机动车道。相位语义按本轮冻结口径：北侧斑马线是东西向人行路线，穿越南北向车道；NS 相位表示南北向车辆放行，此时东西向行人信号为红。测试不关闭车流或移动车辆来制造空隙，而是在实际 NS 相位中等待两辆真实路线车辆分别越过横道（南向车中心低于 `y=3.8`、北向车中心高于 `y=6.8`），并确认剩余 NS 绿灯足以覆盖 11 米过街时长及 0.25 秒裕量后才出发。过街期间断言 NS 相位和 NS 信号声源保持有效、EW 行人信号声源保持关闭、两辆真实 NS 车辆仍继续移动、到达东侧检查点、勇气不变且无碰撞/恢复或“车道”警告语音。默认相位为 NS 绿灯 14 秒、清空 3 秒、EW 绿灯 14 秒、清空 3 秒；车辆位置等待比固定启动时刻更直接地证明已出现安全间隙。

相位与路线语义核对依据：`Assets/TingLu/Levels/StreetLevel.cs` 的北侧斑马线、四条信号路线和轴标签；`Assets/TingLu/World/StreetWorld.cs` 的 `PhaseAt`、`IsGreen`、车辆路线推进和停止线等待；`Assets/TingLu/Config/Shared/StreetConfig.cs` 的默认相位时长。`StreetRoute.SignalAxis == "ns"` 的车辆在 NS 相位放行、在 EW 相位停车；本用例要覆盖的是 NS 放行时的红灯横穿行为，并以车辆确已通过及整个测试期间车流持续推进作为间隙证据。

`OrdinaryVehicleFreeObstacleContactPlaysCollisionBumpThroughSession` 从出生点沿正式玩家输入撞到西侧自行车，核对普通非车辆接触经正式 Session 产生 `collision.bump` 播放、碰撞计数增加且不触发车辆恢复。

`RecoveryBackendFailurePausesProtectsLastGoodSaveAndRetryRestoresPlay` 先保存出生点的最后好存档，再注入 `RecordingAudio.Reset` 异常，通过真实车辆路线触发 Session 恢复。测试核对恢复故障使时钟与音频暂停、菜单停在 Pause、固定错误语句被播报；直接 Resume 和菜单 Resume 均被拒绝；故障期间 `Save()` 返回 `IoFailure` 且磁盘中的出生点、玩家位置、交通时间和碰撞计数仍与最后好存档一致。清除故障注入后从 Pause 选择 Retry，断言回到出生点、恢复 Playing 状态并可再次成功保存。`finally` 中先关闭注入，再 Dispose Session 并删除临时存档目录。

## 需求复核口径

需求复核报告将 R23/A34 更新为：前四项动态任务提示词在派发后补存，构成事前提示词存档时序缺口；本 `SESSION_BOUNDARIES` 提示词已在派发前落盘，是本次的合规个案，不能追溯修复前述缺口。报告将当前 A34 口径收窄为 PARTIAL，并保留既有 `COMPLIANCE.md` PASS 作为此前核查记录，不改动该文件。

## 验证状态与限制

- `A24_RealVehicleContactBeforeAnyCheckpointRestoresSpawnAndStaysSafe`: NOT_RUN。
- `A22_CrossingWithRedNorthSouthTrafficRemainsFreeAndKeepsTrafficRunning`: NOT_RUN。
- `OrdinaryVehicleFreeObstacleContactPlaysCollisionBumpThroughSession`: NOT_RUN。
- `RecoveryBackendFailurePausesProtectsLastGoodSaveAndRetryRestoresPlay`: NOT_RUN。
- 未启动 Unity，未运行 Unity EditMode/PlayMode，也未声称新测试已经编译或通过。总控应在所有源码冻结后统一验证。
- 测试仅提供确定性 Session 自动化证据；不代表 Player 操作、声音实际可听或真人无障碍验收。
