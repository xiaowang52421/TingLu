# M0 感知、记忆与声纹复核交接（2026-10-14）

status: PARTIAL  
contract_version: 0.2.0

## 结果

- 按 `DECISIONS.md`、`CONTRACTS.md`、`OWNERSHIP.md`、`STATUS.md` 与 `00_总控.md` R01–R12 复核 Perception、Memory、Presentation、Shaders 及纯检查。认知边界实现与本地纯检查相符：被动声音只给位置；声纹读取受限感知与冻结记忆；局部回波/接触几何需精确遮挡边界；动态观察不能进入永久记忆；未知声音使用中性定位；盲道实时引导只在脚下确认时出现且离开后清除；纯音频帧不输出视觉标记。
- 在 `VoiceRenderer` 中复用声纹排序列表与定位弧段列表，将短线段/痛觉边框直接写入对象池，去除逐帧排序容器、定位弧列表和逐线段临时数组分配。排序优先级和线条输出顺序保持原样。现存资源 `.meta` 配对仍在；本轮没有新增 Unity 资源。
- `PresentationFrameComposer.Compose` 仍会创建逐帧标记/列表等托管对象；此次只消除了渲染器中可直接复用的热点分配，不能据此称整条表现管线无 GC。
- 未发现应改变感知或记忆判定规则的可复现缺陷；没有扩大探测范围或放宽记忆清除条件。

## 修改文件

- `Assets/TingLu/Presentation/VoiceRenderer.cs`：复用排序缓冲与定位弧缓冲，减少逐帧及逐线段临时分配。
- `docs/handoffs/PERCEPTION_POLISH_2026-10-14.md`：本轮独立交接。

## 验证

- 命令：`pwsh -NoProfile -File Assets/TingLu/Tests/EditMode/PerceptionMemory/Run-PureChecks.ps1`
- 结果：10 项通过，包括声源真值隔离、中性未知声、显式语义与配色、无边界接口时不确认/不清理、窄遮挡与局部记忆裁剪、盲道接触范围、动态观察不进永久记忆、强度设置和纯音频无视觉输出。
- 本机 `Unity` 不在 PATH。没有执行 Unity 导入/编译、Shader 编译、场景运行、Profiler 或设备帧率测量；此优化的运行时 GC 与帧时收益仍需 Unity Profiler 验证。
- 工作区没有 Git 元数据，本轮以现存文件检查，没有版本控制差异摘要。

## 建议

- 用 Unity Profiler 在代表性记忆片段数量下比较 `VoiceRenderer.Render` 的 GC Alloc 与帧时；确认池容量不足时威胁、痛觉边框和视野仍按既定优先级可见。
- 接入 `WorldRegistry` 后复核 `QueryOcclusionBoundaries` 与 `IsOccluded` 的阻挡口径一致，并覆盖墙角、贴边和多遮挡场景。
- 真人体验仍需评估盲道离开后的已记忆低亮度轨迹、未知声音定位与不同显示强度；本轮没有设备或玩家体验证据。

contract_requests: none  
next_dependencies: Unity 导入与 Profiler；WorldRegistry 遮挡集成验证；真实显示设备与用户体验验证。
