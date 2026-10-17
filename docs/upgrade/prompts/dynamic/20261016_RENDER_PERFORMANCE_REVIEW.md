# 渲染性能独立只读复核

请求 gpt-6-luna/xhigh，新独立上下文。用户授权按需添加Agent，加快性能整改。

项目 D:/听路_街区升级；先读 docs/upgrade/IMPLEMENTATION_CONTRACT.md 与 ARCHITECTURE_ADVICE_DECISIONS.md，以及 Presentation/StreetRenderPlan.cs、StreetRenderer.cs、Bootstrap/Runtime/StreetEntry.cs。原D:/听路只读，所有Unity归总控；禁止安装工具、新建聊天、启动Unity或修改Assets。

本轮刚修退化面积和Perception碎片爆炸，同指纹149/149 EditMode、2/2 PlayMode与Build已通过，但离线第三次Probe+Memory约472ms，用户要求优先性能整改。geometry_fix独占Memory/Role05，audit_runtime独占Integration测量，00独占Perception/Session，勿并写。

你的任务：只读分析每帧Render中在memory/frame未变时重复三角化与mesh重建，提出最小可安全缓存方案，明确什么变化须失效（currentUntil、now、palette、Display、memory对象、perception、临时高度等），避免隐藏未知/存档恢复语义改变。可写 docs/qa/upgrade/RENDER_PERFORMANCE_REVIEW_2026-10-16.md；不要空泛列GC建议。先简短给总控可操作结论，不等待长报告。性能必须有测量，静态阅读不冒称优化实测。

后续00明确开放实现：仅独占 Presentation/StreetRenderer.cs 与 Role08/StreetRendererTests.cs。先做有界最小surface batch重建缓存：同一immutable StreetMemoryFrame、Palette/Display、renderer transform不变，且now未倒退/未跨ObservedAt或CurrentUntil边界时跳过RenderSurfaces的清网格/三角化/上传。strokes/player/声音alpha继续每帧更新。Clear必须使cache失效。不要缓存整RenderPlan，不改Contract/Perception/Memory/RenderPlan，不增加全图查询。先不做复杂无限triangulation字典。加能证明重复帧跳过上传、时效/调色/AudioOnly/恢复与Clear正确的少量行为断言，可用internal rebuild计数辅助。禁止Unity，READY后冻结交总控统一验证。
