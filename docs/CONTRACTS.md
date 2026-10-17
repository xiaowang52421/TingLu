# 公共契约 0.2.0

发布状态：CONTRACTS_READY（源码契约可用；不表示 Unity 编译或 Demo 验收通过）。唯一正式声明位于 Assets/TingLu/Contracts。公共修改仅由 00 执行，新增字段先写交接请求。

0.1.1 增补 IAudioSpeech.Speak/StopSpeech 与 SpeechRequest，03同一个调度器实现并共享 IAudioPlayback.Receipt；06使用文本和SupersessionKey合并过时焦点。影响03/06/00装配，现有签名保持兼容。语音失败须真实回报，不能以合成短音替代语句。

0.2.0（恢复执行后）：新增可选IWorldOcclusionQuery精确遮挡边界、IWorldMovement运动解析、MovementResult。ObjectDefinition/AcousticQueryResult/Observation明确PerceptionSemantic；声学结果携带合法类别与材质，感知无需再查询声源当前世界状态猜分类。AudioRequest增加SourceId/Gain/Occlusion/Loop，Gain为共享模型最终值，禁止Unity距离衰减再次扣减。UserSettings增加Palette/FontScale/VoiceprintIntensity。ResumeData独立保存FiredBeatIds/ActivatedCheckpointIds/Finished，防续玩重复触发。旧构造参数保留为可选扩展，存档缺字段必须采用明确默认值。影响01–06及00装配，已同步三个组。

遮挡边界查询返回与查询圆相交的全部世界坐标线段（含端点在圆外但中间穿过范围的边界）。02以边界做保守裁剪；未提供该扩展时不生成“空置证明”，避免删除遮挡后的旧记忆。运动解析返回身体可达位置及实际接触点，不把调试真值传给玩家表现。

## 数据与单位

米、秒、角度；玩法 XY，+Y 向上。出生朝向由关卡确定，停止保留朝向。角度零轴 +Y，正角逆时针。音频位置 (x,0,y)，朝向 (fx,0,fy)；左右符号通过 Coordinates.Bearing 的四朝向测试验证。

Point2/Segment2 为值类型；所有集合在构造时复制为只读。定义轮廓为局部坐标；观察、记忆和探测区域为世界坐标。对象定义和运行位置分离，不向展示传 Transform。定义 ID 如 l1-1/pole/01，事件 ID 用 GUID（跨恢复不复用），片段 ID 用独立 GUID。

ObjectDefinition.Category 和 MemoryPolicy 由配置固定，动态停车不变静态。Observation.Source/Kind 明确信息来源。PassiveSound 仅允许 SoundLocation 且无几何；StaticMemorySnapshot 只接受静态合法几何观察。局部确认不能扩展为对象完整轮廓。

ProbeCoverage 是 02 生成的实际探测凸多边形单元及遮挡边界并集。02 负责几何裁剪、空置证明和记忆片段拆分；不得把无遮挡的整个半径圆盘当作默认有效覆盖。FragmentId 表达同 ID 对象的多个位置片段。

## 服务成员

| 接口 | 实现负责人 | 约定 |
| --- | --- | --- |
| IGameClock / IEventBus | 00 | SimulationClock / EventBus，主线程、模拟秒、同步消费；音频回执异步入主线程 |
| IInputSource / IPlayerController | 01 | Read / Tick / Restore；Restore 将冷却和效果剩余时间从 capturedAt 重映射到 now |
| IWorldQuery / IWorldLifecycle | 01 | 附近对象、材质、遮挡；事件状态捕获、恢复和释放 |
| IAcousticQuery / IAudioPlayback | 03 | Sample 和 Play/Stop/UpdateListener/Reset/SetPaused/Receipt；共享声学参数 |
| IPerceptionService / IMemoryCheckpoint | 02 | Recompute、ClearTransient、Probe、Contact；Export/Restore 静态片段 |
| ICheckpointService / ISaveService | 05 | 激活、恢复；进度/设置/续玩分别加载保存 |
| ISemanticFeedback | 06 | Submit 已知语义、ReplayGoal；不查询完整世界 |
| IPerceptionReader / IMemoryReader / IPlayerStateReader | 相应模块 | 展示只读入口，04 只依赖这些与设置 |

精确字段和签名以 Model.cs / Events.cs / Services.cs 为准。连续状态走只读查询，一次性事件走事件总线。总线按订阅顺序消费，重入发布排队；单消费者异常不吞掉，完成派发后汇总抛出。订阅返回 IDisposable，切关全部释放。

GameplayEvent 使用类型明确的 Payload：CaneTap→CanePayload，Footstep→FootstepPayload，Collision→CollisionPayload，其余→SemanticPayload。全局事件坐标与对象 ID 可空。带 worldPosition 的内部事件不可直接向正式字幕泄露；06 应在合法信息路由后映射。关键消费者按 EventId 幂等，不在事件总线全局吞重（避免破坏调试）。

优先级 CriticalThreat 30 > Navigation 20 > Interface 10 > Ambience 0。AudioReceipt 关联 EventId 和 RecoveryGeneration；Requested/Ready 不开启危险。Started 是软件播放起点，前兆从该回执的模拟时间开始，仍需硬件延迟测量。超时/失败取消或延后危险。不得等待整段音频结束。异步回执只在主线程更新业务。

CheckpointSnapshot 不包含当前感知或 AttemptStats。ResumeData 将检查点和本次尝试统计一起保存；检查点重试不回滚统计。SchemaVersion=1，存储 DTO 使用显式序列化转换，不直接依赖 Unity JsonUtility 对私有属性的行为。未来版本禁止覆盖。

## 更新和恢复

每步：处理暂停与菜单 → 模拟时钟推进 → 输入/角色和世界 → 事件/声学/音频请求 → 20Hz 感知（危险可提高）→ 合法静态记忆 → 表现/语义反馈 → 检查点/目标。敲杖瞬时采样，动态残影随后不重新读取真值。

恢复事务由 00 装配：冻结输入和危险时钟 → RecoveryGeneration++ / 清理旧回执与声音 → 恢复世界事件/随机种子/角色/静态记忆 → 保留 AttemptStats → 重建动态对象 → 同步监听器和声源 → 清理并重算感知 → 播放所需预警 → 后端就绪后开放输入。失败在冻结前的活动时钟上计时 2 秒，菜单暂停冻结演出。恢复期间不能用暂停的模拟时钟计等待超时；后端初始化超时由单独服务计时且只用于故障退出。

程序集单向依赖 Contracts 和 Config；业务模块不相互引用实现，总控 Runtime 统一装配。测试替身只能用于测试。禁止以空后端替代正式完成状态。
