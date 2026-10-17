# 真实组件探测耗时测量

audit_runtime复用 gpt-6-luna/xhigh 请求配置。独占 Integration/StreetSessionIntegrationTests.cs 和对应handoff，禁止Unity。00正在协调性能优化。

在现有测试文件添加一个有意义的连续探索回归兼测量：用正式StreetLevelFactory、World、SimulationClock、Player、Perception、Memory按 RootMemoryRouteDiagnostic.cs 原路径及原敲杖间隔重放，分别测Perception.Probe和Memory.Apply，记录每次probe坐标/片数/存量/耗时与汇总（TestContext.Progress；若TINGLU_UPGRADE_EVIDENCE_ROOT可用可写JSON）。覆盖至少此前t8.28卡顿点及连续多次探测，不改变玩法参数，不给机器速度硬PASS门；用合法非空地面、无异常、正确到达及面积/边界行为作断言。不要复制诊断程序直接当生产行为证明，沿正式组件API。

不得改生产、共享Contracts/asmdef、Acceptance路线、用户存档或启动Unity。测量数据是Editor真实运行阶段耗时，不冒充实际设备声音延迟或真人体验。尽快READY冻结，由00统一跑。
