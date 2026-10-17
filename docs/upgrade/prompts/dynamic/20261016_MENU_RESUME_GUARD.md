# 00追加：恢复失败菜单门禁

input_ui_audit独占 AccessibleMenu.cs、Role11/AccessibleMenuTests.cs及自己的QA/handoff。模型请求gpt-6-luna/xhigh；禁止Unity。

MenuCallbacks新增可选Func<bool> CanResume，旧调用不设置时保持兼容。暂停Esc与Resume选项共用TryResume：门禁失败不调用Resume、不SetPlaying；调用Resume后再次检查，失败保留Pause。主控StreetSession传Running&&!Finished&&!recovering&&!sessionFault&&!Clock.Paused（注意：前置判断在正常暂停时Clock.Paused=true，因此主控传入应只判可恢复，实际Resume失败以fault置位判断）。请使用CanResume只判运行/非故障，Resume异常由主控置fault。测试前置拒绝和回调后失败两路径及原正常恢复。

其他小窗风险只记录，不扩大修改范围。冻结后通知00。
