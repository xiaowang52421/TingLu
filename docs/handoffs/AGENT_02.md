# 02 感知与记忆交接

status: PARTIAL  
contract_version: 0.2.0

## 当前实现

- `PerceptionService` 只从声学结果构造被动定位，不再按声源 `ObjectId` 查询附近世界对象。声源类别、材质和 `PerceptionSemantic` 从 `AcousticQueryResult` 显式复制。
- 探测、接触、触感路径需要 `IWorldOcclusionQuery.QueryOcclusionBoundaries`。缺少该接口或查询返回 null 时，不产生回波几何和覆盖清理；空单元不会清除记忆。
- 遮挡覆盖按 64 个角单元分别裁剪：仅与对应角域相交的边界会限制该单元，避免一个方向的窄遮挡缩小整圈覆盖；覆盖单元仍为保守内切三角形。轮廓线段使用射线通过遮挡线段端点的精确参数切分，再对每段作可见性检查；没有用步进采样补完整轮廓。
- 静态记忆依然按覆盖局部拆分；同 ObjectId 在不同位置的观察保留为独立片段；空覆盖不会推断或删除局部记忆。Memory 使用私有几何工具，避免跨 asmdef 调用 Perception 内部类型。
- 触感路径只在 `MaterialAt(player.Position)` 确认脚下是 tactile/blind path 时生成；只输出最近且离脚点不超过 0.4m 的 Guide 线段，并裁为脚下投影两侧最多 0.4m。每次重算先丢弃上一帧路径 transient，离开时立即清除。
- `StaticMemorySnapshot.Semantic` 随快照复制、清除拆分和导出/恢复保留；不从 ID 或材质猜 Guide。

## 集成

按 00 接线约定创建 `StaticMemoryStore`，传给 `PerceptionService` 的 `IMemoryCheckpoint` 参数，并分别注入 `store.Observe` 与 `store.Clear`。按 `perceptionHz` 调用 `Recompute()`；成功 `CaneTap` / `Collision` 事件调用 `Probe()` / `Contact()`。关卡退出调用 `Dispose()`，恢复调用 `Restore()` 后重算。

`WorldRegistry` 已实现独立的 `IWorldOcclusionQuery`。请核对该查询的边界和 `IWorldQuery.IsOccluded` 使用同一组阻挡边界；当前 `WorldRegistry` 查询还返回非 Event 物体轮廓，而 `IsOccluded` 只检查显式墙段，可能令不同系统的遮挡口径不一致。

## 验证

运行：`pwsh -NoProfile -File Assets/TingLu/Tests/EditMode/PerceptionMemory/Run-PureChecks.ps1`

本轮 Add-Type 纯 C# 检查实际通过 10 项：声源真值隔离、中性定位、显式语义与配色、无边界接口 fail-closed、窄遮挡切分且开放方向保持完整探测半径、同 ID 不同位置与局部清理、当前盲道接触与六米记忆边界、动态观察不进入静态记忆、记忆声纹强度遵循 0–2 设置、纯音频输出不带任何画面元素。局部清理仍只删除覆盖多边形中的旧轮廓部分。Unity 编译、EditMode/PlayMode 和场景验证未运行。

## 后续

- 03 的 `AcousticModel` 须把定义中的 `Semantic`、`Category`、`MaterialId` 传入 0.2.0 声学结果，否则被动声源仍按 Unknown 显示。
- 需用 Unity/WorldRegistry 集成测试验证每种阻挡物的边界集与 `IsOccluded` 一致。
- 需评估保守内圆清理范围；扩大清理必须依赖能证明整块覆盖无边界的精确分区，不能用加密射线替代。
- `.meta` 已为本模块改动和相关测试补齐；全工程 GUID/Unity 导入仍待 00/07 检查。
