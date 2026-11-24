更新：alpha.14已完成204项完整回归与三包交付，最终身份见AVALONIA-ALPHA14-DELIVERY.md；下文为实现阶段复核记录。

# alpha.14 连接表单与工具栏修订

已读经理本次获准运行冻结alpha.13的五张原生图及identity。PID38177、空配置原目录与窗口保留；本聊天只读图/改源/软件测试，不占前台。原图完整像素副本与SHA在avalonia/evidence/independent-native-empty-alpha13-20261030/PROVENANCE.json；其范围仅新建、滚动、私钥页、取消、深浅切换，没有连接/测试认证/上传/用户秘密读取，不能称四区SSH/Windows/picker/vault已验。

根因：“私钥设置”按钮回调硬设authMode.SelectedIndex=1；同时表单固定240说明列无gutter，过长说明在尾字处断行。alpha.14改为只展开/定位私钥节，非当前认证的私钥字段禁用并解释需在身份认证下拉框明确选择；不改原认证、密码可用性、输入、勾选与vault。不忙且明确选择私钥后才启用文件选择；保存策略仍只在明确保存时执行。

说明改为完整短句/有意换行，220dp说明列+24dp gutter，窄窗改上下排列与10dp间隔。连接库标题独占一行，操作间隔10dp；顶部轻量工具栏突出新建；主状态就绪，迁移卡移除，版本/实现与验证说明在关于页。

三个新增软件用例已通过：五种协议/认证组合导航无副作用与显式下拉切换，深浅900/760/700/480间距及底部可达，连接库分行与关于验证说明/无vault调用。保存失败输入保留/显式系统保存及旧表单尺寸用例也通过。主要新图已查看：

- key-navigation-preserves-password.png：仍为密码认证，输入和勾选保留，私钥节只读展示。
- key-explicit-auth-selection.png：明确下拉选择后的私钥方式。
- connection-polish-Dark-900-top.png、Dark-700-top.png、Light-480-bottom.png：说明gutter/完整换行和窄窗底部。
- library-polish-1150.png、library-polish-700.png：标题分行/清晰操作间距/轻工具栏。
- about-verification-details.png：验证边界集中说明。

均在avalonia/evidence/ui-0.3.0-alpha.14。上述为软件渲染，不继承旧原生图为本版通过。当前完整回归进行中，尚未alpha.14打包。alpha.13源包/运行实例保留，无跨聊天发信。
