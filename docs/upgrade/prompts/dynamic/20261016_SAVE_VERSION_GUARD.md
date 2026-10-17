# SAVE_VERSION_GUARD 完整后续任务提示词

复用save_isolation_audit，gpt-6-luna/xhigh，StreetContract1.0.0。先完成只读审计，随后开放唯一Assets/TingLu/Persistence/StreetSaveService.cs、Tests/Upgrade/Role10/StreetSaveServiceTests.cs与自己QA/交接。其他Agent不并写，主控收回前不自改这些文件。不启动Unity、不动原存档、所有IO测试仅新建临时目录。

新增用户架构意见指出future contract如2.0.0-beta当前TryVersion不可解析→Corrupt；当主档/备份一边良好时可能以损坏处理轮转而改写不认识文件。请核实并做最小保守保护：带非空但本版无法解释的contract应拒绝载入/写入，按FutureVersion（包含unknown/unrecognized的明示错误），对主/备份一视同仁；空缺字段是否Corrupt保持现有，不把所有坏JSON都永久保护。已支持的旧纯数字版本仍Corrupt策略，不升级schema、不添加包。补有意义临时文件行为测试：合法本版主档+备份2.0.0-beta，以及主档unknown版本+合法备份，Load/Save均拒绝且文件逐字节不变；已存在Future主备对称性不重复造镜像测试。

不为架构意见批量重构；Save catch诊断若需加只读LastError之类公共API先报总控，不擅自改契约。完成后冻结并交接精确变更、测试名、NOT_RUN状态，等待总控统一Unity。
