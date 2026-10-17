# 探测记忆性能整改

用户明确指出约0.47s反馈过慢，要求解决。请求执行配置沿用 gpt-6-luna/xhigh。

geometry_fix独占 Memory/StreetMemory.cs、Role05/StreetMemoryTests.cs 与几何handoff。目标减少 MergeAdjacentConvex 每合并一次就重建全组边字典的重复工作；允许确定性的增量边索引或一轮多对无冲突合并。保持已有许可边界、临时物局部差集、ObservedAt/CurrentUntil、面积、去重和保存语义。不可粗过滤细片、删除永久记忆、缩小扇形/探测次数或以放宽阈值换性能。

复用 output/pure-probe/RootMemoryRouteDiagnostic.cs：原真实路线第三probe 1766输出片，Probe+Apply约472ms，末段memory976。先给针对性优化与前后同路线数值；此离线观测不替代Unity。共享Contracts与Perception归00，不可并写。

不得启动Unity/安装工具/修改原D:/听路。只写升级副本，READY后先冻结Assets再更新handoff。性能断言不要使用机器依赖的硬毫秒门，测试重点是覆盖/面积/局部更新语义与确定性。总控随后在Unity测实际阶段耗时和完整路线。
