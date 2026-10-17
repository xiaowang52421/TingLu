# SESSION_BOUNDARIES 完整派发提示词

任务：audit_runtime续接，配置继承已请求gpt-6-luna/xhigh。父任务总控，StreetContract 1.0.0。

目标：在已有A26用例之外补当前可自动验的真实Session边界，避免把无需真人的行为一直列为未测。完整读本轮01–04规范、角色12提示词、IMPLEMENTATION_CONTRACT、StreetSession、StreetLevel以及现有Integration用例。原项目只读，Unity排他归总控。

唯一可写源码：Assets/TingLu/Tests/Upgrade/Integration/StreetSessionIntegrationTests.cs；只在此新增测试与必要私有辅助，不改变原断言，不修改生产。总控没有并写此文件。另可修正你的docs/qa/upgrade/REQUIREMENTS_RECHECK_2026-10-16.md里A34/R23口径为本批提示词事后补记缺口，并写docs/handoffs/UPGRADE_SESSION_BOUNDARIES.md。其他文件只读。既有asmdef不变。

用例一A24：正式StreetLevelFactory/Create+Session，出生后在第一个检查点激活前沿实际可行路径进真实车流，真实车辆扫掠导致恢复到Level.Spawn/CheckpointId spawn。累计一次碰撞/恢复，继续若干模拟秒安全且无立即重撞。不得手动调用恢复、注入假接触、挪动车辆或修改出生/关卡。

用例二A22：正式Session按实际路线走到过街起点，在与该过街方向对应的红灯相位中穿过，路线需可证明相位一致、真实交通仍工作；合理选择时机避开车辆，以隔离红灯通行本身与合法车撞失败。断言抵达、勇气未扣、无事故/恢复、没有入车道警告播报；允许脚步/环境声。注意不能误把与车流平行的过街方向相位搞反。若正式路径不可在红灯全程完成，记录原因和精确反馈，不放宽或伪造相位。

固定0.02模拟步，不使用sleep/墙钟逻辑，不越过玩家移动输入；无需增加无关大量测试。可用既有程序集做有界离线逻辑探针，但不得启动Unity、改生产或场景。总控在完整Assets冻结后统一运行。未运行真实测试保持NOT_RUN；交接READY后停写并回报changed_files、测试名、路线/相位说明、真实证据与限制。
