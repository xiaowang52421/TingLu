# 00追加：故障暂停和普通碰撞反馈

主控独占 StreetSession.cs。audit_runtime 仅独占 StreetSessionIntegrationTests.cs及UPGRADE_SESSION_BOUNDARIES.md，配置请求gpt-6-luna/xhigh。禁止启动Unity。

主控将提供：普通非车Contacted播collision.bump；Tick异常暂停；恢复异常置故障标记，Resume不得继续半状态，Retry可尝试完整恢复或新开始清除；Save不得持久化半恢复状态；Report播固定错误句。菜单CanResume门禁由input_ui_audit处理。

请补有意义真实生产链回归：非车接触有碰撞声；用可注入后端回调抛异常让恢复失败，不能Resume/菜单伪Playing，不能覆盖最后好存档，清除故障后Retry或新开始可用。避免断言私有实现。未运行标NOT_RUN，完成冻结。
