# 街区正式接口与接线（G0）

2026-10-16 材质纹理方向补充：StreetSurface / StreetKnownSurface 新增 readonly Point2 TextureAxis，构造尾部可选 `Point2? textureAxis=null`。Known缺省世界X，源Surface对无方向材质默认世界X；tactile/ramp取完整原面最长边，crosswalk取其法向，使白条横跨行走方向。可显式指定方向覆盖默认推断。Canonical拒绝零/非有限值、规范反向并按最大分量归一，复制幂等；Renderer在投影前再取欧式单位向量，以世界原点保持相位连续。只有合法裁片携带材质方向，不暴露完整源面。Perception缓存、Memory裁剪/复制/按轴分组合并、保存读回必须保留此字段；同材质不同轴不得合并。Schema1新增可选textureAxis，旧档缺失默认为世界X，不能从旧裁片逆推原方向。该变更保证工程源码重编译兼容，不承诺旧外部已编译程序集的二进制兼容；本工程无此插件交付要求。改后必须统一验证，不继承CAD6588阶段结果。

2026-10-15，StreetContract 1.0.0。正式唯一声明位于 Contracts/StreetContracts.cs、StreetGeometry.cs；集中配置 StreetConfig.cs。旧契约0.2.0暂保留，新街区禁止使用旧InputFrame/360°Probe作为玩法输入。用户已说明旧版已存档，优先新版本，旧首关/旧编辑器兼容回归不作为交付阻断。

## 坐标与配置

Point2 (X,Y) → Unity (X,高度,Y)。地图北为+Y；朝向默认(0,1)，正yaw顺时针朝东。镜头yaw=0、tilt=52°，位于角色南侧上方，看向北；正交size=9、距离20。W=(0,1)，D=(1,0)，S=(0,-1)，A=(-1,0)，由StreetCoordinates.ScreenMove归一化。鼠标delta.x×0.15度，无deltaTime，反向由StreetConfig.InvertMouse注入。扇形完整夹角100°、半径沿用7，显影0.8秒，冷却/专注/混淆沿用GameplayConfig。上述数值均待试玩调节。

材质与声音唯一目录 StreetCatalog。NS/EW信号轴字符串固定为"ns"/"ew"，null无信号。14秒绿、3秒清空、另一向14秒绿、3秒清空；完整周期34秒。世界相位名"ns"/"ew"/"clearance"；声音启停严格跟对应过街相位（行人过街方向与平行车流一致）。交通角色可提出精确修改请求，不自行复制目录。

## 各角色须交付的正式入口

所有生产类放既有namespace TingLu.Input/Player/World/Perception/Memory/Audio/View/Presentation/Levels/Persistence/UI 内。以下为联调固定API，私有辅助实现自由；公共变更提交00。

|角色|接口/实现签名|
|---|---|
|01|StreetInputSampler : MonoBehaviour,IStreetInput；Configure(StreetConfig)，Read()，SetGameplayActive(bool)，Clear()。失焦自行释放并通过public bool HasFocus提供状态；00将其用于暂停。菜单返回StreetInputFrame.Menu/PausePressed，无ListenHeld。|
|03|StreetWorld : IStreetWorld；构造(IEnumerable<StreetSurface>,IEnumerable<StreetObject>,IEnumerable<StreetRoute>,StreetConfig)；Tick(delta,paused)，Capture()返回同采样时刻快照；Move做真实静态碰撞；VehicleContacts基于上一Tick至当前Tick车辆扫掠与玩家from/to；ExportTraffic/ImportTraffic恢复时间及路线累计距离；公开MoveObject(id,newPosition)/RemoveObject(id)供测试临时挪动。世界对象Polygon为世界坐标，Position是中心，移动时平移值副本。|
|02|StreetPlayerController : IPlayerStateReader；构造(StreetConfig,IGameClock,IStreetWorld,PlayerState)；Tick(StreetInputFrame,double)，Restore(PlayerState,double capturedAt,double now)；事件Action<StreetProbeRequest> Probed、Action<StreetContact> Contacted、Action<StreetFailure,string> Failed、Action<string,Point2> Stepped。SetFinished()。物理推进顺序世界Tick→角色Tick；失败回调内可能恢复，Tick须在失败事件后立即返回。|
|04|StreetPerception；构造(StreetConfig,IGameClock)；StreetProbeResult Probe(StreetProbeRequest,StreetWorldFrame)；StreetProbeResult Contact(StreetContact,StreetWorldFrame)（接触只在接触点附近≤ContactRadius）；PerceptionFrame ProjectAcoustics(IEnumerable<AcousticQueryResult>,double now)；Clear()。04唯一产Observation/PerceptionFrame；几何覆盖仅由Probe/Contact产生。|
|05|StreetMemory : IStreetMemory；无参构造；Apply/Export/Import(frame,stale)/MarkStale。只操作许可输入，连续面积并集可用互不重叠凸片表达，局部差集必须精确。显影由CurrentUntil比较模拟时间，临时旧记录不追踪世界。|
|06|StreetAcousticModel : IAcousticQuery；构造(Func<StreetWorldFrame>,IPlayerStateReader,StreetConfig)；Sample(listener,facing,time)唯一生产AcousticQueryResult。SoundId由SourceId对应的已采样声音目录绑定供播放，可在该实现提供string SoundFor(string sourceId)，不得供渲染查询；StreetAudioController构造(IAudioPlayback,StreetAcousticModel)，Sync(IReadOnlyList<AcousticQueryResult>,int generation)、Reset(int generation)，无Unity依赖，基于同一结果开始/更新/停止声音。若更新IAudioPlayback需00批准，优先停止/重发位置变化的一次短音或使用06自身注入更新委托。UnityAudioPlayback补齐StreetCatalog占位音/脚步，沿用(X,0,Y)监听器映射及角色朝向。|
|07|StreetCameraRig : MonoBehaviour,IStreetCamera；Configure(Camera,StreetConfig)，Follow(Point2,bool snap)。只跟位置；无世界引用。|
|08|StreetRenderer : MonoBehaviour；Configure(Camera,int layer,StreetConfig)，Render(StreetMemoryFrame,PerceptionFrame,PlayerState,UserSettings,double now)，Clear()。只接04/05许可帧；可创建自身材质/纹理，不直接World查询。角色位置/朝向箭头可画，未知环境纯黑；几何高度只依据已知局部片段，禁向未知处延伸投影。|
|09|StreetLevel类（Levels私有内容模型）；string Id/Title/GoalText，PlayerState Spawn，IReadOnlyList<StreetSurface> Surfaces、StreetObject Objects、StreetRoute Routes、StreetCheckpoint Checkpoints；Point2 Goal、float GoalRadius；static StreetLevel Create()或StreetLevelFactory.Create()。交付真实StreetCrossroads.unity或Level内可复现生成器。Bootstrap入口脚本由00提供StreetEntry.cs，09场景绑定此类型（等待00通知），场景可先写生成说明。运行时新格式与旧LevelDocument隔离，建议后缀.street.bytes避免旧编辑器JSON截断。|
|10|StreetSaveService : IStreetSaveService；构造(string persistentRoot,string saveNamespace)，Save/Load；schema=1，容器kind="tinglu.street"、contract="1.0.0"、levelId。显式DTO不可复用旧CheckpointSnapshot。原子备份、checksum、未来保护（含备份）与坏档策略；实际目录仅由注入根+命名空间组成；临时记录读档CurrentUntil=0，活动声源绝不入档。另提供保存settings的需要可请求00，现有LocalSaveService在隔离根调用即可。|
|11|StreetHudPresenter : MonoBehaviour,IStreetHud；Configure(AccessibleMenu)，Present(PlayerState,UserSettings,string subtitle,bool playing)。可内部挂AccessibleMenuPanel或独立画菜单；正常菜单按钮保留，玩法不画操作按钮/脚下文字。00用AccessibleMenu处理输入与回调、11只呈现；新帮助必须自然说明WASD/鼠标/右键/被动听觉。菜单输出通过已有Subtitle，返回玩法必须清输入。|

## 恢复和存档

StreetSaveData同时持有当前Player/Traffic与最近CheckpointPlayer/CheckpointTraffic；Memory为全关累计合法记忆，Collisions累计统计，ActivatedCheckpoints防重复激活，Generation隔离。恢复由00冻结输入与clock，generation++，audio.Reset，世界ImportTraffic（回检查点模拟时间），角色Restore，memory.MarkStale，重新监听/感知/声源后再开启输入。即使世界时间回退，所有旧临时/永久快照CurrentUntil置0以防倒时钟冒充当前探测。累计永久探索/碰撞不回滚。无检查点使用安全出生快照。事故由02 Failed回调统一处理，一Tick最多一次。

G0世界/声学/记忆fixture位于Tests/Upgrade/Shared，测试替身只在TestAssemblies。Role00–11为Editor单元，Integration为Editor跨模块逻辑，Acceptance为不限Editor的PlayMode。Unity验证00排他；任何业务角色不得开编辑器。

G0实施补充（首次派发后、无实际地图消费者前）：StreetConfig增加CameraTargetOffsetX/Y/Z=0和CameraFollowSharpness=10，由07消费。StreetRoute增加可选float? StopLineDistance（构造尾参数stopLineDistance=null），表示从Points[0]沿路线累计到停止线的中心距离，09配置受控路线，03实现红灯停止；已过停止线的车辆应继续驶离冲突区。单路口只需一条停止线；车身前沿/停车裕量由03与09约定。此次属1.0.0补齐，尚无序列化历史数据。

00接线实现StreetSession.cs/StreetEntry.cs已写草稿，依赖后续业务波次；暂用TINGLU_STREET_INTEGRATED编译条件隔离，所有模块交付并通过局部检查后由00移除条件并真实编译。它不是正式可运行入口的证据，不使用空生产替身填补依赖。StreetEntry脚本.meta现已稳定，09可引用该GUID创建场景，最后由00启用。

世界联调补充：StreetRoute.Offset统一为初始累计路径距离（米），不是时间；Traffic.Distances保留绝对累计距离。StreetObject尾参数新增float? contactDamage/contactStun（默认null），对应StreetContact.Damage/StunSeconds；03传递及平移时保留，02空值回退旧GameplayConfig.damageStatic，09停放自行车可设15。World.MaterialAt未定义区域返回null，不假报人行道；02该处可不发材质脚步（需避免footstep.null）。世界时间驱动street.signal.ns/ew源的SoundActive，clearance两者关闭，06不另算信号。

音频接线补充：正式IStreetAudioUpdater.UpdateSource(string eventOrSourceId,Point2 position,float gain,float occlusion)已由00声明；06令已有UnityAudioPlayback实现该接口（原方法签名已经相同），StreetAudioController可检测backend as IStreetAudioUpdater来同步同一冻结声学样本，不必每帧重发循环声。Bootstrap仍可调用原具体方法，后续消重由00处理。
