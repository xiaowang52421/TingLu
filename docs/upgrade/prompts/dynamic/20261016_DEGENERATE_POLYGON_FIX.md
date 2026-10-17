# PlayMode 真实路线退化几何故障修复

总控授权复用 geometry_fix（请求配置 gpt-6-luna/xhigh）。先读 IMPLEMENTATION_CONTRACT、现有几何handoff和本次失败 XML：output/unity-verification/20261016-015920-70b96423/playmode.xml。

目标：A25AndA30 生产 Session 在 WalkTo 探索时因 CopyPolygon 的 Degenerate polygon 终止。定位生产 Perception/Memory 产生退化片的路径，保持未知区域/遮挡和面积语义，修复并加能复现的回归。不要放宽共享 CopyPolygon 守卫、吞异常、删验收断言或修改测试路线来绕过。

独占允许修改 Assets/TingLu/Memory/StreetMemory.cs、Assets/TingLu/Perception/StreetPerception.cs、Role04/Role05 对应测试及你的几何handoff。如确认问题在别处先报告。共享 Contracts、Session 和 Acceptance 由总控处理。不得启动 Unity、安装工具、改原 D:/听路 或创建聊天。

总控将补 Session 异常完整堆栈并排他运行诊断。交付请列根因、最小修复、回归、待验证项，READY 后冻结 Assets。更新 UPGRADE_GEOMETRY_RECHECK.md 保留历史证据。

02:13补充：00独占修改 Contracts/StreetGeometry.Area 为局部原点 double 累加，float签名/单位/阈值不变，Role00补平移细片绕序/拷贝和真共线回归。Agent可使用此版本离线复放，重点确认原真实路线是否修复；不可另加末端筛片绕过。geometry_fix若用 output/pure-probe/MemoryRoute* 保存诊断源码与日志亦在授权内；需注明是纯源码离线证据，不能替代Unity。

后续所有权调整：00接管 Perception/StreetPerception.cs，Agent明确尚未编辑，禁止并写。00实施无交集短路与原凸面claimed mask修复；Agent独占Role04/05测试与几何handoff。RootMemoryRouteDiagnostic.cs/observed.txt归00，保留原真实路线/探测次数及前后片数，新增到稳定快照。
