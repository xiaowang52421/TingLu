# 原地重复探测碎片修复交接

- `status`: `VERIFIED_UNITY_FINAL_STAGES`；本交接记录当前最终 Unity 三阶段证据，Assets 已冻结。
- `scope`: 只覆盖 `StreetMemory` 的表面合并边界与原地重复探测稳定性；不把该证据扩大为整体验收或人工设备验收。
- `contract_version`: `StreetContract 1.0.0`。

## 根因与修复

旧实现把 `MergeEpsilon` 内的端点当作同一点，并在合并轮廓时用近邻去重。两个真实边界只差很小距离时，表面合并会改变边界；下一次使用未合并观测做差集时，残留的微小边界片会被重新加入记忆，造成重复探测持续增长。该路径没有删除永久记忆，也不是面积阈值不足导致的丢面。

当前 `StreetMemory` 只允许坐标完全相等的端点通过共边合并，轮廓去重也只移除完全相同的连续顶点。凸性、重复顶点和候选边索引仍保留原有容差检查；没有提高面积阈值、缩小探测扇区或删除永久表面。Role05 新增近缝矩形在重复探测与 stale Export/Import 后保持两片、面积不变的回归。

## 验证

最终统一 Unity 阶段使用同一源指纹 `384D8BE8D0AD29F3ECB2F46C53E2F6EB18A9FFFAAE7FEFDCAF3668A686662B62`，三阶段运行期间 `sourceUnchangedDuringStage=true`：

- EditMode：184/184 PASS，`output/unity-verification/20261017-011653-3feaf097/results.json`。
- PlayMode：2/2 PASS，`output/unity-verification/20261017-011730-0d53dedf/results.json`。
- BuildWindows：PASS，`output/unity-verification/20261017-011749-950246af/results.json`。

正式 Unity 路线性能/重探 JSON 为 [probe-performance-20261016-171702-4084-5621e8ab.json](../../output/unity-verification/20261017-011653-3feaf097/street-acceptance/probe-performance-20261016-171702-4084-5621e8ab.json)：13 次探测，`Memory.Apply` 中位 4.5351 ms、最大 6.1382 ms；路线末值 267 片，面积 `164.28663706376975`。原地 8 次重探的片数为 `238→266→267→267→267→267→267→267`；第二次重探后面积为 `164.28663706376975`，后续 6 次面积完全相同（JSON 浮点值一致）。

当前树的 Role05 14 项也用 Unity 随附 Roslyn 编译器按正式 Contracts、`StreetMemory.cs` 与 `StreetMemoryTests.cs` 执行：14/14 PASS。PowerShell `Add-Type` 脚本本身在本机因 framework reference/旧编译器兼容性不能直接编译，故该离线结果使用 `output/pure-probe/Role05TestRunner.cs` 作为等价反射运行器；Unity 184/184 是正式阶段证据。

## 修前对照与限制

历史 Unity 基线 `output/unity-verification/20261016-042726-44d669c2/street-acceptance/probe-performance-20261015-202733-9356-d822afce.json`（旧指纹 `92FA7E...C4A0802A`）显示原地重探 `206→229→234→239→244→249→254→259→264`，后续每次增加 5 片，最终面积为 `164.286212284676`。这是旧地图/旧源阶段的历史对照，不能与当前路线直接作同源数值 A/B；它只用于说明原始增长现象。

当前修复证明了这条正式路线在 8 次静止重探窗口内第二次后的计数和面积稳定，并且 Apply 耗时处于当前 Unity 样本范围。它不是任意地图、任意浮点坐标或长期无限探测的形式化证明。仍需保留实际设备听辨、真人无障碍、鼠标手感等人工验收边界；`Started`/Unity 计时也不等于耳机实际出声延迟。

## 交接边界

- 本交接之后不再修改 `Assets/TingLu/Memory/StreetMemory.cs` 或 Role05 测试。
- 若后续发现新几何失败，必须附带当前源指纹、地图版本、观测/记忆片数、面积、耗时和最小复现，再重新统一运行验证。
- 原项目 `D:\听路` 与旧存档/旧试玩目录保持只读。
