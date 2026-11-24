# 原生工作区下一组实现约束（2026-10-27）

完整迁移仍有效，不能因单组通过而最终收尾。`internal/workspace` 已有显式关联/顶栏可见性/循环可达性纯Go模型；UI尚未接入。

- App保留每个真实会话的Workspace/transport/Queue/Operations；建立稳定ID和model.Session。隐藏顶栏仅对被显式引用的SFTP/FTP来源，SSH顶栏不隐藏。非活动关联来源同样隐藏；最后引用解除/owner关闭恢复来源顶栏。外部聚焦隐藏source须BFS到可见owner并选择来源。
- 每个owner+source需要独立浏览视图（目录/筛选/排序/滚动），不能简单重用一个filePane对象冒充独立状态。建议提取现有NewWorkspace中的文件区域成initFilePane；借用view是轻量Workspace字段子集或单独FileView，借用同一source.FileConn/Transfers/Operations，拥有owner窗口和独立UI context，不创建额外终端/monitor，不关闭共享后端。
- 每owner缓存SourceViews[sourceID]；source切换保留这些控件/状态；隐藏不持续刷新，transfer完成仅标记needRefresh，显示后合并刷新。队列需多观察者订阅，不能只有source原始控件收到变化。
- 编辑器始终属于owner中央，OpenText/NewText须从当前fileView捕获source.ID/title/真实连接传给owner，不能借用owner.Conn保存。doc键用sourceID+path，同名跨来源独立，dirty/离线草稿保留，Source切换不改保存目标。
- 文件标签关闭只detach；草稿与传输保持并说明。最后引用解除来源顶栏恢复。显式关闭共享连接则提示关联数/草稿/任务，其他owner保留offline来源元数据/草稿，绝不自动改绑同IP新连接或重放票据。
- owner关闭销毁其view/context；借用view只能unsubscribe/取消自己的UI请求，不能Queue.Close/Conn.Close。真实source会话关闭才关闭source的后端，其他来源/SSH/exec不受影响。
- 三列：最左可收起连接库/堡垒机导航，文件多来源在左，中央终端+原生编辑分屏，右监控。拖动要有可见/可聚焦分隔条、方向键/Shift/Home/End/重置；像素首选宽度+窄窗口限制不覆盖首选；主题/字号/布局持久化。当前开发界面默认控件拼接尚未完成美观验收，不能拿它当最终外观。
- 完成关联/生命周期后，接连接库/安全凭据/TOTP/指纹/认证形式/可设心跳+SFTP已开启探测，再完整深浅主题和实际窄窗口/高缩放/操作截图。最终Windows NSIS/便携/四入口ZIP与SHA/经理复核仍必须做。
