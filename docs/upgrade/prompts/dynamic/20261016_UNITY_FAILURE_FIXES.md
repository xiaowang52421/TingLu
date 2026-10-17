# 2026-10-16 Unity Failure Fixes

请在 `D:\听路_街区升级` 中修复 Unity 首轮 EditMode 验证报告的 A24 与 `RecoveryBackendFailurePausesProtectsLastGoodSaveAndRetryRestoresPlay` 失败。首轮结果为 142/137 通过、5 失败；本轮只处理这两个真实车辆接触集成用例及其必要 handoff 说明。

先追查 `StreetSessionIntegrationTests.cs` 中玩家从正式出生点 `(-5.5, -16)` 经玩家输入走到 `x=-1.35, y=-16` 后，为何等待 5000 个 0.02 秒 tick 仍未发生车辆失败。检查 Session 菜单/时钟状态、玩家当前位置和状态、碰撞半径与车辆接触顺序、NS 南向路线闭环及交通灯停车位置。用源码和现有失败诊断形成原因结论，再据此修复测试路线或同步条件，让正式生产 Session 的真实玩家移动与真实活动车辆产生接触。

禁止合成或直接调用碰撞/失败/恢复事件，禁止把车辆删除、静止、停用或改变交通相位来绕过失败。保留正式出生点、正式路线与 Session 事件流。不得启动 Unity、Unity Editor 或 Unity 测试；源码检查可行，最终测试状态仍由总控统一运行。

仅修改本次修复所需的 Integration 测试和对应 handoff；不要改动需求复核报告、生产玩法实现或其他测试范围，除非诊断确认生产实现存在必须修正的缺陷，并先向主控说明证据。完成后冻结源码，报告根因、改动文件、测试方法和未执行的验证。
