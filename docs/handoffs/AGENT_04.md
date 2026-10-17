# 04 声纹与视觉交接

status: PARTIAL  
contract_version: 0.2.0

## 当前实现

- `PresentationFrameComposer` 只读取感知、静态记忆、玩家状态和 `UserSettings`；声源不读取完整世界状态。
- 类别表现来自 `Observation.Semantic`：Person 用短划线，Threat 用脉冲线，Interaction 用点划线，Guide 用触感线，Ordinary 用实线。Unknown 声音只出定位弧和中性灰，不从 Event/材质/ID 猜威胁。
- `PaletteMode.ColorBlind` 使用另一组区分色；`VoiceprintIntensity` 按契约 0–2 同时控制实时声纹与记忆线强度；视野和疼痛边框颜色也随配色切换。字幕字号 `FontScale` 属于 06 的 UI 设置应用，不由这层处理。
- `VoiceRenderer.Configure(Camera,int,Material)` 可由 Runtime 显式配置相机与专用图层，之后调用 `Initialize(composer)`；也提供 `Initialize(composer,camera,layer)` 重载。相机仅渲染声纹专用图层，菜单 UI 仍应使用 IMGUI/独立 Canvas。
- 线段池满时排序为威胁、触感、当前定位/几何、记忆；痛感与视野显示安排在普通线条和记忆之前。超池截断的定位是否仍足够清晰需要设备验证。

## 集成

场景拥有者创建声纹相机，设置黑底和只包含声纹专用 Layer 的 Culling Mask；不要让世界 SpriteRenderer 使用该 Layer。由 Runtime 先配置 renderer，再创建 composer 并 `Render(perceptionReader,memoryReader,playerReader,settings,simulationNow)`。事件总线回调交给 `OnEvent()`，离场 `Clear()` 并释放订阅。纯音频模式会清空游戏表现；NoUI 保留声纹。

首关声源必须由 03 在 `AcousticQueryResult` 写入受限语义；05 配置静态交互物、动态行人和关键威胁的 `ObjectDefinition.Semantic`，不能依赖材质 ID 猜类别。

## 验证

由纯 C# Add-Type 回归检查实际通过：未知声音不会显为威胁、Threat 的外形由显式语义驱动、辅助配色改变颜色、强度值降低输出，记忆线在强度设为 2 时也按配置同比增强，纯音频帧不包含标记、视野或疼痛画面。Unity `VoiceRenderer`、Shader 编译、场景相机隔离、截图和超池行为未运行；Unity 未检出。

相关纯 C# 命令：`pwsh -NoProfile -File Assets/TingLu/Tests/EditMode/PerceptionMemory/Run-PureChecks.ps1`

## 后续

- Unity 导入后运行 EditMode/PlayMode，检查屏幕比例、黑底相机、独立 UI 和专用 Layer 配置。
- 用盲色模式审查各类别在多背景下的可辨度；当前线型和形状仍是主要区分渠道。
- 检查 `VoiceprintIntensity` 对痛感边框和视野扇区的应用是否符合设置预期；纯 C# 中记忆线与实时声纹已覆盖 0–2 强度回归。
- `.meta` 已为本模块新增与现存代码、Shader、测试补齐；Unity GUID 导入验证未做。
