# alpha.13 布局复核：根因与正常MainWindow新图

2026-10-30。alpha.12的终端左/文件中/监控底、逐核横条为真实SessionPane/MonitorPanel源码行为。旧four-areas夹具只是把SessionPane放在带合成提示的Window中，省略MainWindow外层导航，没有改变内层布局。已以正常MainWindow复现保存修前图avalonia/evidence/alpha13-before-layout/normal-main-expanded-dark.png；此前“仅待外部”判断撤回。

alpha.13改为：最左MainWindow连接导航、左侧文件与懒树、中央终端/编辑器、右侧监控。编辑器三向停靠仅在中央变换，文件/监控不随之换位；保留原控件、草稿、PTY和SSH认证。窄窗响应式导航、最小可用列宽；中央编辑区预留正文空间，避免只显示工具栏。监控整栏垂直滚动，逐核CPU为两列紧凑环形仪表、固定高度虚拟ListBox，真实Linux采样不改为固定UI数据。

正常MainWindow测试走公开启动入口/指纹确认、手动恢复导航并展开编辑/监控，不把Pane移到自造布局中。自有127.0.0.1服务输出明确合成的256核采样；两个/三个实际采样得出delta，认证次数1。软件逻辑700×433约对应1050×650@150%的内容面积，但未做物理150%或Windows实机验证。

新图（路径均在avalonia/evidence/ui-0.3.0-alpha.13）：

- normal-main-expanded-dark.png：1150×760正常MainWindow完整四区。
- normal-main-expanded-700-logical.png：正文可见的小窗完整四区。
- normal-main-monitor-bottom-700-logical.png：同窗右栏滚动到底，网络趋势/说明可达。
- normal-main-expanded-light.png：完整浅色MainWindow。
- monitor-256-cores-last-180.png：180dp窄栏滚动至cpu255；128行数据，实际生成仪表数量有界，不创建256个控件。
- queue-narrow-170.png：170dp队列两行/26×26详情与取消操作/完整tooltip，实际鼠标取消队列任务通过。
- queue-details-small.png：300×200详情，滚动正文与固定关闭按钮。

已逐张查看主要完整/小窗/256核末项/队列图。新MainWindow坐标/正文可见性、右栏最末说明可达、禁止逐核ProgressBar、虚拟化、共享认证及队列真实鼠标命中均有断言。当前在修订后的相关回归/完整验证流程，尚未alpha.13三包；alpha.12保持不变。所有截图为软件渲染，非原生GUI/Windows/生产证明。不向其他聊天发送消息。
