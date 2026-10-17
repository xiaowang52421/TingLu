# 真实组件探测耗时测量交接

- `status`: `READY`；测试源码已冻结，等待总控统一运行 Unity。
- `tests`: `NOT_RUN`。本任务没有启动 Unity，也没有把源码检查或离线测量写成 Unity 结果。

## completed

- 在 `StreetSessionIntegrationTests.RealRouteContinuousProbesMeasurePerceptionAndMemoryStages` 中，以正式 `StreetLevelFactory`、`StreetWorld`、`SimulationClock`、`StreetPlayerController`、`StreetPerception` 和 `StreetMemory` API 重放原路线及敲杖节奏。
- 每次探测独立计时 `StreetPerception.Probe` 与 `StreetMemory.Apply`，记录模拟时间、坐标、coverage cell/边界数量、感知面片数量、记忆对象/面片存量、面积及两阶段毫秒耗时；逐次记录同时写入 NUnit `TestContext.WriteLine`（进入 XML test output）与 `TestContext.Progress`，汇总中位数、P95、最大值与总耗时写入 Progress。
- 路线按历史节奏执行五次探测并覆盖此前约 `t=8.28` 的热点；抵达西侧路线终点后，以合法静止状态按正常 cooldown 加 1.1 秒节奏再探测八次，总计固定为 13 个样本。断言覆盖与观察片数/面积稳定、记忆面积无明显变化且面片数未无界增长。
- 若设置 `TINGLU_UPGRADE_EVIDENCE_ROOT`，测试会在该目录写逐次样本和阶段汇总 JSON；未设置时只输出提示，不影响测试执行。
- 增加 Session 回归，验证 cane tap 请求提交时探测记忆尚未应用，而同次探测之后会写入有效地面记忆。
- 修复测试文件中重复的 `TimingSummary` 嵌套类声明。

## changed_files

- `Assets/TingLu/Tests/Upgrade/Integration/StreetSessionIntegrationTests.cs`
- `docs/handoffs/UPGRADE_PROBE_PERFORMANCE_MEASUREMENT.md`

没有修改生产代码、共享 Contracts/asmdef、Acceptance 路线、用户存档或其他角色文件。

## validation

- Unity EditMode 上一版证据：`output/unity-verification/20261016-030014-1aec301a/editmode.xml`，158 项中 157 项通过、1 项失败。唯一失败是首个静止探测间隔断言：预期 `4.1 ± 0.021 s`，实际 `4.1399999999999118 s`；由循环在 Advance 前检查输入、再按 0.02 s 步进造成的固定帧量化边界触发。该失败不需要也不应改生产节奏。
- 当前修订将首个及后续间隔改为 `[caneCooldown + 1.1, caneCooldown + 1.1 + 2 * frameSeconds + 0.000001]` 秒；每次样本同时经 `TestContext.WriteLine` 写入 NUnit 输出，失败测试也会保留已采集的逐探测记录。
- 当前源码修订的 Unity EditMode / PlayMode：`NOT_RUN`，由总控统一运行。当前 Unity 测量仍是诊断数据，不设置机器耗时 PASS 阈值；此前本地结果不能据此宣称性能达标。
- 静态检查：`TimingSummary` 仅保留一个定义；采样、汇总与 JSON 格式参数逐项对应；无机器耗时硬 PASS 门。
- 结果若写入 JSON，计时基准是 Unity Editor 中 `Stopwatch` 包围的正式组件调用；它不代表设备上的真实出声延迟，也不代表真人操作体验。

## next_dependencies

- 总控使用 Unity `C:\Program Files\Unity 2022.3.62f1c1\Editor\Unity.exe` 统一运行该 Integration 测试并审阅逐次日志/可选 JSON。
- 若测试失败，按首个失败断言和实际逐探测记录回交修复；在有完成的 Unity 测试结果前保持 `NOT_RUN`，不得报告通过。
