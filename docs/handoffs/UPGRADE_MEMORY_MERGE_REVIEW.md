# StreetMemory 增量合并只读复核

- `status`: `READY_FOR_UNITY_REVIEW`；StreetMemory/Role05 已冻结。本复核未修改 Assets，也未运行 Unity 或测试。
- `scope`: 检查 SortedSet 可变排序键、候选失效、活动多边形引用、失败候选缓存、面积/元数据保持与确定性。

## 结论

- 当前实现未发现静态阻断。`AddPolygonEdges` 和 `RemovePolygonEdges` 都先从 `SortedSet<MergeEdgeGroup>` 移除候选组，再更新 references 并刷新 `FirstPolygonOrder` / `FirstEdgeIndex`；重新插入只发生在刷新后的组仍有合法候选时。合并会逐边从索引删除两张旧多边形的全部引用，然后将它们标为 inactive，再加入新多边形。因此未见排序键在集合内变化，或候选继续引用已失效 polygon 的路径。
- 多段共边反例已由两层保护挡住：`HasOtherSharedBoundary` 检查选中边以外的正长度共线重叠；拼接后 `HasRepeatedVertex` 检查非相邻近重复顶点。新增 Role05 fixture 使用面积各为 2 的上下两段共边矩形，断言输出仍为两个不相交面、总面积为 4，且没有非相邻重复顶点；它没有逐面断言面积。
- `RejectedPairs` 的键包含两端不可变的 `Polygon.Order` 与 `EdgeIndex`。初始与后续 polygon 的 `Order` 在一次 `MergeAdjacentConvex` 调用内单调唯一；点集/edge index 创建后不变。缓存只挂在局部 `MergeEdgeGroup` 上，合并索引和缓存均随本次调用释放，不跨 `Apply` 累积或保留 polygon 对象引用。
- 合并按完整反向共边生成轮廓；只在 material、`ObservedAt`、`CurrentUntil` 完全相同的 `SurfaceKey` 组内进行。Role05 覆盖 8×8 网格合并后的面积 64、每格中心覆盖、相同输入确定性；覆盖旧/新观测的总面积 96 与新旧 metadata 保留；多段共边 fixture 检查两张输出面不相交及总面积。
- 原先将 incoming 几何预合并后直接作差集 mask 的改动已撤回。当前 `Apply` 仍以原始观察面建立差集索引；合并只用于最终记忆面，不会把拼接边界送入要求凸 mask 的 `StreetGeometry` 差集路径。

## 验证边界

- geometry_fix 的离线纯源码诊断报告新增面积、metadata、重复顶点及多段共边用例通过；该报告不是 Unity NUnit 结果。
- `RunRole05MemoryTests.ps1` 在当前 PowerShell 主机因 framework reference 解析失败，未能编译该测试脚本。统一 13 样本 Unity 验证仍待总控执行；t=8.28 的 `Probe+Apply` 离线耗时约 457 ms，仍偏高，不能宣称性能已解决。
- 局部凸性判定仍用 `Cross < -MergeEpsilon`；叉积是面积量而 `MergeEpsilon` 是长度量。当前回归对常规共边格片和自接触反例有效；高尺度、极浅角的舍入边界仍建议通过统一 Unity 样本与实际性能/几何观测验证。
