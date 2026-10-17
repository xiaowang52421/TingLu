# 2026-10-16 总控继续交付：接口与文件所有权

用户已要求继续完成。上一轮C272是可运行检查点，不是完整交付。现在解除以下指定文件的冻结；其余仍只读。原D:/听路、原存档/试玩只读，不安装升级依赖。Unity/Player由00排他运行。各角色先读对应完整提示词，未运行测试标NOT_RUN，READY即冻结。子Agent不得自行改需求、扩大范围或改他人文件。

## 00冻结的接口

- StreetContract.Version 保持 1.0.0，SaveSchema=1。高程字段作为 schema-1 可选扩展写入；旧档缺高程参数迁移为0，校验checksum仍使用原封装 contract，不把合法旧档当未来档或擅自删除。未来档主/备份保护不变。这样保留已有存档契约兼容，避免为可选字段制造无必要的版本断裂。
- StreetSurface / StreetKnownSurface：现有构造尾部textureAxis后追加可选float heightBase=0, heightSlopeX=0, heightSlopeY=0；readonly HeightBase/HeightSlopeX/HeightSlopeY，HeightAt(Point2)返回 h=b+sx*x+sy*y，参数须有限。裁剪复制保持三个系数，Memory分组合并键包含它们。
- 地图人行道、路沿顶面常规高度H=0.15世界单位（地图米），道路为0，坡道连接0/H。具体覆盖冲突须依据当前地图裁剪优先级校正，不可照搬不连续的草案。保留2D移动/碰撞，展示角色和物体按其位置的已知面高程抬起，不读完整地图补未知区。
- StreetWorldFrame构造末尾追加 double? nextSignalChangeAt=null；readonly double NextSignalChangeAt（缺省time，故旧fixture不能凭空许可持续灯态）。世界生产快照用同采样时刻NextPhaseChange得到严格将来边界。
- 新 StreetSignalObservation：readonly string ObjectId；Point2 Position；bool IsGreen；double ObservedAt,ExpiresAt。构造同序，需合法ID/有限点和时间，ExpiresAt>=ObservedAt。
- StreetProbeResult构造末尾追加 IEnumerable<StreetSignalObservation> signals=null；readonly ReadOnlyCollection<StreetSignalObservation> Signals（缺省空）。只Probe对已实际确认灯具轮廓的同ID信号产生状态，受真实遮挡；有效期min(now+painSeconds,NextSignalChangeAt)，无正有效期不产。Contact和声源观察不授予灯态。
- street.signal.ns/ew保原稳定ID和音相，改Permanent灯具小轮廓、Height=2，非实体碰撞/非遮挡。红绿取本次SoundActive；永久Memory只记外壳，无动态状态。状态不入Memory/DTO。
- StreetSession公开IReadOnlyList<StreetSignalObservation> Signals（快照列表）。Probe结果交Memory后按ID更新临时灯态；每次RefreshSenses剔除过期，Start/Recover/Return清空。不得读取World补颜色。
- StreetRenderer.Render 和 StreetRenderPlan.Create末尾可选 IEnumerable<StreetSignalObservation> signals=null。仅有效观察且同ID灯具已有许可轮廓，画当前状态；过期中性外壳，AudioOnly无图。不从World获取。地面逐顶点HeightAt；路沿立面只在双方均已知且有高度差的公共边段创建，禁止外缘补未知地面。

## 排他写权

|角色|写权|目标|
|---|---|---|
|00|Contracts/StreetContracts.cs；Bootstrap/Integration/StreetSession.cs；Bootstrap/Runtime/StreetEntry.cs；共享契约/主控状态与最终汇总|共享API、接线、审核、统一测试构建和交付|
|map_traffic_audit|World/StreetWorld.cs；Levels/StreetLevel.cs；Role03/Role09测试；UPGRADE_MAP_TRAFFIC_AUDIT.md|R15地图灯具/同源相位边界；D12连续地形/8坡道及路线检查|
|architecture_review|Perception/StreetPerception.cs；Role04测试；UPGRADE_PERCEPTION_COMPLETION.md|高程保留、遮挡许可灯态|
|geometry_fix|Memory/StreetMemory.cs；Role05测试；专属output/pure-probe诊断；UPGRADE_GEOMETRY_RECHECK.md|高程保留/分组；重复探测增长逐片证据和最小修复，禁止阈值吞片|
|audit_runtime|Persistence/StreetSaveService.cs；Role10测试；UPGRADE_PERSISTENCE_COMPLETION.md|1.1高程往返、旧档迁移及未来保护|
|render_performance_review|Presentation/StreetRenderer.cs；StreetRenderPlan.cs；Role08测试；UPGRADE_RENDER_COMPLETION.md|已知面高程、合法路沿立面、临时灯态及缓存正确性|
|stationary_vehicle_review|Audio/UnityAudioPlayback.cs；StreetAudioController.cs；Role06音频测试；UPGRADE_AUDIO_COMPLETION.md|池拒绝有界上下文与有证据的最小修复|
|verification_review|tools/Verify-Unity.ps1；tools/upgrade/Verify-StreetPlayer.ps1；UPGRADE_VERIFICATION_COMPLETION.md|前置失败留证、暂停菜单全屏探针；Assets诊断改动须申请|
|final_qa|DELIVERY_GAPS_RECHECK_2026-10-16.md（后续另授最终验收）|独立核剩余功能与实际新证据|

所有Agent回报根因、精确文件、接口依赖、未跑检查；不把新源码套用C272 PASS。H及灯具尺寸是00常规实现决定，不等待用户再次批准。
