# Avalonia 0.3.0-alpha.6 检查点（2026-10-30）

C#迁移中的文件懒树、多来源及顶部标签行为阶段版；完整迁移继续，旧包保持。

## 实际行为

- 文件区明确选择已打开且可用的来源，不按IP/主机名匹配、不重新认证；每个来源有独立路径栏/目录列表/懒树及原来源生命周期。
- 终端和监控保留在原SSH会话，文件/编辑/传输绑定用户选定来源。只隐藏被其他工作区关联的FTP/SFTP协议顶部页，SSH页不隐藏。解除最后关联或关闭所属工作区后恢复；文件来源循环仍保留可达顶层。
- 解除关联只关闭借用视图，不关闭来源或丢弃草稿/原来源队列。关闭原来源使相关草稿只读、绑定任务取消，不改投其他连接。草稿标签、提示及保存确认包含来源名。
- 懒树展开才读取子目录，不跟随链接；每路径单飞与8秒截止。最多128缓存目录/50000条目、单目录20000项，UI树4096节点/128层；收起释放节点，晚回复不填回已收起/替换分支。派生TreeView已明确应用基础模板，实际渲染可见节点检查通过。
- 修正Avalonia延迟初始TextChanged使首次目录列表被误判过期：请求记录当前已观察路径，初始化/服务端规范化事件不清掉有效首屏；用户后续改路径仍使旧回复失效。

## 验证与边界

最终102项全量通过（50秒），日志 avalonia/evidence/alpha6-final-validation.log。覆盖原四区/安全编辑/心跳/认证/跳板/保管库等回归，以及7项新来源/懒树模型和UI回环。实际关联按钮→选来源→顶部仅SSH页保留，选择器等待时切换仍上传原来源原目录（两连接认证各1）、解除/所属关闭恢复、草稿解绑保留/来源关闭只读、懒树展开和收起晚回复均通过。

24张软件图及尺寸/SHA索引在 avalonia/evidence/ui-0.3.0-alpha.6/SCREENSHOTS.json，其中 associated-file-tab-hidden.png、source-restored-after-owner-close.png、lazy-tree-expanded.png、四区三尺寸/深浅主题。所有均为headless软件及自有回环，不是原生GUI、物理150%或生产证明。旧空白懒树渲染失败日志保留，最终图已实际查看。

修复发布锁文件RID不一致：全工程明确win-x64/osx-arm64，归一化锁文件后net10.0包版本/contentHash不变（alpha6-rid-lock-verification.json）。正常Mac打包后的无窗口runtime-check/合成xftp诊断通过，未访问真实凭据。

## 冻结身份

- sourceFiles: 474
- sourceManifestSHA256: 86ff0c5aac743971afa2ef88fe27abff7b6c48286ba6b3dd38b15a8d70c4c9b8
- sourceArchiveSHA256: 5379a66f6fa7fa83251114832e367f348c11b821b42b4b2bf2e079e55264a021
- checkpoint: /Users/xianyu/IdeaProjects/forterm/checkpoints/avalonia-0.3.0-alpha.6-20261030
- macApp: /Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.6-macos-arm64.app
- macExecutableSHA256: 9e4ffd5f99068e8b18460d9a5beaca99b0ab3d1fc27963297f5029c7b9acee88
- windowsPrototypeZIP: /Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.6-prototype-windows-x64.zip
- windowsZIPSHA256: e10303be2d601dd94e262cffc850febd3e1418793d835a6efe50dc1565e420f0
- windowsMainExeSHA256: b0c19c2a8ca0862d68134e2a5b432dd278b00ece8f871236c0cd88477ef73a5d
- symbolsArchive: /Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.6-symbols.zip
- symbolsSHA256: 1ca90ee642924de441da02b020f76f1db6d1f21ba3b48bb1ed344080416d516d

正常Win ZIP 52,070,006字节，展开121,086,629字节；Mac124,717,768字节。无PDB，symbols单独归档，DLL/许可完整保留。四AMD64入口、ZIP CRC、签名与474源码逐项一致；PAYLOADS/SIZE/SHA256SUMS-Avalonia-0.3.0-alpha.6均在release目录。239PE常规/延迟导入和18个Mac原生依赖检查未见未解析项或VC外部导入；icu.dll仍有Win10 1903+系统边界，不保证1809，干净Windows运行仍待。

## 继续项

FTP实际连接和文件流尚未迁移（模型预留FTP类型不代表已实现FTP）。随后继续复制移动、权限/sudo、更多布局快捷键与完整迁移矩阵；最终Windows安装器/便携版未出。原生GUI/IME/系统picker/真实vault/Windows及生产现场仍由经理协调，不占前台。另需在后续版本固定工作区关闭后新建的标签顺序，避免Dictionary复用槽位改变显示顺序。

后续缺陷补记（alpha.7修复）：旧版原生终端组件在逻辑树detach时会解绑外部PTY读写；alpha.6切换标签、连接库或重建标签关联后，SSH传输虽仍在，终端视图可能停止接收/输入。alpha.7由工作区持有重挂载保护、实际关闭显式Dispose，已补切页期间继续接收服务器输出和返回输入的回环。旧alpha.6包不改，此行为以更新版本为准。
