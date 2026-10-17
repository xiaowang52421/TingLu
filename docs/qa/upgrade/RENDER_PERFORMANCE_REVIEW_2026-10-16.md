# Render 性能复核与最小 Surface 缓存

日期：2026-10-16  
范围：`StreetEntry.Update` → `StreetRenderer.Render` → `StreetRenderPlan.Create` / Surface mesh batch。仅检查当前新版街区入口与 Role08；不推断旧版入口行为。

## 可执行结论

存在明确的重复 Surface 工作：`StreetEntry.Update` 每帧调用 `Render`；`Render` 每帧创建新的 `StreetRenderPlan`。只要记忆中有地面片，每个地面片就会重新进入 `AppendPolygon` 和扫描线 `Triangulate`；随后 `RenderSurfaces` 清空各 Mesh、重传顶点/UV/颜色/索引、重算 bounds 并重设材质属性。对于同一不可变 `StreetMemoryFrame`，这部分是确定的重复工作。扫描线三角化还会枚举非相邻边对来找交点，其成本随顶点数快速增长，因此实测出现大面片时尤其值得消除此路径。

已在 `StreetRenderer` 增加单条 Surface batch 缓存：当同一 `StreetMemoryFrame`、Palette、Display、renderer 的 `worldToLocalMatrix` 未变化，且模拟时间没有倒退或跨过任何 surface 的 `ObservedAt` / `CurrentUntil` 时，直接保留已上传 Mesh，跳过清空、三角化、网格数据上传及 bounds 重算。缓存仅保留最近一个 frame 引用；`Clear` 清空 Mesh 并使缓存失效。新建/导入/恢复记忆会得到新 frame 身份，自然重建。Surface 的临时/永久物体高度不参与地面 Mesh，但其 contours/highlights 仍由当前 frame 的 `StreetRenderPlan` 每帧重算。

Perception、now 下的声音弧线透明度、玩家位置/朝向、物体 contour 与高度都会影响 stroke，因此没有缓存完整 RenderPlan 或 strokes。`AudioOnly` 生成空 surface plan，进入该模式会清掉地面 mesh；退出时 Display 不同会触发重建。`NoUI` 虽然当前地面外观与 Standard 相同，也按约定纳入键值，模式变化后重建一次。

## 失效规则

|输入变化|Surface 缓存行为|原因|
|---|---|---|
|`StreetMemoryFrame` 对象引用改变|失效|同一帧作为不可变 surface/object 快照；覆盖导入、恢复、记忆更新和临时对象高度变化。|
|Palette 或 Display 改变|失效|Palette 改变表面材质属性；AudioOnly 控制是否存在地面 mesh；Display 变化按要求明确失效。|
|renderer `worldToLocalMatrix` 改变|失效|Mesh 顶点存于 renderer 本地坐标，需重新投影世界几何。|
|模拟时间跨越任一 surface 的`ObservedAt`或`CurrentUntil`|失效|current/stale 分类及对应 batch/亮度发生变化；边界采用 `(previous, now]` 检查。|
|时间倒退|失效|恢复/重置可能改变显影分类；重建并将后续边界检查基准重置到新时刻。|
|`Clear()`|失效|主动清屏后下一帧必须重建。|
|Perception、player、surface 时间边界之间的普通 `now` 推进|不让 Surface 缓存失效；strokes 仍逐帧生成|这些数据只影响线条、声纹透明度或玩家标记；不缓存 RenderPlan，故不会显示过期线条。|
|临时/永久对象的高度或 contour|单独不影响地面 mesh；若相应更新带来新的 MemoryFrame，则按 frame 身份保守失效；strokes 仍逐帧生成|Surface mesh 只来自已知地面 polygon。物体的局部高度与轮廓通过线条表现。|

缓存只在三角化/Mesh 更新成功后提交为有效；异常中断的半成品不能被下一帧当作命中。`SurfaceBatchRebuildCount` 是可观察的重建计数，不是耗时指标。

异常清理复核补充：每次 surface rebuild 入口先停用/清空现有 batch 与四类 staging list；无论 rebuild 成功失败，`finally` 都清 staging list。失败路径再次禁用并清空所有 mesh，清零 ActiveSurfaceBatchCount、清除缓存 frame 并保持 cache miss；成功路径只在 uploads、bounds 与属性设置全部完成及 scratch 清理后提交缓存。`Clear()` 使用同一 batch 清理流程。Role08 的既有 `RenderUsesOnly...` 测试会用反射为批次的 Vertices/UVs/Colors/Triangles staging list 各注入一项，再调用 `Clear()` 断言四者全空；反射仅限测试，不为生产代码增加故障注入点。实际 Unity 异常时的恢复尚未运行验证。

## 回归与测量边界

`StreetRendererTests` 新增两组行为断言：稳定记忆帧连续渲染不再重建 Surface batch，同时确认玩家和声纹 stroke 仍更新；另覆盖新 MemoryFrame、ObservedAt/CurrentUntil、时间倒退、Palette、Display/AudioOnly、renderer transform 与 `Clear` 失效。

另新增 `RecordsInitialRebuildAndThirtyStableRenderTimings`：复用 Role08 已有的 13 顶点自接触 asphalt 记忆面片，加一条合法短时威胁声纹 observation 和玩家箭头，经 `StreetRenderer.Render` 实际绘制。用 `Stopwatch` 单独记录首次 Surface rebuild 和紧接的 30 次稳定 Render；时间从 1.1 到 1.39，远早于 `CurrentUntil=1000`。按排序样本输出 p50（中间两项平均）、nearest-rank p95 和 max，并输出首次与稳定帧后的 `SurfaceBatchRebuildCount`。稳定阶段断言重建数仍为 1；不设毫秒通过门槛。`TestContext.WriteLine` 将记录放入 Unity Test Framework 的测试输出/XML 日志。样本覆盖完整 Render（含每帧 plan、声纹 alpha 与线条更新），因此代表此 fixture 上的编辑器渲染路径，不代表 Player 帧率或整体游戏负载。

本复核没有启动 Unity，未运行 Unity 测试或游戏，也未采集实际样本数值。计时测试已就绪，需由主控统一 Unity 验证后读取 XML 测试输出。历史记录中的离线“第三次 Probe + Memory 约 472 ms”属于 Perception/Memory 诊断，不能归因于 Renderer，亦不能作为此缓存的前后性能数据。当前能确认的是代码路径可跳过，不能声称具体毫秒收益。统一运行时验证还应在同一源码指纹下覆盖初次 render、稳定帧、边界到期、恢复、AudioOnly 切换，并报告片段顶点/三角规模及 cache hit/rebuild 数；若要量化优化幅度，须另采同 fixture 的未缓存基线与缓存路径，报告中位/P95。

## 静态核对点

- `StreetEntry.Update` 无条件在运行状态调用 `rendererView.Render(...)`。
- `StreetRenderPlan.Create` 每帧投影 surface/object/perception 状态；声音弧线 alpha 随剩余寿命变化。
- `StreetRenderer.RenderSurfaces` 旧路径每次均 `Mesh.Clear`、遍历 `AppendPolygon`、上传四组 mesh 数据、`RecalculateBounds`。
- `AppendPolygon` 对每个 patch 调用 `Triangulate`；交点扫描对非相邻边成对检查。
- `StreetMemoryFrame` 对外集合只读，surface 几何为只读副本；`StreetMemory.Apply/Import/MarkStale` 会重建 `Current` frame，可用对象身份做单条安全缓存，不需要全图 hash 或无限三角化字典。

这些是源码静态结论，不是运行时测量结论。
