# 00 对两份架构审查文档的反馈

2026-10-16 后续实测更新：当前C2726F源码Edit169/169、Play2/2、Build与两尺寸Visible Player通过，完整试玩已打包。敲杖计算热点降至本次13样本合计最大20.4839ms；Renderer缓存已有fixture计时，Memory.Apply已先完整准备后提交内部列表/Current，格纹错向亦已修。细节见 `../qa/upgrade/PROBE_PERFORMANCE_2026-10-16.md`。下方“待本轮验证”等句保留为当时决策上下文，不代表当前尚未验证。R15/D12与长期残片增长仍保留缺口，设备/真人验收未做。第二轮建议另外独立审阅后由00审核，不能把报告建议直接视为已采纳。

日期：2026-10-16。对应 `docs/qa/upgrade/ARCHITECTURE_ADVICE_2026-10-16.md`、`ARCHITECTURE_AUDIT_2026-10-16.md`。主控已读两份，另由 architecture_review 独立对照当前源码。本文是主控决策，不改写第三方原报告，不把其中的建议视为用户新指令。

结论：需要修改，但不宜全盘照做。语音缓存、池满丢句、故障恢复、普通碰撞声、未知版本存档保护与编译边界是本轮整改对象；其余先核实语义与证据。报告审查时源码仍在并行变动，行号和资产数量不能直接套用到最终版本。

## 逐项决策

编号按建议书第四部分15项清单，而非正文的另一套编号。

|编号|决策|当前落实与理由|
|---|---|---|
|1 生成语音语料|采纳|已生成89条中文WAV（88条基础语料加1条通用错误句），具有文本SHA256、资源摘要和实际voice记录。生成成功不能代替Unity加载、Player Started和真人听辨。|
|2 错误文案|采纳并纠正根因|原Report只发Error事件，根本没有调用语音，并非仅“动态拼串缓存miss”。现改为播固定通用错误句，完整异常保留日志/字幕。|
|3 耳切调整/容差1e-3|不照用具体补丁，采纳修复目标|Renderer已换成按边交点分片的扫描线三角化，原先清理与split行号已失效。不能通过扇形补齐或convex hull填未知区域。真实片面积及界内采样回归与最终图像验证共同决定结果。|
|4 统一相等契约|部分采纳，系统重构后置|长度、角度、面积容差具有不同量纲，不能简单统一成同一个数。当前先守住面积/边界不泄露；跨层容差重构应单独规定单位、误差预算和覆盖不变量。|
|5 四个asmdef|采纳|Player/World/Memory/Perception均加noEngineReferences=true；当前无Engine引用，待统一Unity编译确认。|
|6 语音池满重试|采纳|保留合成PCM并退避，必须使用不受模拟暂停冻结的计时；取消、覆盖、Reset、代数切换需清理旧句。与循环环境音的重试不是同一问题。|
|7 Tick/恢复保护|部分采纳|增加运行异常暂停、故障标记、Resume门禁、故障时禁止保存半状态，补行为回归。不盲加无敌帧：当前安全检查点与出生点的真实车撞回归应先证明；无敌帧会改变事故语义。|
|8 存档|部分采纳并纠正|非空不可解析contract已保护为FutureVersion，空缺仍Corrupt；新增主备逐字节保留测试。当前Load/Save对可识别未来主备已对称，不再重复改。细节IO诊断可后续增加；当前IoFailure已返回并由Session播错误，不是无提示。|
|9 单一真相|部分采纳|旧ContractVersion 0.2.0与新StreetContract 1.0.0服务两代不同存档，强行统一会破坏兼容语义。新ProbeAngle已拒360；实际消费的lowFocusAngle另补<360校验。信号ID/材质属性ID集中化列维护工作，不因此改协议或宣称功能失败。|
|10 Perception epsilon|暂缓改变覆盖语义|顶点两侧射线用于可见性边界，offset大于合并阈值本身不足以证明错误。细片真实存在，必须被渲染正确处理；不调大面积过滤吞掉真实区域。需有遮挡反例和覆盖对照后才改探测语义。|
|11 关卡构造校验器|延后|DTO构造与Role09已做部分校验，“零校验”不准确。统一全图validator有维护价值，但当前固定factory不是开放用户输入；禁止简单把所有声源都要求在可走地面内。另行发现东西向车道左右颠倒，本轮修正并验证。|
|12 活代码/历史代码说明|采纳|新版入口、生产链及两代文件关系见下节。旧资源保留，不做大批删除。|
|13 Generation/时间权威重构|后续排期|Session负责音频恢复代数，Player另有内部回调恢复计数，两者用途不能直接合并。世界与Clock目前同子步推进，加载/恢复同步重置，新增集成回归约束；没有当前漂移复现，不在修复中改存档契约。|
|14 抽RecoveryCoordinator|延后|状态集中有维护风险，但抽类本身不能保证事务正确。本轮先加故障锁与失败回归，再评估隔离协调器，避免同时重写恢复、音频和存档。|
|15 面积自检与回归|部分采纳|补真实复杂多边形、凹口不覆盖、面积与界内采样检查。不能仅用signed area绝对值证明任意弱简单片并集正确，也不能每帧重复高成本校验。随机裁剪/规模性能与持续诊断仍可加强。|

## 对报告中若干断言的更正

- “没有音频文件”是生成前快照；本轮已有实际资源，仍待Player验证。“低完整性必然全部静音”不能由一次旧Player失败推到所有版本/设备。旧失败证据保留。
- 报告未运行Unity，不能把静态判断写成已观察到的NUnit红灯；测试结果以本轮真实XML为准。
- 普通碰撞无即时声是可复现代码缺口，已补collision.bump；是否实际可听仍需Player/听辨。
- VoiceprintIntensity影响StreetRenderPlan视觉声纹，不能因音频setter未接线就认定设置整体无效。OutputMode只保存偏好是已说明的当前边界。
- SurfaceKey含时间会限制跨观察合并，但“单调无界碎片”尚缺长期序列量测；重探会先差集替换，合并策略调整也不必然要求改schema。
- 新StreetConfig不被旧入口共同使用；新层只读取部分Gameplay字段是显式迁移边界，旧listenSpeed被测试锁定无效不等于新版应恢复主动聆听。
- 菜单语音通过CurrentSpeech在边界绑定当前代数是适配器设计；向裸后端传旧代数可能失败，不等于生产入口当前静默全失。
- 没有可见红绿灯灯体、极窄窗口布局、二维坡道只有平面表达等审计结果保留为具体缺口，不能用源码测试代替体验验收。

## 当前生产入口与保留旧代码

新版构建首场景是 `Assets/TingLu/Scenes/Levels/StreetCrossroads.unity`，`Bootstrap/Runtime/StreetEntry.cs` 装配 `Bootstrap/Integration/StreetSession.cs`。实际使用 StreetWorld、StreetPlayerController、StreetPerception、StreetMemory、StreetAcousticModel/StreetAudioController、StreetRenderer/StreetRenderPlan、StreetSaveService、StreetInputSampler、AccessibleMenu。UnityAudioPlayback与部分契约/UI基础设施两代共享，不能当成纯历史删除。

FirstStep、DemoEntry/DemoSession、PlayerController、PerceptionService、StaticMemoryStore、WorldRegistry、LocalSaveService及旧JSON编辑器管线作为旧版保留；不作为本轮新版默认运行入口。Scene/asmdef引用应先查证，不能以文件名或行数把它们批量判死代码。

## 验证边界

本反馈先记录代码决策；在修改冻结前，历史113/113不认证当前源码。最终以同一sourceFingerprint的EditMode、PlayMode、BuildWindows、新Player图像和语音Started为证，结果另见本轮最终复核。设备实际发声、听辨、真人无障碍体验仍单列，不由自动化推定通过。

## 后续真实运行发现的补充整改

- 车辆首次接触：World终点圆形重叠含容差而扫掠未命中时，原来先写入active集合却不报告事件，后续真正接触被去重吞掉。已统一首次命中语义，补同tick去重、深入、分离重入、暂停和traffic reset回归；第一次冻结EditMode144/144通过（015836）。
- 真实路线探索：PlayMode015920与020335失败，完整栈定位到Memory合并后复制地面片。独立三顶点反例证明原绝对坐标float鞋带求和会在反向后从非零变零；00已将Area改为局部原点double累加，保留float签名与1e-8面积阈值，补正反向/反复复制/真共线回归。这属于具体反例驱动的数值修复，不是调大epsilon。
- 021201后续诊断被00停止并保持FAIL：稳定面积计算暴露第三次探测19502片导致记忆处理长时间未完成。00进一步在Perception避免无交集差集切割，并使用原凸面作已占区域遮罩，避免反复沿径向裁片接缝细分。候选F已经位于cell内，F减去(cell交prior)与F减去prior等价；独立Agent已只读核此集合关系和材质排序。离线同路第三探测降到1766片，Probe加Memory合计约472ms完成；它是局部诊断，不替代正式Unity，也不是独立Memory耗时或物理出声延迟。
- 详细前后记录见 `output/pure-probe/RootMemoryRouteDiagnostic-observed.txt`。后续修复必须重新冻结验证，不继承上述144项PASS。
- R15的灯具/灯态及D12的路沿/坡道高度表现仍未完成；现有相位/信号音、材质边界与二维通行不等于这些视觉要求。高度表现不应扩大成未提出的完整三维角色物理，最小许可链方案已写入 `docs/handoffs/UPGRADE_MAP_TRAFFIC_AUDIT.md`。
