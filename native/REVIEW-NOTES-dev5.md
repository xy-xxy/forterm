# 0.2.2-dev.5 上传流程续查候选

现场“选完文件仍不动”尚未精确定位。原生版已关闭，当时运行版本和提示未确认；当前旧Electron0.1.7的成功不能证明原生当时行为。此候选不宣称已解决未知现场原因，不自动重登、重放票据、重传或更改目标路径规则。

继承dev.4：关闭队列不虚报入队；上传即展开队列，显示固定阶段；更多→复制上传阶段信息，内存最多64事件/最近20上传，仅版本、固定阶段、相对计时与状态，不含路径、文件名、会话ID、错误文本、凭据。

新修复：
- 已复现：关联文件视图等待覆盖确认期间关闭，该视图的上下文已取消而来源共享队列未取消，旧逻辑会永久等确认。现在隐藏旧确认并跳过尚未确认的任务，不写入、不关闭来源transport。
- 已复现：隐藏队列展开时，外围文件面板未重新分配footer高度，分配0但需229px，传输行被裁掉。现在显隐变化刷新外层布局，新增/开始/失败项滚入可视范围；不在每个进度刷新时抢滚动位置。

验证：
- 软件Fyne驱动、串行UI分派、真实合成SSH/SFTP回环，执行实际App.Open及xftp控制器参数；主窗口与-newwin独立来源窗口两条路径。
- 真实上传按钮→普通选择器取消0队列、再次选择中文/空格/%文件→正确关联Workspace和共享队列→远端字节一致；覆盖提示在可见owner窗口，跳过保留原字节，确认覆盖后字节一致；两条路径各认证1，未保存临时连接。
- 关闭借用视图的覆盖等待以及动态队列高度回归，从失败日志到修复后通过；相关transfers/desktop/localfile/sshconn/ftpconn定向race通过。
- 证据：upload-dev5-final-race.log、upload-controller-route-before-fix.log、upload-queue-layout-before-fix.log、upload-controller-route-dev5-final/{result,stages}-{tab,window}.json和overwrite截图（软件栅格，不是Windows/原生前台截图）。

当前构建未执行Windows GUI/真实生产上传/原生前台picker。此前PID45327仅证明旧业务基线。下一次现场只需一次已授权上传并复制阶段信息，准确版本/卡点同时取得；详docs/NATIVE-UPLOAD-FIELD-PLAN-DEV5.md。已发布dev.3三包及dev.4候选均不覆盖。
