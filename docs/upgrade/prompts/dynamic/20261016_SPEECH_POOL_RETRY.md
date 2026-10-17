# 00追加：语音池满保留与重试

用户授权继续修复；执行请求配置 gpt-6-luna/xhigh。speech_fix 独占 UnityAudioPlayback.cs、Role06/UnitySpeechSelectionTests.cs、Build-StreetSpeechCache.ps1、TingLuSpeech资源和自己的handoff。禁止启动Unity、改其他角色文件或原项目。

修复 FinishSpeech 在池满时直接丢整句的问题：保留已合成PCM，以非模拟时间进行有限频率退避；暂停菜单的Interface语音仍能重试。保持串行可懂，禁止每帧反复失败、无限新建/重合成。取消、supersession、Reset/Dispose、代数变化必须清理等待语音，不能复活旧句。

语料增加固定错误提示“出了点问题，请先回到首页，再试一次。”。主控在StreetSession.Report播这句，完整原因保留日志。补真实池满→释放→Started及Reset/取消不重播的有意义回归；测试未运行须标NOT_RUN。完成后冻结并通知00，不启动Unity。
