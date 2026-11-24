# alpha.33 无人值守阶段交付（2026-11-10）

连接库支持收藏、排序方向、协议模式、分组树和行级动作。工作区已有文件图标栏、文件工作区标题、终端标题、监控标题和可收起/最大化操作。文件列表采用图标行，右侧监控按系统、CPU逐核、内存、网络、磁盘和进程区块展示实际指标。终端启动后主动展开监控，文件列表失败时保留关联已有连接入口。新增 Telnet 独立 TCP 终端适配器、合成回环测试、工作区结构测试和状态隔离测试。完整239项测试通过，7项视觉定向测试通过。

交付说明见docs/AVALONIA-ALPHA33-DELIVERY.md。ZIP SHA 577099aa4540f8219c966768ed5f206979cbbb6b712db9752a72212f12f1a444；setup ab1c28d122c11058174b84fcf7617f14b8a7f044f309616238dca62ef23eb8be；portable 5226e8ce2f34c97801c47d43e78400e194707afd59d0d45dd19f480ab5b7ec1c；Mac apphost bd0d07d090ef4db91ef0ba79d95761a30c3becca4beab4622895a01eb47ffa2f。

管理协议、云端仓库、RDP/VNC 和 Windows 系统客户端适配仍需要后续 provider 阶段。

---

# alpha.19 XTerminal风格下拉弹出与动画已完成，三包已生成（2026-11-10）

参考XTerminal只读观察到的深色菜单浮层、图标/文字行节奏和柔和悬停反馈，统一所有ComboBox。alpha19新增150ms上浮+淡入展开，圆角深色弹出层，34dp条目，绿色选中项和悬停过渡。文件来源、工作区排列、连接协议、认证、跳板、偏好和账户下拉均接入；键盘导航与轻量关闭保持。

下拉动效定向2项通过，完整231项通过，195张软件截图索引。交付说明见docs/AVALONIA-ALPHA19-DELIVERY.md。ZIP SHA 8a5569286dd526f4d769daa770474757a84e843ebd23b2d823416bd0f5b5fa02；setup fdcae227bdd45ef3fbc076aaacc2b31d2983dca007c9c5e26f457ac1b1344b78；portable 3d6389c989b0b7e6155241e16be0ad062f7df3d23df1113b6399eb86eb2e5c1a；Mac apphost 4ad3ce0938a0fe2df6cfdea35706f55236fea0598ef64502d4b10c65d3f49310。源码672文件，293载荷，14条新版校验索引。

XTerminal仅只读观察，未操作其终端内容。alpha19 Mac真实窗口和Windows/ConPTY/DPI/IME/picker/vault/真实堡垒机仍待现场；旧alpha18及更早包、旧会话和用户配置保持。

---

# alpha.18 截图标注区域已完成界面打磨，三包已生成（2026-11-10）

按截图四处区域整理了连接导航、工作区能力条、文件来源工具条和编辑器排列选择器。连接导航改为图标+名称+数量的紧凑行，连接列表有协议徽标。四个能力开关为统一状态芯片，文件来源栏有标题和成组操作，排列选择器与能力条同高并带方向图标。键盘快捷键和已有文件来源逻辑保持。

相关界面2项与既有8项定向UI回归通过，完整第二次231项通过。第一次完整运行有一个本地启动提示重复的偶发场景，单独重跑及第二次完整运行通过，日志保留。当前alpha18软件截图194张，重点图见avalonia/evidence/ui-0.3.0-alpha.18；交付说明见docs/AVALONIA-ALPHA18-DELIVERY.md。

ZIP SHA 90807284deabe66745cd07596c633eed0e0be19c8ee2cc5c6ae32f66dd227824；setup d582be3721678b0aebf7696956823fa0113baba5c109a0755c297d683d723ee6；portable 30817fa3e97457581542d630bcf8c29397bc962d600c427ebe2e6ef2977e2dab；Mac apphost 245ea2a9a8a24c1eb2287021065149b68d527a66d1945077e9bb9983c0ead460。源码671文件，293载荷，15条新版校验索引。旧alpha17及更早包、旧会话和用户配置保持。

Mac真实窗口、Windows/ConPTY/DPI/IME、picker/vault、真实堡垒机权限和生产上传仍待现场。当前本地改动和三包已完成，不占用用户前台，不发送跨聊天消息或发布。

---

# alpha.17 按钮上下/左右居中三包及Mac候选已交付（2026-10-31）

通用Button+实际Presenter双Center，分类Left覆盖改Center；图文整体6dp、低基线⌄矢量。34dp普通/30dp图标/26dp队列及原局部尺寸、padding/状态/Content/自动化名保留，树/表格/菜单正文不居中。详AVALONIA-ALPHA17-DELIVERY.md，含同尺寸深浅/三字号/100/125/150%修前后直链与像素/布局证据。

20相关UI项16秒全部通过；首轮舍入断言和错标浅色图原记录保留，最终明确每窗theme+ActualTheme断言。670源码匹配、101当前软件图；冻结Mac精确DLL再复核18图/child几何通过，仍软件非native。未重跑旧227/模型/终端缩放，alpha16实际Mac有限通过保持该版归属。

ZIP SHA 27a059edde091e72e76187984ebd37c523f529dbd0801b048dfd3bcc8f04900c；setup 654dc96c43b3ea51419fc22751c2d89505b227bc347354f5516c05417379f571；portable adad8d59f041c895f52f3a31fde00fd4dc563addb3ceaf76edde7278b0ff3925；Mac apphost 3ba67b420f09cb5078a699076591b9dca3f3bf9f36e811e95faa9b094fee6123。293载荷/原ZIP/bundle一致，双表依赖与零窗口诊断通过，15条SHA和说明/清单齐。新Mac候选未打开，默认realm AvaloniaAlpha17。

经理协调本版native复核；Windows/物理DPI/IME/picker/vault/生产仍待。不占屏/关用户窗口/改旧包会话配置/跨聊天发信/推送发布。当前本地交付闭合，无新失败时停止制造改动和重复测试。

---

# alpha.17 按用户新反馈修按钮水平+垂直双居中，定向复核中（2026-10-31）

用户新反馈按钮文字靠上并补充左右也居中；不是alpha.16终端缩放新失败。冻结16保留。原包实际Presenter为Stretch/Stretch、分类额外Left；新17通用内容双Center、分类Center，不改标准模板/对称padding/点击尺寸/状态。低基线⌄改内置矢量，图文作为整体6dp间距、Content/自动化名称保留。

冻结16精确DLL修前截图和新源修后同尺寸，中英文13/14/16、深浅/100/125/150%及真实按钮bounds/像素留白、键盘focus/Space/hover/队列鼠标取消定向验证。初次断言26dp物理尺寸忽略125%像素舍入（实26.4dp）失败保留，改为检查声明26dp与<=1物理像素舍入；未扩大区域。首轮部分命名Light的主/连接图被MainWindow默认偏好重置为Dark，旧图保留并修正宿主为每窗显式theme+ActualTheme断言，最终复核中，不把错误标签当浅色证据。

不重跑无变化旧227全量、模型上游/终端缩放，不占前台、不操作旧用户会话或跨聊天发信。当前未冻结17，后续定向通过再新Mac/Windows三包，真实GUI由经理协调。

---

# alpha.16 独立实际Mac启动与缩放有限复核通过，已归档（2026-10-31）

经理获本次用户许可运行冻结alpha.16独立PID49932，自有ZDOTDIR/预写实际多行彩色中文左右提示，正常按钮无NRE。双击标题实际1150×788→2560×1326→1150×788→2560×1326；文件分隔将终端收窄约284px再重置，当前提示无新增重复/交错；一次粘贴echo的精确执行和独立输出通过。边角拖拽没变尺寸不算；工具typeText大写被改变不归因产品。历史保持旧列是不reflow边界。

9张原图+identity/manager-result/launch.log按原字节和SHA归档，详INDEPENDENT-NATIVE-RESIZE-Avalonia-0.3.0-alpha.16.md及evidence/independent-native-resize-alpha16-20261031/PROVENANCE.json。身份匹配冻结Mac apphost。经理正常关闭自建终端/CmdQ并确认PID退出，旧用户会话未动。

本聊天仅读图/归档维护文档，未占屏/改代码/重打包/重复测试/跨聊天发信，冻结三包、668源码与138软件图清单不改。只合成提示实际Mac有限通过；用户原主题、Windows/ConPTY/IME、picker/vault/真实通道和生产上传仍待，此次无协议尺寸抓取。当前无新增本地问题，不制造改动或轮询。

---

# alpha.16 启动空引用修复三包已交付，原生复验仍待（2026-10-31）

冻结alpha.15已在精确包内DLL正常按钮路径复现NRE，位置TerminalView.Pty.cs:319，模型尚未初始化；经理原图/日志与软件复现独立保存。新alpha.16等待就绪、工作区显式启动/默认控件并发单飞、关闭等待取消与spawn归属/本次reader token修复；真实错误仍显示。详AVALONIA-ALPHA16-STARTUP-REVIEW.md与DELIVERY。

修后完整227项1分13秒全过，含正常按钮3组预写实际多行夹具/仅一次PID/大→小→大严格一份，以及并发、初始化前关闭、无效exe非零失败可见。首轮无效exe错误期待Unix抛异常的测试失败保留，不隐藏错误。冻结alpha.16精确DLL同按钮软件复验也通过，0控件首次NRE/无错误文字。668源码逐项一致、138软件图、293三包载荷/原ZIP和bundle一致；240PE双表/18Mac依赖、outerPE、无窗口Skia/HarfBuzz/合成xftp通过。源码控件ID launch-ready-v16/模型resize-grid-v15。

ZIP SHA f07dd6697b449670169534e0cfc0341df6ffea43bc92be446b5e7d5f2be86b64；setup b493f7bfe0d202a8af5bf9e801fe999cc240ce937edf07ac52066a068b9241e3；portable a97a9cf1abbb4d5dc1bb121c68a3fc70f22221cd086cd5cb5236dd5b2584523c。Mac apphost 27eaa7b55753c9f91c3fae052f612cc811dde582af9d6b95277ee8a8056dccb1，独立app/配置realm AvaloniaAlpha16；旧alpha.15及用户旧会话保持。已按真实native NSIS工具更新源内打包路径/元数据，记录BUILD-TOOLS。补齐前版空控件diff（固定原commit逐项原SHA核对），不倒改旧冻结记录。

仍待经理协调新版本实际Mac正常按钮+正确多行缩放、Windows/ConPTY/IME/DPI、系统picker/vault/真实通道及生产上传；门户契约未给。未占前台/读用户.zshrc/消费真实票据、agent、vault/SSO审批/跨聊天发信或推送发布。当前本地可交付缺口已闭合，不为等待新增功能或重复无变化测试；本聊天仅保留真实待现场项。

---

# alpha.16 启动空引用已定位并修复，完整回归中（2026-10-31）

冻结alpha.15正常按钮路径已直接用包内DLL复现，首次NRE在TerminalView.Pty.cs:319，模型尚未初始化；后续Loaded再次启动解释错误+提示符并存。独立原图/日志与精确DLL软件复现分开保存，详AVALONIA-ALPHA16-STARTUP-REVIEW。旧alpha.15不改。

新alpha.16等待就绪、工作区明确启动/默认控件并发单飞、关闭等待取消/异步spawn归属和局部读取令牌；错误保留。正常按钮3独立真实多行夹具/严格单份缩放+并发+关闭+无效exe共6项通过。首轮Unix退出2误期待抛异常的测试失败保留并更正断言，未隐藏错误。此前新页预布局覆盖缺口已明确补上。

当前开始单次必要工程完整回归，未冻结交付/未称真实GUI与Windows通过。上游模型不变不重跑；同时补齐前版空控件diff（逐个原SHA核对），新打包默认使用已验证原生NSIS工具。只本聊天编辑/本地证据，不占前台、不读真实.zshrc、不碰既有会话、不跨聊天发消息。

---

# alpha.15 独立身份完整性通过，实际缩放复验授权pending（2026-10-31）

经理报告三包SHA、ZIP293文件CRC、Mac apphost SHA与codesign独立核验通过，详docs/INDEPENDENT-IDENTITY-Avalonia-0.3.0-alpha.15.md。仅身份/完整性，不冒称全部静态或实机通过。

经理已一次问用户是否现在允许新版独立窗口+自建本地终端约3分钟大→小→大，临时配置/自建提示，不读用户.zshrc/不连服务器/不关旧实例。问题pending，不重复问/占屏。当前无新增已确认本地代码缺口，源码/冻结包保持，不重复无变化全量。经理forterm心跳已由经理删除，避免空轮询；本聊天未操作或跨聊天发信。用户回复后经理协调实际复核/新错再续接。

---

# alpha.15 缩放修订三包已交付，真实窗口复核待协调（2026-10-31）

详docs/AVALONIA-ALPHA15-DELIVERY.md与RESIZE-REVIEW。首轮216中1失败按原SHA d81c56b…保留；最终布局模型/PTY成对提交后固定5独立严格单份提示场景与真实单事务/stty断言通过，修后完整221项57秒通过；上游2312通过1原有SKIP。历史明确保持原行、不自动reflow；裁切单元放大可恢复/擦除不复活、真实输入非回显/选中复制、双PTY隐藏切页隔离、SSH WindowChange/alternate与文件保留均过。使用已有等宽Nerd，未装字体。665源码/135当前软件图/两组件resize-grid-v15包内标识核验。

新ZIP SHAc5a89672fca879cd3d14eeeba292e283327b3f939219f55f40dde363c250c4c3；安装器46aab22db5fa8e631d64dfd5cc394aecce4c98a808fa50fe749b73b173b2b074；便携0bbc9c3d1776e6fe9a9e8d163267d3d364453804ad947293d29eaf4276bf52dc。293载荷全一致，原ZIP/模块精确嵌入，双表/运行时源码来源核对；正常无PDB、symbols另存。

旧缓存缺失且恢复x86 NSIS在当前主机不可执行，独立构建匹配3.04/8192/log ARM64主机工具，仅补充runner替换compiler路径，冻结应用/脚本/Windows资源不改，详BUILD-TOOLS与独立恢复点；未装Rosetta/系统组件，原失败保留。主机工具变化未重跑无变化功能全量。

alpha.14与用户会话/配置/旧包不动，现场两张图仅本地复核未归档发布。新原生GUI/真实用户提示缩放/Windows/ConPTY等仍待经理审后协调，不称全验收，不占屏/重复问/跨聊天发信。无需按已有图去clear用户终端。

---

# alpha.15 修后完整221项通过，构建缩放修订候选（2026-10-31）

修后完整单次221项57秒通过：avalonia/evidence/alpha15-revised-full-validation.log；供冻结的alpha15-final-validation.log为同一通过结果副本。首轮216中1失败逐字节保留alpha15-first-full-failure.log（SHA d81c56b01427d5c2c7c379b01f68c989df93f7172e1f9535ac86e1b3a12c9bd8）。修复的是模型/PTY在最终布局成对提交，事务内不发中间尺寸；固定5独立多行提示场景严格1份、一次最终真实PTY事务提交与stty回读均过，不是重复直到绿/放宽计数。上游固定模型2312通过1原有SKIP。

正在两平台self-contained和三包构建，alpha.15尚未冻结交付。原行历史策略/裁切恢复/擦除范围、Nerd仅用已安装字体、隐藏/双PTY隔离/SSH alternate和非回显输入输出/选中复制等证据见RESIZE-REVIEW。旧alpha.14和实际用户会话不改，新版原生GUI/真实用户提示/Windows缩放仍待经理协调，未占前台或跨聊天发信。

---

# alpha.15 布局事务成对提交修复已接，修后完整回归中（2026-10-31）

首轮216中215通过/1份提示变2失败仍保留，副本avalonia/evidence/alpha15-first-full-failure.log；不以定向偶然绿交付。针对时序新增：最终布局后统一提交模型和PTY，预提交期间不发中间尺寸；真实PTY事务用例断言只提交1次最终几何并stty回读。原多行提示用例保持严格1份，固定5个独立场景/两种中央切换序列全过，连同本地启动/双PTY隔离/SSH重绘共9项19秒通过。

当前新整组日志alpha15-revised-full-validation.log，尚未结束/冻结。旧合成captures已保存在alpha15-before-layout-coalescing-*，当前UI目录由新整组重新生成；用户图未归档发布。裁切保留/擦除不复活、历史按原行边界、上游2312通过1固有SKIP等边界不变。无clear/自动终端输入，不改alpha.14源包/用户会话，不占屏、不跨聊天发消息。

---

# alpha.15 完整回归216中1失败，当前阻断未解除（2026-10-31）

中断前工程完整回归已实际结束：216总计、215通过、OwnedZsh多行提示在四区切换line56仍从1份变2份，日志avalonia/evidence/alpha15-final-validation.log保留。上游模型最终2312通过1原有SKIP。不能以定向通过或重跑偶然变绿交付；此前“完整回归中”由此更新。

当前追加正常布局事务内的尺寸提交时序定位与修复：模型和PTY作为一对，在最终布局后统一提交，避免连续中间cols/rows触发多次shell重绘；不是仅延后PTY而让模型先跑。保留严格重复计数，修后将做同场景固定次数稳定性与必要整组回归。无alpha.15冻结包。原行/裁切恢复/擦除不复活边界继续验证，用户alpha.14会话、旧源包不动，不占屏/跨聊天发信/重复问。

---

# alpha.15 真实PTY缩放定向通过，工程完整回归中（2026-10-30）

详docs/AVALONIA-ALPHA15-RESIZE-REVIEW.md。独立zsh已复现多行左右彩色提示在大→小→大后从1份变2～3份；候选稳定原行网格/保留裁切单元和擦除范围、保存光标锚点、PTY绑定/异步spawn同步、隐藏/零面积忽略、SSH一格一致已实现，固定原版本MIT模型/控件源码与diff可审。不是clear/主题推脱/读用户配置。历史行为明确为保留原行，不自动reflow。

12项定向通过；其后四区三种中央分隔、缩放后真实鼠标选中复制、正常渲染调度截图也已通过。真实stty回读/非回显随机输出、原Local Launch+字体zoom、双PTY隐藏/返回/输入隔离、SSH WindowChange/alternate有界帧+随机ACK和文件通道保留均有断言。旧字体探测Menlo无Powerline，系统已有Nerd，新栈实际MesloLGS NF且字形可用，不安装字体。只用owned服务/PTY/数据，用户现场截图未复制发布/转录，现有会话未操作。

正在单次工程完整回归和最终固定源码上游复核（上游先前2312通过1原有SKIP）。alpha.15尚未冻结三包，不称新版本原生GUI/用户真实提示/Windows已验。alpha.14与所有旧源包会话保留，真实GUI新缩放由经理协调，不重复问/占屏/跨聊天发信。

---

# alpha.15 调查窗口缩小后的终端重复错行（2026-10-30）

最新真人反馈窗口从大缩小时终端有问题。经理获准只看用户本地终端并缩放，不输入命令；alpha.14第二个本地页首次观察已存在彩色左右提示严重重复/断裂/交错，非仅截边。/tmp/forterm-resize-observe-uqm1z6/before.png和smaller.png已读，只作本地复核，不复制进发布或外传、不转录用户可见内容；第二图尝试拖角未改变尺寸，不称新缩放复现。

当前仅在自建PTY/合成服务定向调查，正常MainWindow大→小→大，核查实际PTY尺寸/回显/非回显随机标记、xterm reflow/光标、字形与渲染、隐藏/切页和各PTY隔离。用户既有终端/配置/.zshrc/票据不读取、不操作，不以clear或主题推脱。alpha.14及旧包源/会话保留，新修订alpha.15；不重复问表现、不占前台，不向其他聊天发消息。

---

# alpha.14 有限实际Mac复核通过，已只归档（2026-10-30）

经理获本次用户明确许可运行冻结alpha.14 Mac，PID5725，SHA2fe5ed4b…匹配。实际新建→私钥导航仍为密码且密码可用、私钥字段disabled/说明明确；滚轮0→273→434、跳板与取消可达，gutter与文字完整可读。alpha.13发现的导航副作用和说明间距两项在该有限实际Mac检查中通过。

详docs/INDEPENDENT-NATIVE-EMPTY-Avalonia-0.3.0-alpha.14.md。三张原生图/identity/manager-ui-result原像素与SHA归档在avalonia/evidence/independent-native-empty-alpha14-20261030。仅此空配置UI子集；未连接/测试认证/vault/shell/上传，未验真实四区/picker/Windows，不称全部通过。经理已取消未保存表单，主窗留给用户看，alpha.13未动。

按要求本聊天仅归档记录，未运行GUI/重复测试/改代码/重打包，原542源码与三包身份保持；不占前台、不向其他聊天发消息。

---

# alpha.14 连接表单/工具栏修订三包已交付，待独立复核（2026-10-30）

详docs/AVALONIA-ALPHA14-DELIVERY.md和FORM-REVIEW。204项完整回归46秒，542源码/82软件图；五种认证/协议导航保模式/密码可用性/输入/勾选且vault零调用，显式下拉才改方式。说明24dp gutter/完整短句、连接库标题分行10dp间距、轻工具栏与关于验证说明已修并看图，旧工作区/入口/保活/票据规则保留。

ZIP SHA6b8701d09e2736533fd5c7f09d5161936f5892e07b3b4ebd306551c58464716c；安装器aa585cf6564c937d71576a0ab3be66f89369dcd697d91153a1458117aad960fd；便携3af9ed83e51088aa677d37e2667ff485211e746a641144f0f425eb766d9b6379。293载荷与原ZIP/模块匹配，双表检查/来源摘要通过，正常无PDB，symbols另存。原生alpha.14/Windows/picker/vault/生产未验，仍待独立复核。

经理此前仅alpha.13空配置原生新建/滚动/取消/深浅通过，原五图与identity像素/SHA留档在avalonia/evidence/independent-native-empty-alpha13-20261030，不当新版/真实四区证明。PID38177末次只读检查仍运行，主入口SHA8e2578dc…未变，未占前台/关闭窗口。所有旧包保留，不重复无变化全量、不向其他聊天发消息；本聊天旧forterm-24已不存在，无新定时器。

---

# alpha.14 完整204项通过，构建连接表单修订候选（2026-10-30）

单次完整204项46秒通过，avalonia/evidence/alpha14-final-validation.log。五种协议/认证组合的私钥导航保模式/输入/勾选/vault无副作用，显式下拉才切换、深浅四宽度gutter/底部、连接库独立标题/操作间距和关于边界均验证；主要新图已逐张看。原有认证保存、工作区/传输/监控回归通过。

当前构建新三包与冻结身份，尚未alpha.14最终交付。alpha.13独立空窗原生证据已留档，仍只限经理那次新建/滚动/取消/深浅；PID38177及前台窗口不动，不称新版原生GUI/真实四区/Windows/picker/vault已验。唯一编辑者，不向其他聊天发消息，不重复无变化全量。

---

# alpha.14 导航无副作用/间距/普通UI话术修订已接（2026-10-30）

详docs/AVALONIA-ALPHA14-FORM-REVIEW.md。经理五张alpha.13原生空窗图已读并完整留档，PID38177现有窗口/配置/冻结包不改。私钥导航硬改认证已移除，五种协议/认证组合验证原模式、输入、勾选和vault无副作用；需下拉明确选择。24dp说明gutter/完整短句、连接库标题分行与10dp操作间距、轻量顶栏、关于验证说明已接，主要软件图已看，定向通过。

当前单次完整回归进行中，尚未alpha.14三包；不占前台、不把alpha.13有限原生空窗复核当新版四区/Windows/picker/vault验收。不向其他聊天发消息。

---

# alpha.13 有限原生空窗复核已完成，alpha.14修连接表单与工具栏（2026-10-30）

经理获本次用户许可后实际启动冻结alpha.13 Mac，PID38177，独立空配置目录/var/folders/ds/ksvp5t7x22q8m4sd8l98qxbw0000gn/T/forterm-manager-alpha13-qvgn336w/settings；identity.json与library-dark/connection-dark/connection-bottom/key-bottom/connection-light原图同根。真实点击新建、输入区滚动0→259、私钥页继续到487、底部取消/字段可达和深浅切换通过；未连接、测试认证、上传、读用户凭据。应用保留给用户查看，取消未保存表单，旧窗口未动。仅此有限空窗UI通过，不能继承为四区SSH/Windows/picker/vault验收。

已读全部原图。新确认的源码缺口：私钥导航偷偷选择认证方式；表单说明贴右控件/末字孤行；连接库标题与删除按钮粘连，主工具栏拥挤，迁移话术侵入普通流程。当前alpha.14修这些既有要求，alpha.13包/运行实例不改，不占当前前台。导航只定位/展示，认证必须显式选择，回归验证值/勾选/秘密输入与vault无副作用；再渲染核验间距和关于说明。唯一编辑者，不向其他聊天发消息。

---

# alpha.13 经理独立审图/静态核验通过，GUI授权待回复（2026-10-30）

经理报告已实际查看normal-main-expanded-dark与700-logical图，确认文件左/终端编辑中央/监控右及逐核环形修订；独立ZIP SHA801abff6…与CRC通过，展开116.32MiB，Mac签名通过。此为经理审图/静态复核证据，不是实际GUI或Windows运行通过。

经理已一次询问用户是否现在允许运行alpha.13 Mac独立空配置约3分钟界面检查并截图（无生产连接/上传，不关旧终端），授权问题pending。程序员不重复询问、不启动GUI/占屏、不更改当前冻结身份、不跑无变化全量。后续实际窗口及安装器/portable独立复核仍由经理协调；程序员交付不等于最终全部验收。不向其他聊天发消息。

---

# alpha.13 布局修订三包已交付，待独立复核（2026-10-30）

完整说明docs/AVALONIA-ALPHA13-DELIVERY.md；根因和正常MainWindow图片索引docs/AVALONIA-ALPHA13-LAYOUT-REVIEW.md。201项完整回归46秒通过，其后同一仪表用例刷新保持末核的补验通过（无生产代码变更，不算202项、不重复全量）；540源码/61软件图。实际布局已改导航/文件左、终端编辑中央、监控右；256核虚拟环形网格/末核刷新保持、700逻辑小窗正文与监控末项、170dp队列26dp实际鼠标取消已测和看图。alpha.12确属源码布局缺口，旧“仅待外部”表述撤回，旧包保留。

新ZIP SHA801abff6fb6c5ff68d7743974f2c9f46d08793e1171f2bd85f1d5e4d7916d788；安装器161ec71264ecdb40f9dc0e1b87e8ad13305bfba74e99490e4672f889a89a8117；便携53b0266e342b62cb2a714aeb94f5c1cbfcce60cde936d9001b813c0c7c67236d。293载荷全部一致、便携原ZIP精确嵌入；正常无PDB、symbols另存。打包工具已集成同源码Mac校验，不再跨架构加载x64 IL；175bundle条目、全部管理运行库及外壳常规/延迟PE检查完成。

本次修订仍待经理独立复核，不说全部本地/生产已验收。Windows/物理150%/IME/原生picker/真实vault/生产仍待；门户契约缺。旧Go现场上传未结案。所有恢复点和旧版保留，不抢前台/真实秘密，不向其他聊天发信；未操作经理定时器。本聊天forterm-24先前已确认不存在，无新定时器。

---

# alpha.13 完整201项通过，构建新交付身份（2026-10-30）

完整单次201项46秒通过（avalonia/evidence/alpha13-final-validation.log），其后只补同一仪表测试刷新后保持cpu255位置，737ms通过（alpha13-core-scroll-refresh.log），无生产代码改动，不把补验算成202项/不重复无变化全量。正常MainWindow四区三尺寸深浅/小窗正文、256核虚拟仪表/末核/刷新位置和170dp队列26dp实际鼠标取消均通过，主要图已看，根因和图片索引见docs/AVALONIA-ALPHA13-LAYOUT-REVIEW.md。

正在self-contained两平台/新三包构建。alpha.12旧包和537源码仍保留；alpha.13打包工具已改用同源码Mac诊断器校验Windows原ZIP，不再直接跨架构加载x64 IL。新身份尚未冻结，不声称Windows/物理150%/原生picker/vault/生产已通过；本次修订仍待独立复核。不抢前台/真实秘密，不向其他聊天发信。

---

# alpha.13 正常MainWindow新图与根因已补（2026-10-30）

见docs/AVALONIA-ALPHA13-LAYOUT-REVIEW.md。alpha.12布局缺口确属源码而非夹具，修前完整MainWindow留档；当前已改导航/文件左、终端编辑中央、监控右。新真实MainWindow软件图含1150、1050、700逻辑与浅色、右栏滚到底；700窗口编辑正文有可见高度断言。逐核环形/256核128行虚拟化/180dp窄栏到cpu255、170dp队列两行26dp按钮及实际鼠标取消已测，主要图已看。没有把合成采样/逻辑尺寸当物理Windows证明。

正在本次变化相关回归和新身份打包，尚未alpha.13交付。旧alpha.12三包/源/证据不改；此前“仅待外部”结论由本节修正。唯一编辑者、不抢屏/生产/真实秘密，不向其他聊天发信。

---

# alpha.13 修复已确认的本地布局与仪表缺口（2026-10-30）

经理核验alpha.12三包与载荷通过，但发现真实源码布局仍为终端左/文件中/监控底，逐核CPU横条与任务按钮问题；不是夹具布局造成。此前“仅待外部/本地收敛”表述撤回，仍有本地工作，不据三包已生成停止。alpha.12产物、537源码与证据均保留。

当前alpha.13按既有要求修连接导航最左、文件区左、中央终端/编辑、右侧监控；逐核紧凑环形仪表+虚拟化/内部滚动、窄栏与700×433逻辑（1050×650约150%）可达、队列两行/26dp操作与tooltip。会提供正常MainWindow完整组合图，保持真实采样/同SSH和权限隔离。唯一编辑者，不抢前台，不操作生产/真实秘密，不向其他聊天发消息；在本聊天和文档回应。

---

# Avalonia alpha.12 本地三包已交付，仅待外部验收（2026-10-30）

自动跟进：调用automation_update删除forterm-24时，工具返回not_found，确认该任务本已不存在；未改经理聊天的forterm定时器，也未给其他聊天发消息。

最新交付说明docs/AVALONIA-ALPHA12-DELIVERY.md，最终身份release/DELIVERY-Avalonia-0.3.0-alpha.12.json。188项完整回归47秒+新增便携10项定向全通过（不是一次198全量），537源码/43软件图。四入口ZIP SHA7453997c11a3366b4117302146c74f4bc00cfe507e1fcf0c5721841065f83ae4；安装器060918fd88d6b98abe73a56dae78b9275470f7c0b5b831ce05b4a18baa36a729；便携7c3e1c558cb38e5e65ee8a9e2220fd3cfd5cfeeb4244dce83ee8b6ed85cdaffd。293载荷全部一致，便携内嵌原ZIP与模块精确校验；正常无PDB，symbols另存。旧Electron/Go/alpha.1–11保持。

完整本地功能迁移已收敛：三向停靠/四区显隐与比例/快捷键、配置无秘密迁移与删除保护、账户TOTP、权限sudo、流式复制移动及覆盖、共享认证四能力和文件协议顶部标签规则均已接和本工程回环。最终Windows更新说明/现场未勾选清单、SHA/验证文件在release。240PE双表无未解析/VC外部导入；Win1903+边界保留。

原打包器的ARM Mac加载x64校验失败保留；补充只读/自有临时目录验证器完成实际bundle/原ZIP/同源码Mac校验，源码与成品没有重写。详情及独立恢复点见交付报告。冻结IDENTITY中三包false是当时状态，最终DELIVERY/VERIFICATION为后续真实三包状态。

现仅待外部协调：原生GUI/系统picker/真实OS vault/Windows性能IME高DPI/真实堡垒机通道和生产上传；旧Go现场“选完文件不动”未结案；门户契约仍缺。最新一分钟回复对应旧Go请求，不继承旧证据、不重开旧实例。停止自行扩展和无变化测试，不抢前台、不读真实秘密、不生产上传、不向其他聊天发消息。收到明确新反馈或协调好的实际测试再继续同一目标；不是全部生产/Windows功能已验收。

---

# alpha.12 布局/配置迁移已接，最终回归进行中（2026-10-30）

最新冻结alpha.11（176项/519源码）；alpha.12完成编辑器置右/置左/置底、四区显隐/比例持久、实际拖动/方向键/双击比例重置、全局Ctrl/Meta+Shift快捷键与700小窗20号字体工具栏换行。原上下排列方案软件图发现文件列表挤压，已换为置左；探索图移到avalonia/evidence/alpha12-layout-exploratory-not-final，不当最终证明。合成SSH切换三布局保留同编辑草稿/原PTY且认证1；置左/置底/工具栏/导入确认截图已实际查看。

无秘密连接/账户分别导入导出、旧Go v1明确选择导入、新标识/跳板映射/拒秘密字段/导入取消/不继承信任或系统秘密标记、连接删除保护跳板与凭据半失败回滚已接，相关20项通过。正在单次最终全量，尚未alpha.12冻结。NSIS当前用户按版本安装/精确卸载与临时portable启动壳语法合成验证通过；正常C# runtime不启动Go，随后从同一冻结ZIP生成并解包逐文件比对。

最终三包/SHA/更新说明/现场清单仍待当前流程完成，不停在阶段包。原生GUI/系统picker/真实vault/Windows/IME/生产仍待外部协调；旧Go现场上传失败未闭合，不将旧回复和旧证据算C#通过。不抢前台/真实秘密，不向其他聊天发消息，唯一编辑者。

---

# Avalonia alpha.11 已冻结，继续布局快捷键与配置迁移（2026-10-30）

最终176项76秒通过，519源码/35软件图、正常四入口Win ZIP/Mac/symbols冻结，详docs/AVALONIA-ALPHA11-CHECKPOINT.md。Win ZIP SHA29c1a0670d0da94dd56c55eb218c91f4a9da8f7a69ae8d88c801355eea3a1dc1；Mac主入口7575bab7cda50072d2cdb1b523bffabd286e7d23d32e5ef250ab616cec782e31。TOTP独立账户/vault/严格解析/遮蔽复制/关闭清零/失败回滚与无秘密导出已接。静态240PE无未解析，正常无PDB；无真实vault/原生GUI/Windows/生产验证。

接着alpha.12布局持久化/快捷键与无秘密配置迁移，再最终Windows安装器/portable/SHA/说明/现场清单，不停在阶段包。最新一分钟复验回复属旧Go上传请求，未重启旧实例/未继承旧证据。唯一编辑者，不抢前台/真实秘密/生产，不向其他聊天发消息。

---

# Avalonia alpha.11 TOTP账户已接，正在完整回归（2026-10-30）

最新冻结仍alpha.10，旧包不改。当前alpha.11完成独立堡垒机账户系统vault存储、元数据无秘密/导出清除存在标记与停用验证器、严格Base32/otpauth独立参数、SHA1/256/512与6/8位、默认隐藏/当前周期复制/条件清剪贴板/关闭清零。22项定向通过：RFC全部算法大时间、无效URI、vault保存失败、元数据竞争回滚/保留其他账户、安全导出取消/变化、软件UI遮蔽/失败输入保留/当前复制。两张新软件图已实际查看，不是原生GUI或真实vault验证。

正在单次完整回归，尚未alpha.11出包。后续继续布局快捷键、无秘密连接配置导入导出与最终Windows安装器/portable和现场清单。最新用户的一分钟复验回复对应旧Go上传流程，当前无那个实例运行；没有启动旧实例或将旧证据算入C#。不抢前台/生产/真实秘密，不向其他聊天发消息。

---

# Avalonia alpha.10 已冻结，继续TOTP与布局快捷键（2026-10-30）

最终154项74秒通过，512源码/33软件图与正常四入口Win ZIP、Mac、symbols冻结，身份见 docs/AVALONIA-ALPHA10-CHECKPOINT.md。Win ZIP SHA284b0d334793f9b3dd4d60303d83f6354fda8b738437bb297b9b8f4cb0bcb20a；Mac主入口2b7b40a29d5a8758e592609a9e0f8865017e22cb571087e62abee4a4c5c26bb6。正常无PDB，240PE双表无未解析/VC外部导入，仍无实际GUI/Windows/真实vault/生产验证，最终安装器portable未出。

权限2000项/32层只读预览、后序/特殊位/文件留空保留/逐项复核/部分取消、SFTP与已知FTP CHMOD及同SSH独立安全sudo已接。sudo需已装Python3/no-follow/策略/用户路径空间确认，随机提示后本次密码清零，无自动安装或交互终端输入。SFTP目录SETSTAT非原子CAS边界保留。合成sudo临时目录模拟root，无本机系统sudo。

继续alpha.11 TOTP独立账户安全存储/显示隐藏复制/严格Base32和otpauth/导出无秘密，再布局快捷键与最终三包。旧包不动，不占前台/真实秘密/生产，不向其他聊天发信，唯一编辑者。

---

# alpha.10 最终154项通过，权限/sudo候选构建中（2026-10-30）

当前alpha.10最终单次154项全量通过，日志avalonia/evidence/alpha10-final-validation.log，74秒；在正常四入口两平台构建，尚未冻结新身份。普通SFTP/FTP权限计划、后序/保特殊位/跳过链接/变化与部分取消，sudo同SSH独立exec/随机明确提示/本次密码清零/device-inode/no-follow/拒exec保其他能力，以及权限UI确认取消/计数“小项”/小窗和sudo身份确认全部回环。sudo由自有夹具临时目录模拟root校验，没有执行本机系统sudo/读取真实凭据或生产。

正常SFTP文件走句柄FSTAT/FSETSTAT，目录走原路径LSTAT/SETSTAT复核，仍不能声称SFTP目录原子CAS或inode保障；sudo需既有Python3/dir_fd/no-follow/策略与用户确认路径空间一致，不安装不降级，不向交互终端输入。最新冻结仍alpha.9（145项/502源码）。

后续TOTP与布局快捷键，再最终Windows ZIP/安装器/便携、SHA、更新说明和现场清单。真实原生GUI/IME/系统picker/vault/Windows/生产仍待外部协调，不占前台，不向其他聊天发消息，不回旧Go上传。

---

# alpha.10 权限递归与安全sudo已接，分组验证进行中（2026-10-30）

最新冻结alpha.9（145项/502源码）不改。当前alpha.10新增有界2000项/32层只读权限计划、后序/特殊位保留/文件留空保留、变化与部分取消停止；SFTP文件句柄chmod、目录原路径SETSTAT复核、FTP已知SITE CHMOD。普通权限3项通过，安全sudo4项（原SSH、拆分随机提示、密码清零、替换节点拒绝、非随机提示不索取、拒exec保文件与终端）通过。未执行真实系统sudo，夹具仅自有临时目录模拟root校验，no-follow/fd能力实际执行。

权限窗口/确认取消/完成计数单位“项”、sudo路径空间确认和只在exec提示后询问本次密码的2项软件UI通过，已看普通预览/小窗/sudo结果图；新增9个rwx勾选正在最新定向。UI迟发TextChanged误使新预览过期已以参数签名修正。源SFTP目录LSTAT/SETSTAT仍非inode/CAS强排他，sudo需已装Python3/策略/同路径空间，不能忽略该边界或自动降级。

尚未alpha.10全量/出包。随后TOTP、布局快捷键与最终Windows ZIP/安装器/便携，原生GUI/IME/系统picker/真实vault/Windows/生产待经理协调，不抢屏，不碰真实凭据，不向其他聊天发消息。

---

# Avalonia alpha.9 已冻结，大文件流式与SFTP覆盖对等已收敛（2026-10-30）

最终145项54秒通过，502源码/30软件图、正常四入口Win ZIP、Mac和symbols冻结，见 docs/AVALONIA-ALPHA9-CHECKPOINT.md。Win ZIP SHA a1e257c3b64597f834c3556270fc288c3c4abbd454764cf80f2a0205c5b9364a；Mac主入口e3e49859d9feffc821e84cb817029d19718175ef078eb97974edeb4f76ba37cd。正常无PDB，240PE双表无未解析/VC外部导入；原生GUI/Windows/生产/真实vault未验，最终安装器portable未出。

独立SFTP/FTP来源32KiB流式，实际17,825,806字节四组合通过，无整文件RAM或额外登录；资源有序预留，FTP兄弟视图共享物理资源。仅同FTP控制连接复制保持16MiB限制，大移动走改名。SFTP覆盖需明确确认/固定旧版本/posix-rename，复制保留旧目标权限、原地移动保留源权限；目标/源变化/取消/双向并行/部分移动已测。经理指出的两项差距已按现实现收敛，不能再将alpha.8限制当当前事实。

继续alpha.10权限递归/sudo，再TOTP、布局快捷键及最终三包完整清单；唯一编辑者，不占前台/真实秘密/生产，不向其他聊天发信，不回旧Go上传。

---

# alpha.9 流式对等收敛，最终145项通过，构建中（2026-10-30）

最终单次145项54秒通过，avalonia/evidence/alpha9-final-validation.log。实际约17MiB四种独立SFTP/FTP流式组合、同FTP大复制拒绝/大移动改名、SFTP大移动确认覆盖、复制保旧目标权限、目标变化/取消/源读后变化保旧、双向有序预留、GUI覆盖确认/取消等通过；无额外登录票据。单FTP物理控制连接复制才保留16MiB清零RAM限制。上传/下载/编辑保存亦用单源预留，资源顺序/释放已接。

当前在正常四入口两平台构建，尚未alpha.9冻结身份。最新冻结仍alpha.8（134项/496源码），旧包保留；当前代码已不再是全协议16MiB/一律同名拒绝，矩阵与README已补当前行为。完整迁移仍待权限递归sudo、TOTP、布局快捷键和最终三包，外部原生GUI/Windows/生产仍待协调。不占前台/真实秘密，不向其他聊天发信。

---

# alpha.9 流式与SFTP安全覆盖已接，正在收敛对等缺口（2026-10-30）

当前最新冻结仍alpha.8（134项/496源码），没有重打旧包。alpha.9资源对象按稳定ID顺序预留两端、FTP兄弟视图共享物理资源；SFTP/独立跨源采用32KiB流式，FTP源关闭显式消费ACK，再复核源。实际约17MiB的四种独立SFTP/FTP组合及同FTP大复制拒绝/大移动改名5项通过，认证均1，无自动新登录。SFTP目标计划/明确覆盖/原权限保留、取消保旧、目标变化拒绝和双向并行预留及GUI覆盖分组通过；后台上传/下载/编辑也加入单源预留，相关31项回归通过。正在跑最新流式与源变化安全测试，尚未alpha.9全量或打包。

后续完成该阶段整体验证/冻结，再权限递归sudo、TOTP、布局快捷键与最终Windows三包。复制阶段限制只能在测试收敛后从矩阵移除；同一FTP控制连接复制仍16MiB、FTP不能安全覆盖时拒绝。依旧唯一编辑者、不占前台/生产/真实秘密，不向其他聊天发消息。

---

# Avalonia alpha.8 已冻结，下一项大文件流式复制对等（2026-10-30）

最终单次134项50秒通过，496源码/29软件图、四入口Win ZIP、Mac与独立symbols冻结，身份见 docs/AVALONIA-ALPHA8-CHECKPOINT.md。Win ZIP SHA1a2cb366a7d66ea9899b151f7d5948299263b7f4807f48fe950d5161a42771fb；Mac主入口c57e1e768110b42f843214ba8c2d9bfc259760098d4517471ca126dd62fc9b48。正常包无PDB，240PE双表无未解析/VC外部导入。原生GUI/Windows/真实vault/生产未验，最终安装器portable未出。

路径恢复/FTP父链接/私有剪贴板/文件复制移动/目录CRUD已接并回环。全量过程中旧重叠日志不采用；Paramiko弱引用dead channel夹具已定位修正，最终单次全量通过。大文件与覆盖仍非对等：本版全16MiB内存/同名拒绝，经理已要求完整交付前恢复旧Go仅同FTP控制连接受限、其他32KiB流式、有序资源预留与>16MiB测试，以及SFTP安全覆盖确认。计划docs/AVALONIA-STREAMING-COPY-PLAN.md。

继续alpha.9先收敛这两项，再权限sudo/TOTP/布局快捷键和最终三包。旧包不动，不占前台/真实秘密/生产，不向其他聊天发消息，唯一代码编辑者。

---

# 复制迁移对等缺口已确认，后续必须补齐（2026-10-30）

经理独立读代码指出：当前alpha.8全部复制16MiB RAM只是阶段实现。旧Go dev9只有同一FTP控制连接复制受16MiB限制，SFTP/独立跨源为32KiB有界流式。完整交付前必须恢复SFTP及可并行独立源大文件有界流式/中转，不额外登录票据，保留源目标身份/SHA复核/取消保旧/源变化保留/资源有序预留，并实际测试>16MiB。同名拒绝也不是最终对等：SFTP安全覆盖选择必须迁移，FTP无原子保证可明确拒绝。矩阵/README已写明，不能宣称全部对等。

按经理要求不打断当前alpha.8冻结流程；随后先收敛这两项，再权限sudo、TOTP/布局快捷键和最终三包。只在本聊天/本地交接记录，不向其他聊天发信。

---

# alpha.8 文件操作/路径恢复分组通过，完整回归与构建进行中（2026-10-30）

当前最新冻结仍alpha.7（120项/486源码），alpha.8在做最终完整回归/正常四入口构建。FTP父目录链接拒绝、REALPATH拒绝/不支持明确绝对路径恢复已分组通过；四种SFTP/FTP复制/移动、源变化只保留已提交副本并报告部分移动、同名/关闭源保护6项通过。目录SFTP0700创建/FTP服务器策略、改名、非空拒绝/空目录确认删除2项核心通过；文件区私有CtrlC/X/V跨视图绑定与目录菜单GUI、路径恢复共5项软件UI回环通过，已修空目录列表不能聚焦及自动刷新抢操作锁的问题。

截图 cross-source-copy-move / directory-actions / realpath-explicit-recovery在avalonia/evidence/ui-0.3.0-alpha.8；后两轮待稳定刷新后再保存，移动进度保留已复制字节。当前无alpha.8冻结包。权限递归/sudo、TOTP及布局快捷键和最终安装器portable仍待。旧包不动，唯一编辑者，不占前台/不碰生产或真实vault，不向其他聊天发消息。

---

# alpha.8 文件操作与路径恢复已实现，整体验证准备中（2026-10-30）

最新冻结仍alpha.7（120项/486源码），当前alpha.8未出包。FTP父目录逐级LIST拒绝链接，核心/FTP UI14项通过；REALPATH明确拒绝/不支持保留未知CWD通道，不猜根、不探测，用户确认绝对路径后才读取并建立探测锚，恢复/取消/上传认证1的3项通过。

跨SFTP/FTP四组合复制移动、源变化后保留源且报告部分移动、同名/关闭源保护6项通过；SFTP0700目录/FTP目录创建改名及非空删除拒绝2项通过。应用内文件剪贴板/文件列表CtrlC/X/V、跨来源等待确认后切视图仍固定原目标、目录GUI确认删除等UI已接并通过，正补稳定渲染/完整回归。复制限16MiB普通文件不递归，已有目标不覆盖；FTP无通用原子排他/替换保证。

随后权限递归/sudo、TOTP、布局快捷键和最终安装器portable仍待，不将旧Go证据当本工程通过。所有测试仍自有回环/软件渲染，不抢前台/生产/真实vault，不向其他聊天发消息。

---

# Avalonia alpha.7 已冻结，继续文件安全边界与操作迁移（2026-10-30）

最终120项47秒通过；486源码/26软件图、正常四入口Win ZIP、Mac与独立symbols已冻结，身份见 docs/AVALONIA-ALPHA7-CHECKPOINT.md。Win ZIP SHA 323cbeca75e24cc8fe9ddcec3175eda4f36e781b89ca531854d1e4f005130630，Mac主入口SHA 0e3c27d1a0719f56782aed9a355021bc9e8be06268d357c6f1bbfbaaa60ee6e9。正常包无PDB；240PE双表扫描无未解析/VC外部导入，icu系统版本边界保留。无原生GUI/Windows/真实vault或生产运行，无最终安装器portable。

FTP入口/文件流/另存新名/同认证兄弟视图/SSH+FTP四区双方认证1已接；修复alpha.6切页解绑PTY，隐藏继续读取/返回输入已验证。协议/账号端点变化不转用旧保存密码、后台队列与标签顺序已修。FTP无通用原子排他/替换，已有目标拒覆盖；父目录链接身份还需补强，不能据此宣称完整FTP安全迁移。

继续alpha.8先FTP父链接复核和SFTP拒REALPATH后的明确绝对路径，再复制移动/权限sudo；TOTP和布局快捷键等仍在清单，不遗忘。旧包不改，唯一编辑者，不占前台/生产/真实凭据，不向其他聊天发消息。

---

# alpha.7 最终120项通过，FTP与终端生命周期候选正在打包（2026-10-30）

alpha.7最终120项全量47秒通过，日志avalonia/evidence/alpha7-final-validation.log；FTP GUI四区/另存/上下传/双方认证1、丢失数据/改名ACK不虚报或重登、协议NOOP/提交点取消，以及工作区隐藏继续读取PTY/返回原输入、旧密码不转用新端点/协议/认证方式均已覆盖。已实际查看ssh-ftp-four-areas和ftp-connection-test软件图，未原生GUI。

当前在按新FluentFTP依赖更新诊断入口锁文件并发布正常四入口两平台，尚未alpha.7冻结身份；alpha.6旧包保持且报告已注明切页解绑缺陷。下一项仍复制移动/文件权限sudo、TOTP、明确绝对路径恢复和布局快捷键，完整迁移未完。不占前台、不操作生产/真实vault，不向其他聊天发消息。

---

# alpha.7 FTP与终端重挂载修复已回环，最终回归/打包进行中（2026-10-30）

FTP入口/保存协议/后台串行上下传/严格数据完成ACK/UTF-8/BOM另存新名/与SSH四区并行已接，两个来源各认证1。标准FTP无原子排他或替换保证，已有目标拒绝覆盖；不自动重连、会话轮换或重试票据。8项原核心+FTP NOOP/提交点取消、3项FTP GUI/连接测试与旧凭据防转用测试均已分组通过。

实际组合UI发现并修复alpha.6终端库在逻辑树detach后解绑PTY读写：新WorkspaceTerminalControl在整个工作区生命周期持有reparent保护，切标签/连接库/来源关联保持读取，关闭显式Dispose。实际隐藏时服务端输出仍进入VT，返回后原PTY输入有效，认证和shell仍各1。alpha.6旧包/报告已补记限制，不改旧包。

119项全量48秒曾通过；随后补认证方式切换守卫，避免切到私钥时错误要求新密码，正跑最终120项并打包。新增密码协议/账号地址变化禁止留空自动转用的UI/保管库守卫，无真实凭据访问。尚无alpha.7冻结包。后续继续复制移动、权限sudo、TOTP、明确绝对路径恢复和布局快捷键；不抢前台，不向其他聊天发信，不回旧Go上传。

---

# alpha.7 正在接FTP与统一文件/编辑来源（2026-10-30）

最新冻结包仍alpha.6（102项/474源码），未重打旧包。当前源码alpha.7先修关闭后新建标签稳定顺序（SourceModel5项通过），新增FluentFTP55.1.0 MIT和仅测试pyftpdlib2.2.0。FTP单认证列表/流式上下传/完整SHA/字面中文空格%20/同名拒绝/提交ACK不明停源不重登、UTF-8/BOM另存新名8项核心回环通过；队列改后台执行，防UI线程做传输。文本来源适配回归21项和连接/来源/队列相关25项通过。

标准FTP没有通用原子排他/替换保证，不主动覆盖已有目标，在线编辑原文件保留，提供另存新名；不能把FTP当SFTP的EXCL/posix-rename。FTP协议入口/多来源文件视图/在线编辑适配已接，尚待新UI实测和完整回归/打包。保存/测试连接新增端点或协议变化时不得自动转用旧存储密码的守卫，测试待补。

下一步FTP完整界面/SSH+FTP并行与权限降级核验，随后复制移动、权限sudo、TOTP、REALPATH拒绝明确路径恢复及布局快捷键等完整清单。依旧不占前台、不碰生产/真实vault或agent，不向其他聊天发消息；不要回到旧Go上传。

---

# Avalonia alpha.6 已冻结，继续FTP与文件操作（2026-10-30）

最终102项全量50秒通过，474源码/24软件图、正常四入口Win ZIP与Mac包、独立symbols已冻结。Win ZIP SHA e10303be2d601dd94e262cffc850febd3e1418793d835a6efe50dc1565e420f0；Mac主入口SHA 9e4ffd5f99068e8b18460d9a5beaca99b0ab3d1fc27963297f5029c7b9acee88。身份/源码tar/SHA/PAYLOADS/SIZE见 docs/AVALONIA-ALPHA6-CHECKPOINT.md。正常包无PDB；239PE双表扫描，Win10 icu.dll版本边界保留；没有Windows或原生GUI实跑，没有最终安装器portable。

已接明确多来源独立视图/只隐藏被关联文件协议顶部页/SSH不隐藏/解除和所属关闭恢复，懒树按需/有界/收起晚回复、草稿原来源/解除保留/关闭只读。最终已修正TreeView基础模板空白并渲染查看，以及首次延迟TextChanged丢目录首屏。各来源认证1/原picker目录绑定通过。继续alpha.7：先固定关闭后新标签稳定顺序，再FTP与复制移动/权限sudo等完整清单；旧版本不改，不向其他聊天发消息，不占前台或连接生产。

---

# alpha.6 多来源与懒树已接，完整回归进行中（2026-10-30）

当前C#源码0.3.0-alpha.6；alpha.5包/466源码已冻结保留。独立文件视图借用原SFTP通道，明确关联、只隐藏被关联文件协议顶部页、SSH不隐藏、解除最后关联/关闭所属工作区后恢复；工作区/来源独立，不自动按IP匹配或重新认证。草稿标签/确认含来源，解除关联保草稿/队列，关闭原来源只读保草稿。

新7项模型/真实回环软件UI验证通过，含正常关联按钮→选择来源→对应顶部页隐藏、选择器等待时切换仍写原来源原目录且双方认证各1、解除/所属关闭恢复、懒树按展开读取/收起晚回复忽略，以及关联草稿来源关闭。重构回归发现并修正Avalonia延迟初始TextChanged使首次目录列表被误判过期的问题。现正完整102项回归，尚未alpha.6打包/GUI。旧alpha.5四区95项检查点仍是最新冻结包。

后续继续FTP/文件复制移动/权限sudo及布局快捷键完整清单；不向其他聊天发信、不占前台、不重跑旧Go上传，不将软件图当原生或生产验收。

---

# Avalonia alpha.5 已冻结，继续 alpha.6 文件导航与多来源（2026-10-30）

95项全量通过，466源码、正常四入口Win ZIP、Mac与独立symbols已冻结，源归档/包身份和待验边界见 docs/AVALONIA-ALPHA5-CHECKPOINT.md。Win ZIP SHA256 2b545871fe5757e84cbd3ae0b9c499fb5213255cdee5dc3c3206f265c044316e；Mac主入口SHA 9244b2adf9bb0561a04543297754b79c55bd7040dfbbad3f85ee2257b2659cfa。正常包无PDB。21软件图不等于原生GUI；没有Windows运行、真实vault或生产验证，安装器/portable未出。

四区、原生安全编辑与同SSH真实Linux逐核/网络监控已接；自动3秒/隐藏暂停、四能力并行认证1与权限降级通过。239 PE新增双表解析补全ARM64与延迟导入扫描，未见VC外部导入；发现icu.dll系统版本边界，Win10 1809不能宣称可用。继续alpha.6懒树、多来源明确关联、只隐藏被关联文件协议顶部页；再FTP/文件操作与权限sudo。旧包不动，持续本地可做项，不等待“继续”，不抢前台。

---

# Avalonia alpha.5：四区、安全编辑与真实监控已实现，95项全量通过（2026-10-30）

当前唯一编辑者继续 C# 迁移，没有回到旧 Go 测试。alpha.4 冻结包/456源码已补齐真实身份，见 AVALONIA-ALPHA4-CHECKPOINT.md；alpha.5 新增 AvaloniaEdit、固定来源多草稿、UTF-8/BOM/换行、1MiB打开保存边界、内存快照排队保存/明确确认/远端哈希冲突/新建排他/未保存提示，以及同SSH独立exec真实Linux逐核/内存/磁盘/网络趋势。默认手动、3秒可选自动、隐藏取消暂停、首样未知、8秒可见截止/1MiB输出/防叠加与拒exec保其他能力已接。

95项本工程全量通过（日志 avalonia/evidence/alpha5-final-validation.log，47秒）；四能力并行认证1/权限降级、3秒自动与隐藏、保存提交点取消不误报、队列取消保草稿、三尺寸深浅软件渲染通过。正在生成独立正常四入口Win/Mac原型包和符号；最终身份待包完成后补。不是最终安装器/portable，尚未实际GUI/Windows/生产或真实vault。仍需懒树、多来源关联隐藏对应文件协议顶部页、FTP及复制移动/权限sudo等；后续继续，不等待用户再说继续。

补记：发现3个旧测试曾硬编码alpha.3截图目录，alpha.4全量覆盖了3图；alpha.3报告已明确失去原图归属，原摘要/当前摘要另存审计，包和源码未修改。所有截图输出现在按程序集实际版本隔离。禁止再将历史Go PID45327当C#验收。

---

# Avalonia alpha.4 已冻结，继续 alpha.5（2026-10-30）

alpha.4 最终 75 项测试通过，456 源文件与检查点/四入口 Win ZIP/Mac 包已冻结。此前“尚未全量/打包”是历史状态。准确身份、测试和待验项见 `docs/AVALONIA-ALPHA4-CHECKPOINT.md`；Win ZIP SHA256 `c2d14e985f134a4c79c4a5f047991820933069c0da0211dbe195be5653fffa88`。正常包不含 PDB，符号独立保留；原生 GUI/Windows 实跑未验，完整迁移未完成。

继续 alpha.5 的同 SSH exec 真实监控/安全在线编辑与四区工作区，再推进懒树多来源、FTP、文件操作及权限 sudo。历史 Go PID45327 仅旧版证据，不能充当 C# 验收，不重复旧上传。当前聊天唯一代码编辑者，不抢屏、不连生产、不向其他聊天发信；保留所有旧包与用户修改。

---

# alpha.4在继续实现agent、跳板、心跳与会话UI（2026-10-30）

alpha.3包/443源码检查点已冻结不改。当前工作源码已升0.3.0-alpha.4，独立Alpha4数据目录；新增已有本机agent协议签名（不启动agent/不读私钥）、带一次性随机认证且固定目标的私有跳板relay、保存跳板链/循环上限、复用已认证跳板、可靠全局请求应答心跳及单飞SFTP只读探测、偏好主题/字号/间隔持久、同认证新页与成功自动收导航/手动epoch优先。

本工程新定向通过：owned agent实际签名1项；真实跳板/拒转发保留原能力2项；协议/SFTP探测开关、无应答不叠加且断开不重登、SFTP超时保留shell3项（48.7876s）；跳板循环/复用、偏好持久与clone原页关闭/导航手动覆盖/小窗工具条6项。没有接触用户SSH_AUTH_SOCK/真实agent，只有显式测试peer；尚未alpha4全量/打包/GUI。正继续私有relay安全边界及整体验证，见avalonia/evidence/alpha4-*.log。仍不抢前台、不连生产、不向其他聊天发消息。

---

# Avalonia alpha.3检查点已冻结（2026-10-30），完整迁移继续

安全上传/下载临时文件+完整SHA与双端版本复核、原子覆盖确认/保权限、SourceId/目录绑定、元数据超时、取消保旧与回执不明隔离原文件通道；有界串行队列/小窗UI/标签关闭已接。系统vault API、显式保存/失败保留/回滚、持久指纹与私钥口令UI已接；真实vault读写未试，Mac只加载符号不访问凭据。57项最终16.9900s通过，12软件图按版本归档；未实际GUI/Win/系统picker/生产上传。

正常四入口Win ZIP `f97d5044861461c6d18513afa3c09b2793651dbd9261605bd5f4a77c59fc97fb`，51,770,085B（49.37MiB）/展开114.71MiB，无PDB；symbols另包25.84MiB，所有DLL/许可保留。Mac正常app SHA `7e5c19177bd9da02ca5dbddd98097e8e50b8aad2d9680fb4f0d92e313c709d60`，118.18MiB/签名通过未启动GUI。443源码与tar/manifest相符，正常包主托管payload另有SHA；详情docs/AVALONIA-ALPHA3-CHECKPOINT.md与release/IDENTITY、PAYLOADS、SIZE、SHA256SUMS-Avalonia-0.3.0-alpha.3。

旧alpha2/native/Electron包和用户窗口不动；新Alpha3数据独立，不自动迁移旧密码。继续agent/跳板/心跳配置与会话复用，再继续在线编辑/真实监控等完整矩阵，不把原型或静态无VC导入说成干净Win已实测，不把模型picker当现场失败结案。经理独立只读审查，实际GUI另协调；程序员不抢屏/不碰生产/不向其他聊天发信。

---

# Avalonia alpha.2检查点已冻结（2026-10-30），完整迁移仍在进行

C#四入口/精确ArgumentList/URL一次解码/当前用户有界IPC与三秒HMAC去重，临时凭据只内存、不自动KI回填/重放；多轮/重复密码/取消/私钥core/rekey指纹缓存已接入，终端反压只关自身通道。31项本工程测试5.9041s通过，Mac实际无窗口诊断转交与打包runtime-check通过；Windows只交叉构建。新9图按版本在avalonia/evidence/ui-0.3.0-alpha.2/，不冒称原生GUI/物理150%。

正常Mac release/Forterm-Avalonia-0.3.0-alpha.2-macos-arm64.app，apphost SHA `175a0e0b7c454d26a2935f354ff3c6bd255594cb27b45a93d03152a50272d1f8`，签名通过未启动GUI。Win四AMD64入口原型ZIP SHA `ebc3f4384ac3594410e09cba92171d3453219a601c26929dd0baf4c1d55a07d2`；实际托管Forterm.dll SHA `6b11830361ae59a9adc4a95cd18dd162ef92895be8bc62e530dc0a3771228d0a`（.NET EXE apphost跨版本可能相同，必须看托管payload/整体ZIP）。430源码与tar/manifest匹配；身份与PAYLOADS/SHA/证据见docs/AVALONIA-ALPHA2-CHECKPOINT.md。

递归237PE静态链无vcruntime/msvcp/未解析项，self-contained不等于干净Win验证。新版默认独立AvaloniaAlpha2元数据，FORTERM_AVALONIA_DATA可显式隔离数据和IPC；不读取/替换旧库。alpha1/native11/Electron旧包与用户运行窗口保留。完整矩阵avalonia/docs/MIGRATION.md；agent/跳板/vault/高级安全文件/远程编辑/真实监控/队列及最终安装器portable等仍待。经理协调实际GUI，程序员不抢前台/不向其他聊天发信/不碰生产。旧ui-alpha1目录曾被测试复用，不能单独当冻结alpha1精确图证；报告已补记，新图改版本命名。

---

# Avalonia alpha.1首纵向检查点已冻结（2026-10-30），完整迁移继续

独立C#工程avalonia/，无Go后台。官方SDK10.0.403/Avalonia12.0.4/真实XTerm.NET终端和Porta.Pty/SSH.NET共享transport源码扩展已打通。9项本工程测试8.8321s通过：SSH认证1/双PTY+SFTP/1MiB+13内容一致/拒通道隔离/租约存活、ownedUnixPTY、真实headless Tab/CtrlC/resize/鼠标选中/复制粘贴焦点、ANSI/alternate/中/组合/ZWJ模型；8图已渲染查看，非可见GUI。

正常Mac原型release/Forterm-Avalonia-0.3.0-alpha.1-macos-arm64.app，SHA `aee64c1cd008dfd146c009a502c9c5ad1d7145c3fec6c20736394ed739828794`，签名通过未启动GUI；self-contained包--runtime-check在无有效DOTNET_ROOT下加载Skia/HarfBuzz成功零窗口。Win原型ZIP SHA `d866fb625a2615029f7462045678f29b7d360e121666026d03379dca05135a89`，CRC通过，232PE静态链无vcruntime/msvcp，干净Win仍未实跑。417源码/锁文件/许可与tar匹配，身份见release/IDENTITY-Avalonia-0.3.0-alpha.1.json。

详情docs/AVALONIA-ALPHA1-CHECKPOINT.md，完整缺口avalonia/docs/MIGRATION.md。此为首原型，不是四入口/安装器/portable与完整迁移交付；多轮认证/票据入口/编辑监控/安全文件高级能力继续做。经理可协调此独立Mac候选实际GUI，程序员不抢屏；独立bundleID/元数据，alpha1尚无单实例IPC，不会转交旧Go。旧dev11包/用户dev9窗口保持，未生产操作。请先读此节，历史Go交付不等于C#完成。

---

# 2026-10-30：用户授权C# + Avalonia完整重做，首纵向检查点进行中

唯一编辑者正在独立avalonia/目录实现0.3.0-alpha.1，无嵌套Git、不复用Go后台。旧Electron/native源码配置、全部包和用户dev9窗口保持。官方SDK10.0.403仅独立缓存，Avalonia12.0.4、Iciclecreek4.1.2/XTerm.NET3.0.0/Porta.Pty2.2.3、SSH.NET2026.0.0 MIT源码最小共享transport补丁已接入；版本和许可证/具体未迁移项见avalonia/docs/DEPENDENCIES.md、MIGRATION.md。

首原型实际VT、SSH shell、SFTP流、原生StorageProvider调用与卡片界面均已编译，self-contained win-x64/osx-arm64初版发布成功。正在本机合成回环核验认证1/通道隔离/引用、VT键盘/resize/ANSI/alternate/CJK/粘贴；旧Go测试不算C#通过。软件渲染图位于avalonia/evidence/ui-alpha1，尚未启动可见Mac窗口、不占前台、不连接生产。完整原生picker/物理150%与Windows现场仍待。

初步递归PE import检查（232个PE，包含包内x64/arm64 OpenConsole）未见vcruntime/msvcp，所有导入为app-local/API set/已识别Windows组件；这只是静态调查，不能称干净Windows已运行/零系统依赖，完整native报告见evidence/native-imports.json。不从用户System32复制DLL，不自动要求装VC++。

当前是重写中的首检查点，不是完整迁移交付；全能力矩阵逐项维护，不忽略四入口/票据规则/多轮KI/跳板/安全编辑/监控/FTP/权限sudo等缺口。生产门户契约缺口和旧dev3上传现场失败继续保留。不再扩Go/Fyne外观。

---

# dev.11原生按钮修订已冻结（2026-10-29）

按用户“那就调整啊”完成连接底栏：34dp/6dp圆角/14dp字和留白的原生按钮组件，左测试/运行时取消，右取消/144dp绿色保存并连接/有提示的仅保存信息菜单；30dp图标、可访问名称、hover/pressed/disabled/focus、Enter/Space与菜单Escape/Enter。提示留在同一弹窗内不抢Entry焦点，菜单/tooltip向上避让；宽度按内容换行，滚动到底修复保持。经理已看首轮软件图确认方向；未做GUI。

完整正常Mac release/Forterm-Native-0.2.2-dev.11-test-macos-arm64.app，执行SHA `4cdb2f372d3ca31cca6a2ba69bdb331a7ac337a164382d502032a838c368fc03`，arm64/adhoc通过未启动；Windows审查ZIP SHA `fbbb84d052d90dc4dbf163a389fcd4d5ccdadfd77be743dd6d4c2202ee8db63a`，主PE `417bb5732f404dceb69b32f3fd8d121064dc2fedc20886c82b52b7ed6f23ddfe`，CRC/四入口AMD64通过未运行。465源码/tar/内嵌助手一致，仅5项相对dev10新增变化；identity/检查点见docs/NATIVE-DEV11-DELIVERY.md。

最终race80.854s、最终滚动六尺寸/150%8.800s、按钮软件审查13.236s及vet通过；15图在native/evidence/ui-0.2.2-dev.11/final/（尺寸SHA索引）。仅保存菜单进入原丢弃密码确认，未增加秘密持久化。dev10旧包与用户dev9窗口不动；正常共享IPC不能把旧窗口认成11，不强关。新系统GUI/Windows/生产上传仍待，新安装/便携版未生成。详NATIVE-DEV11-DELIVERY与REVIEW-NOTES-dev11。

---

# dev.10连接卡片与末项滚动候选已冻结（2026-10-29）

已完成用户最新连接参考图/“滚得到但不到底”修订：原生四类导航、分组卡片、同排地址端口/用户密码、深浅与窄窗响应式、独立固定footer/gutter；Entry纵向滚轮转交、完整范围重测、焦点自动揭示，同类认证/权限/本次密码视口共用。dev9功能与密码/测试连接规则保留。用户正在使用的dev9窗口/包不变，未抢前台。

正常完整Mac release/Forterm-Native-0.2.2-dev.10-test-macos-arm64.app，arm64+adhoc通过、未启动，执行SHA `6fd50be6b0b0afe7c8192ca6690042cb5f5869d485caf47c57eceed86f184d6c`。Windows四入口审查ZIP SHA `83b45e2b2db285c134dbb94e1abc06001f61a409f7cab6e6183b881daff890e6`，主PE `416927ad4e720b21f040e8ba33147bbdf7bdcf52cb80cad817fa044abce929d6`；CRC/AMD64通过。463源码与tar/内嵌助手匹配，检查点native-0.2.2-dev.10-review-20261028；身份见release/IDENTITY-Native-0.2.2-dev.10-test-macos-arm64.json。

最终定向race341.634s、轨道/输入长值race16.177s及vet通过；17张软件图/尺寸SHA在native/evidence/ui-0.2.2-dev.10/review/。具体改动/证据/限制见docs/NATIVE-DEV10-DELIVERY.md及native/REVIEW-NOTES-dev10.md。未原生系统窗口/Windows运行/生产上传验证；新安装器/便携版未生成。先审图再协调GUI，不把共享IPC复用的dev9窗口当dev10，不强关现有会话；不将旧上传前台许可混同成新界面实测。

---

# dev.10连接卡片与末项滚动修订进行中（2026-10-29）

用户已实际运行dev9并反馈“能滚动，但滚不到下面内容”，同时要求按XTerminal参考结构重做。当前唯一编辑者已实现原生分类导航/分组卡片/响应式同排字段/固定footer及专用gutter；修复输入框内部scroll吞纵向滚轮，展开/收起和resize后重测范围，Tab焦点自动揭示。16问题认证、权限、本次密码表单复用视口。保存/显式测试/认证流程保留，未做代理等装饰假功能。

首轮软件滚动/拖动/末项/折叠/窄窗回归通过；最终定向race和新正常Mac/Windows候选构建进行中。当前软件图与日志位于native/evidence/ui-0.2.2-dev.10/及dev10-*.log；旧迭代原始失败记录保留，最终记录以新交付文档为准。恢复点checkpoints/native-before-dev10-scroll-20261029/native-source.tar.gz。

用户dev9窗口及包保持，不抢前台，不关闭/替换会话。新的前台上传复验许可不自动混用为本次连接界面实测；本轮按经理要求只离线软件/编译验证，待新候选审图再协调运行。不得继续写成“用户仍使用dev8”或认为dev9已验收所有UI。开发范围以这次真人反馈为准，旧24小时边界已被后续授权替代。

---

# 当前待验证（dev9，2026-10-29）

dev9综合实现已冻结正常完整Mac与Windows四入口审查ZIP，相关测试通过，详NATIVE-DEV9-DELIVERY.md。当前剩余：经理独立复核459源码/内嵌助手/包与边界；由用户决定何时保存退出dev8或明确并行隔离目录启动9；实际首绘、Tab/新页、原生Open/Save、窄栏/高DPI、真实当前服务器文件与exec权限；生产上传/提权不得由程序员代测。正常实例IPC会复用旧版，不能误认新版本已启动。Windows新包实际复制/执行仍由经理协调，不把交叉编译当通过。

本轮新增项实现及有限支持（仅普通文件复制、同FTP16MiB、安全覆盖能力、sudo依赖）均在交付文档明确。旧内容为此前状态历史，后续以本节和PROGRESS首节为准，不重复旧测试/催解锁或操作用户前台。

---

# 当前原生工作与待验证项（2026-10-29）

## 最新交付与协调

用户已解锁，明确要求完整Mac实际连接版。正常cmd/forterm入口的 `release/Forterm-Native-0.2.2-dev.8-test-macos-arm64.app` 已基于冻结源码构建，正常SSH/SFTP/配置/vault/IPC，非预览；SHA c3aad24733444c1864c67bd9afaf98de8750c31bd349c47fc6b36b81a83ab339（可执行文件）。程序员不启动/不读凭据/不连接，经理启动后用户自行连接。身份见对应release/IDENTITY JSON。Windowsdev8-test已建但ZIP未复制；旧“等待解锁”均为历史，不再是当前阻断。


## 当前独立审查与实际运行状态

经理已独立核对dev.8 ZIP CRC/四AMD64入口、414冻结源码/tar与Mac预览摘要，并在隔离冻结副本运行冷启动一致性race通过4.135s；记录 `native/evidence/ui-0.2.2-dev.8/independent-manager/verification.json`。这仅是软件驱动；dev8原生首帧、Windows运行未做。现阶段停止无意义轮询，等待已有解锁问题的回复及经理后续操作；不新增测试/占屏/催问，冻结身份不变。

经理已按用户许可完成dev.7 Mac实际离线UI检查，PID92827已正常退出。深浅页、静态四区、导航实拖约40逻辑px、700×433逻辑小窗更多/刷新和导航收起有7张原始截图，摘要校验归档 `native/evidence/ui-0.2.2-dev.7/independent-native-mac/`。这不是Windows/真实服务器/上传或真实150%显示缩放；本轮键盘分隔及重启持久化也未实测。此前“用户未答复/未启动”的原始记录保留为历史。

首次深色页末位0/8裁剪、切浅色和重排后正常，外观尚未全部验收。当前dev.8已离线复现runGL监听安装前的字体旧测量85.359375px与当前字体87.515625px相差2.15625px，并将主题前置到首批控件创建前，避免再次迟设主题。修后同批名称显式/隐式测量一致、主题仅设置一次的race回归通过；相关UI定向race22.332s/冷回归3.444s及vet完成；414源码的新dev8 Windows审查ZIP与未启动的Mac预览已冻结，身份和首帧方案见 `NATIVE-UI-REVIEW-DEV8.md`。不能将此离线结果当dev8原生首绘已通过；dev7原包/源/图不改。

Windowsdev7复制获授权，目标 `192.168.2.2 D:\Tools\Forterm-Native-dev7-test` 已创建但仍空：粘贴未成功后Mac锁屏，解锁请求已一次提出、等待中；没有文件复制成功或新Windows运行。经理独占远程UI，程序员不触屏/不重复催问。

## 已冻结并审查通过的业务基线

0.2.2-dev.5审查ZIP SHA256 `a823930a95c8f57b797ab54ca205bb4ca320bf03d815c18a6486e0cb7a08d609`：经理独立核对CRC、四AMD64入口、405条源码及tar；冻结副本race验证控制器tab/newwin、关联视图关闭、Windows URI（desktop6.113s/localfile1.376s）和隐私白名单/关闭队列/短窗队列（desktop2.866s）通过。transfers无匹配项不计独立测试通过。原始记录已归档 `native/evidence/release-0.2.2-dev.5/independent-manager/verification.json`。

已确认修复借用视图关闭后的覆盖等待及展开队列高度/可见性；受限阶段报告通过审查。未知原生现场“选完不动”的具体原因仍未确认，dev.5未实际Windows运行或生产上传。dev.3完整ZIP/安装器/便携是已交付试用基线，不再称“尚未三包”；dev.2/dev.3旧待审状态已过时。原生picker PID45327复验已闭合，不再等待解锁或重复询问同项。

## 当前结果与后续

已在0.2.2-dev.7实现原生主工作台分组树/虚拟六列表格、协议筛选搜索、行内操作、嵌入堡垒机及统一工作区视觉。经理指出的中文衬线回退已改为系统CJK sans主字体（当前Mac PingFang SC；Windows路径待实测），不分发系统字体，终端等宽/宽字不变；窄窗固定更多内有刷新。软件深浅/小窗/canvas1.5倍率及相关race、预览凭据拒绝器和行选择焦点验证通过，来源与截图见 `NATIVE-UI-REVIEW-DEV7.md`。

dev.7的413条源码ZIP与Mac离线预览保持冻结，并已完成上述实际Mac检查。当前dev.8只修首绘主题初始化顺序，新身份构建已就绪、不覆盖dev7；原生首帧复核仍待协调。实际dev8原生首绘复验由经理后续协调；当前不抢前台、不操作Windows App、不做生产上传。

## 待现场或后续验收

1. 原生上传：待用户以后愿意进行一次正常小文件上传并主动提供固定阶段JSON，同时确认真实版本与卡点；由经理统一协调，不反复催问，不自动生产上传/重登/重放票据。最小方案 `NATIVE-UPLOAD-FIELD-PLAN-DEV5.md`。用户暂用已有成功记录的Electron0.1.7，不推断原生从未运行，也不将相邻应用业务日志当SFTP错误。
2. Windows性能：已收到空配置报告，但Forterm按钮0样本不足以确认旧卡顿修复；尚需当前最终构建真实操作反馈。已有diag1/diag2报告和冻结工具保留，不重复请求已做步骤。Mac长负载仍有峰值，不能推断Windows根因。
3. 新外观软件阶段已完成：1000行/130模板、搜索/筛选/回收操作/焦点、1050×650与真实canvas1.5倍率、深浅与中文主字体、三向编辑和四区截图通过。经理已完成dev7实际Mac检查，当前只余首绘末位裁剪的dev8原生复验；真实Windows/键盘分隔/重启持久化仍待；此前小偏好页本地截图失效仍不当实际原生通过证据。
4. 最终选定版本的Windows实际启动/按钮、高DPI/IME、统一四能力并行及权限降级、真实目录上传、安装/卸载仍需现场确认。按最终代码生成四入口ZIP/安装器/便携、SHA、更新说明和准确边界，独立复核；不重打冻结dev.5或历史包。
5. 厂商门户适配仍缺脱敏契约，独立于上述任务；不猜生产接口。

保持唯一源码编辑者，只改Forterm；不扩诊断框架，不重跑无变化全量测试，不推送发布。原历史清单已完整保存在dev.5检查点的NATIVE-REMAINING-before-manager-review.md，以本清单为当前状态。
