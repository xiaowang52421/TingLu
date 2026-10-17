# UPGRADE_08：迷雾与纹理渲染

status: READY_FOR_UNITY

role/model/effort: role08；目标 gpt-6-luna/xhigh；本次执行环境未提供可核实的实际 model、provider 或 effort 元数据，不能确认是否与目标相符。

contract_version: StreetContract 1.0.0

## completed

- `StreetRenderPlan` 仅消费 `StreetMemoryFrame`、`PerceptionFrame`、`PlayerState`、`UserSettings` 与模拟时间。已知地面按稳定 materialId 映射简化纹理；永久轮廓、临时当前/旧轮廓、触觉路径和短时声音弧使用不同样式。声音定位弧不生成地面或实体轮廓；动态几何观测不绘制为完整对象。
- `StreetRenderer` 配置黑色背景与专用显示层，绘制已知地面的平面网格和轮廓线，并提供 `Render`、`Clear` 与动态资源回收。地面顶点与颜色逐点同步；线框和地面均关闭阴影投射/接收。
- 已知永久物体与当前临时物体的局部高度只用于其已知轮廓：轮廓线上沿高度绘制顶线，并在已知角点画细竖线；不挤出或抬高地面面片。过期临时物体保留暗淡地面虚线，不抬高成似真的活动实体。
- 增加七种稳定 surface pattern 映射；斑马线沿多边形局部短轴形成横向条纹。Shader 使用 Built-in `UnityCG.cginc`，材质由 `Resources.Load` 加载，不依赖 `Shader.Find`。

## changed_files

- `Assets/TingLu/Presentation/StreetRenderPlan.cs`
- `Assets/TingLu/Presentation/StreetRenderer.cs`
- `Assets/TingLu/Presentation/StreetRenderPlan.cs.meta`
- `Assets/TingLu/Presentation/StreetRenderer.cs.meta`
- `Assets/TingLu/Shaders/StreetSurface.shader`
- `Assets/TingLu/Shaders/StreetSurface.shader.meta`
- `Assets/TingLu/VisualAssets/Resources.meta`
- `Assets/TingLu/VisualAssets/Resources/TingLu.meta`
- `Assets/TingLu/VisualAssets/Resources/TingLu/StreetSurface.mat`
- `Assets/TingLu/VisualAssets/Resources/TingLu/StreetSurface.mat.meta`
- `Assets/TingLu/Tests/Upgrade/Role08/StreetRenderPlanTests.cs`
- `Assets/TingLu/Tests/Upgrade/Role08/StreetRenderPlanTests.cs.meta`
- `Assets/TingLu/Tests/Upgrade/Role08/StreetRendererTests.cs`
- `Assets/TingLu/Tests/Upgrade/Role08/StreetRendererTests.cs.meta`
- `docs/handoffs/UPGRADE_08.md`

## integration

由 00 在唯一运行时入口创建/取得 `StreetRenderer`，传入主相机、专用显示层和 `StreetConfig` 调用 `Configure`。每帧将 05 当前 `StreetMemoryFrame`、04 `PerceptionFrame`、当前 `PlayerState`、`UserSettings` 和模拟时间传给 `Render`。`Configure` 会把相机清屏模式设为纯色黑、背景设为黑色，并将相机 culling mask 限制为传入显示层；集成时须确保该相机/显示层专用于本显示用途。

## tests

- `StreetRenderPlanTests`：材质映射 7 个用例；另测旧记忆保留/高度限制、AudioOnly、仅声音观测及动态几何不出轮廓。共 11 个 NUnit 测例，`NOT_RUN`。
- `StreetRendererTests`：相机黑底与层、网格顶点/颜色数量一致、`Clear` 资源状态、地面不随高度挤出及轮廓局部高度线。共 2 个 EditMode 测例，`NOT_RUN`。
- 仅做了源码与资源引用检查；没有启动 Unity、编译、运行测试或构建。无截图/实际画面证据。

## limitations

- 斜俯视相机下遮罩边缘泄漏、材质纹理可辨性、对比度、线宽和高度视觉效果仍需真实画面检查；没有声明 A08/A11/A12/A13/A15/A17/A19/A29 整项通过。
- 声音独立层目前是短时弧线占位表现；真实定位、过期、纯音频行为和听觉可用性须由集成与人工测试确认。
- 材质为程序简化纹理，不是最终美术；本角色未做真实设备或色觉辅助人工验收。
- 00 应确认 `StreetSurface.mat` 的 Shader 引用在导入后无丢失，并运行本程序集测试及实际场景截图核验。

## contract_requests

- 无。未修改共享契约、配置、asmdef、场景或 Bootstrap。

## next_dependencies

- 00：排他打开 Unity 导入本角色资源，运行 `TingLu.Tests.Upgrade.Role08` EditMode；解决任何导入/编译/测试失败并保存真实日志。
- 00/12：完成镜头与已知地面/轮廓投影联调，检查遮挡边缘与未知区无纹理/阴影泄漏；保存不同模式画面证据。之后由 12 独立判定跨模块验收项。
