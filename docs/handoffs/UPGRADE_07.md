# UPGRADE_07：固定斜俯视镜头

status: READY_FOR_REVIEW

role/model/effort: role07；要求 gpt-6-luna/xhigh；本次执行环境未提供可核实的实际 model、provider 或 effort 元数据，不能确认是否与目标相符。

contract_version: StreetContract 1.0.0

## completed

- 新增 `StreetCameraRig : MonoBehaviour, IStreetCamera`，接口为 `Configure(Camera, StreetConfig)` 与 `Follow(Point2, bool snap)`。
- 相机使用共享配置的倾角、正交尺寸、距离、目标偏移和跟随 sharpness；固定 yaw 使用 `StreetCoordinates.CameraYaw`，不接收角色朝向或世界数据。
- Point2 位置经 `StreetCoordinates.ToScene` 映射至 Unity 空间。正交相机从玩家南侧上方朝北，水平跟随使用帧率无关的指数平滑；`snap` 可立即定位。
- 新增 NUnit EditMode 测试，覆盖北/东投影方向、玩家目标居中、配置偏移、固定朝向、snap、平滑步长一致性和非 snap 跟随。

## changed_files

- `Assets/TingLu/View/StreetCameraRig.cs`
- `Assets/TingLu/View/StreetCameraRig.cs.meta`
- `Assets/TingLu/Tests/Upgrade/Role07/StreetCameraRigTests.cs`
- `Assets/TingLu/Tests/Upgrade/Role07/StreetCameraRigTests.cs.meta`
- `docs/handoffs/UPGRADE_07.md`

## integration

由 00 在唯一 Bootstrap 接线点创建或取得组件并注入配置：

```csharp
StreetCameraRig rig = cameraObject.AddComponent<StreetCameraRig>();
rig.Configure(mainCamera, streetConfig);
rig.Follow(playerPosition, true); // 首次进入或角色/世界传送后 snap
```

正常游戏帧将当前 `PlayerState.Position` 传给 `Follow(position, false)`。相机组件不持有模拟时钟；暂停期间 Bootstrap 必须停止调用 Follow，恢复/检查点跳转时以 `snap: true` 对齐，避免镜头在暂停时继续平滑或跨地图长距离追赶。监听器朝向仍由 06/00 按角色朝向独立接线。

## tests

- `StreetCameraRigTests`：已编写 5 个有意义的投影、朝向、配置跟随及平滑测试；状态 `NOT_RUN`。本角色不启动 Unity，需由 00 在排他 Unity 窗口运行 `TingLu.Tests.Upgrade.Role07` EditMode。
- 实际镜头画面、不同窗口尺寸下的人工观察、与 08 几何/纹理投影联调：`NOT_RUN`，无画面证据。
- G0 导入与 Role00 契约测试由主控报告为 PASS；此处仅作为前置状态，不是 Role07 测试结果。

## limitations

- 真实运行视觉与玩家体验尚未验证。`CameraTilt`、`CameraSize`、`CameraDistance`、目标偏移和跟随 sharpness 当前均为共享配置中的临时基线。
- 暂停冻结依赖 Bootstrap 在暂停期间不调用 `Follow`；正式 `IStreetCamera` 没有时钟/暂停参数。
- 实际 model/provider/effort 不能从本次工具元数据中核实。

## contract_requests

- 无。00 已在 `StreetConfig` 增加 `CameraTargetOffsetX/Y/Z` 与 `CameraFollowSharpness`；正式 IStreetCamera 签名及 StreetContract 版本保持不变。

## next_dependencies

- 00 完成唯一相机创建和 `StreetCameraRig` 接线，并按运行状态门控 Follow；传送、恢复、检查点跳转时传 `snap: true`。
- 00 在 Unity 排他窗口运行 Role07 EditMode 测试；随后由 08/00 联调已知几何与地面投影，并记录真实画面观察。
