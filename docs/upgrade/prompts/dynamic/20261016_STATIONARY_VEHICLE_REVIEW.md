# 静止玩家车辆穿过不命中：独立定位

请求gpt-6-luna/xhigh。只读生产/测试，独占docs/qa/upgrade/STATIONARY_VEHICLE_DIAG_2026-10-16.md与output/pure-probe/StationaryVehicleDiagnostic.cs等本任务前缀诊断输出。禁止Unity、原件写入，不直接改World/Player/Integration（其他Agent在分析）。

现场XML：output/unity-verification/20261016-014206-8da30594/editmode.xml，142/140通过，A24与RecoveryBackendFailure两用例未触发车撞。玩家从正式spawn走到(-1.372,-16)，t3.44南车(-1.35,-11.72)，t8.44南车已过路尾，无暂停、错误或碰撞。真实车辆穿过静止玩家仍未触发。

优先找生产根因：StreetWorld.VehicleContacts与相对扫掠/activeVehicleContacts、Player.Tick静止与接触、构造时Polygon位姿/CopyObject。可以用当前Library/ScriptAssemblies与Unity自带Mono执行只读离线诊断，注意非Unity证据；不要在netstandard配环境上花很久，静态明确结论先报00。给最小修复建议与真正防回归测试，保留安全边界。不伪造失败、不关交通、不仅延长等待。
