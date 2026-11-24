# alpha.36 XTerminal 风格本机文件树已完成（2026-11-11）

本机文件树按参考图改为整栏，目录和文件叶节点同时显示，目录按需展开/收起，终端和资源监控并列。完整回归 241 项通过；alpha.36 Mac 原生窗口已启动并展开 `/Users/xianyu`，真实窗口可见文件叶节点。

alpha.36 Windows x64 ZIP、安装包、便携版、symbols、SHA256 和交付说明已生成；ZIP 293 文件且四入口/安装包/便携版逐文件校验通过。Windows 实机与真实堡垒机仍待现场。

# alpha.35 本机目录树已完成（2026-11-11）

本机文件区现在有懒加载目录树：展开只读取当前目录子目录，收起释放分支，点击树节点同步右侧列表；本地路径边界、刷新、返回上级和远程关联降级逻辑保持。完整回归 241 项通过，alpha.35 Mac 原生窗口已启动并展开 `/Users/xianyu` 目录树。

alpha.35 Windows x64 ZIP、安装包、便携版、symbols、SHA256 和交付说明已生成；ZIP 293 文件且四入口/安装包/便携版逐文件校验通过。Windows 实机与真实堡垒机仍待现场。

# alpha.34 本机文件工作区已完成（2026-11-11）

本地终端启动后自动绑定本机文件来源，默认从用户主目录加载文件列表；列表行复用工作区文件图标，支持刷新、进入子目录、返回上级和本地路径边界校验。本机来源保留本地协议身份，不参与远程文件会话关联或认证；远程文件列表失败仍显示已有连接关联入口。新增 `LocalFilesUiTests` 两项，验证独立本地来源和真实本地终端集成。完整回归 241 项通过，截图 `avalonia/evidence/ui-0.3.0-alpha.34/local-terminal-file-workspace-alpha34.png`。

alpha.34 Windows x64 ZIP、安装包、便携版、symbols、SHA256 和交付说明已生成；ZIP 293 文件且四入口/安装包/便携版逐文件校验通过。Mac 原生窗口因系统锁屏未启动，Windows 实机与真实堡垒机仍待现场。

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

# alpha.15 已复现多行提示残留，候选网格/PTY修复验证中（2026-10-30）

独立zsh -f/自有ZDOTDIR与历史文件、正常MainWindow，大→小→大已复现：单行提示不重复，多行彩色长提示+右提示在缓冲区从1份变2～3份，非仅渲染；修前owned证据avalonia/evidence/alpha15-resize-failure-multiline。用户现场两图仅本地看、不转录/归档发布，未读其.zshrc/终端，也不称smaller图缩放成功。

alpha.15候选使用稳定物理网格，不主动重排活PTY输出，裁切单元保留且擦除不复活旧尾；固定原版本MIT源码XTerm.NET3.0.0(commit453ed957…)和控件4.1.2(commit0851f519…)，同API版本，补丁/原档SHA随vendor。另修已排版后PTY绑定/异步spawn末尺寸同步、隐藏/零面积不送临时尺寸，SSH不再最小2格偏离模型。saved cursor归一绝对行/重排锚点，模型用例覆盖。不是clear/输入命令/修改用户主题。

最新11项定向通过，含完整文字/宽字符恢复、全/部分擦除、saved cursor、真实zsh stty尺寸+UI输入+非回显随机输出、实际Launch/缩放字体、两个本地PTY隐藏/切页/输入隔离、同SSH文件保留的alternate frame/WindowChange/非回显ACK。另上游模型2311项通过、1项因fixture通配未拷贝失败；补原始fixture后仅重跑该Unicode全码位项通过，1项上游固有SKIP。最后补丁后上游/工程整体验证仍待，不说最终通过。

字体独立探测：旧栈实际Menlo缺Powerline，系统已有MesloLGS NF/JetBrainsMono/Hack Nerd；候选优先已有等宽Nerd，不安装字体、不读用户字体配置。新探测验证中。alpha.15未冻结三包，alpha.14与用户会话保留，不占前台/生产/真实秘密、不向其他聊天发消息。

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

# dev.9完整综合候选已冻结，待经理审后由用户验证（2026-10-29）

当前授权项已合并：大连接表单/密码策略与保存并连接/显式后台测试、真实分发Tab、同transport独立新页和引用计数、成功自动收连接导航、文件懒树/内部复制移动/安全权限计划与sudo、3秒可选同SSH exec监控/逐核环形/真实有界网络趋势、紧凑两行传输UI、Mac/Windows原生系统选择器与生命周期保护。具体功能与限制统一 `docs/NATIVE-DEV9-DELIVERY.md`、`native/REVIEW-NOTES-dev9.md`，不因新增漏原任务。

完整Mac `release/Forterm-Native-0.2.2-dev.9-test-macos-arm64.app`（正常cmd/forterm，非预览），可执行SHA `300d7407507b70d878917be18ca001c4614f072b6c6989885f4ff15e1dff6550`，arm64/adhoc核验通过，未启动。Windows四入口ZIP `release/Forterm-Native-0.2.2-dev.9-review-windows-x64.zip` SHA `f96c9f2340408d950aeb23226b929982cee457771bb7b77a20c32c3b164d6b25`，主PE `963290df835c1ca7c764f8a5bbf2a0824a34679d412fb44fc1ea2a65e4c96fbb`。459Go/C/ObjC源码及tar逐项一致，权限助手内嵌txt单独摘要，CRC/AMD64通过；检查点 `checkpoints/native-0.2.2-dev.9-review-20261028/`（沿用脚本目录后缀，实际本轮29日）。身份 `release/IDENTITY-Native-0.2.2-dev.9-test-macos-arm64.json`。

相关包最终race全部通过（desktop146.219s/sshconn32.492s及其余），vet干净。实际Fyne分发核心采用源码复制+仅测试overlay禁用可见Master TestMain，无native窗口，证明Tab09/ShiftTab/表单/编辑器/双目标、合成本地PTY/SSH；共享两真实PTY/原页关闭/认证1与拒shell保留通过。密码save/vault/显式连接和失败保留、跨SSH/FTP内容/移动/取消/旧源保护、逐核动态/计数/首样、树旧回复/绑定、资源排序/取消及权限只读规划等定向通过。软件深浅/小窗/150%/合成128核和25任务图见ui-0.2.2-dev.9/software-second。

边界：FTP同连接复制16MiB RAM、不自动另登；FTP安全覆盖不明确则拒绝，复制移动仅普通文件不递归；sudo需已安装安全Python API/root策略、仅明确随机sudo提示时临时stdin密码，未做生产提权；系统选择器只编译/回调模型，未真实打开或访问用户文件；新9 GUI未启动、Windows仅交叉编译。仍不称生产上传/原生panel/物理键盘/实际Windows高DPI已通过。

用户仍使用dev8，旧包/连接保留且SHA复核无变。正常实例IPC会复用旧进程，经理不得把旧8画面当9；换版需用户保存/退出，不能强关；如需并行可明确用独立IPC和空设置目录，不自动迁移真实配置/读真实凭据。程序员未占前台、不读生产秘密、不连接/上传/运行sudo，交经理审后操作。

---

# dev.9处理真实Mac反馈：连接表单/密码策略/显式测试/终端Tab（2026-10-29）

用户正使用完整Mac dev8，程序员不占屏、不关闭其连接、不读真实凭据。按新授权在dev9实现：放大快速/保存连接等同类弹窗并保证小窗可达；复现填写密码后再问（已确认未勾系统保存时会丢弃且成功提示错误，尚不认定为用户唯一根因）；加入只在用户点击时后台有界且可取消的连接测试；终端聚焦Tab交对应PTY，表单继续焦点遍历、代码编辑按现有缩进规则。

已确认vtwidget映射KeyTab但未实现Fyne.Tabbable，驱动在TypedKey之前因此遍历焦点；正在补真实driver分发层及合成shell/隔离回归。已有dev8源码/包和在用GUI保持。所有验证仅合成数据与回环服务，不生产连接或上传。

---

# 完整Mac功能版已构建，待经理启动给用户自行连接（2026-10-29）

用户明确要完整Mac版实际连接终端，本轮仅基于414条冻结dev8源码构建正常 `cmd/forterm`：`release/Forterm-Native-0.2.2-dev.8-test-macos-arm64.app`，arm64可执行SHA256 `c3aad24733444c1864c67bd9afaf98de8750c31bd349c47fc6b36b81a83ab339`，adhoc签名和架构通过。默认 `desktop.New("cn.forterm.native")`，正常SSH/SFTP/保存连接/系统vault与正常实例IPC；不是connection-preview或合成harness，只有明确--ui-check才进入离线响应模式。身份与入口核对 `release/IDENTITY-Native-0.2.2-dev.8-test-macos-arm64.json`；旧包保留，无源码改动、无新功能/无关测试。

程序员未启动、未读真实凭据、未连接服务器；交经理启动后由用户自行连接。用户已经解锁，不再沿用旧等待解锁状态。Windows `D:\Tools\Forterm-Native-dev8-test` 已建，ZIP仍未复制，未新Windows执行，继续由经理处理，不混入生产上传/命令/重登。

---

> dev.8经理独立复核已归档：`native/evidence/ui-0.2.2-dev.8/independent-manager/verification.json`。ZIP4bbb1f50… CRC/四AMD64入口/414冻结源码及tar、Mac预览fd112d04…一致；最小初始化diff已审，隔离冻结源码冷启动一致性定向race通过4.135s。仅无窗口软件驱动，不是dev8原生首帧/Windows通过。当前解锁请求仍未答，Windowsdev7-test仍空，未复制未运行；等待经理后续传包及具体首帧协调，不重复轮询/催问、不改包、不新增测试或占屏。任务尚未最终完成，收到新输入后继续。

# dev.8首绘初始化修复候选已冻结（2026-10-28）

离线复现并修正迟设主题导致首批字体测量不一致：旧85.359375px对当前87.515625px，短2.15625px；此前从未预热的新名称也复现。修后先选择主题再建Window/控件，初始化只设一次，隐式与明确字体测量一致。相关UI定向race22.332s、冷回归race3.444s及vet通过；不加宽列/补空格/切主题/强制重绘，不改系统字体和终端字宽。实际dev8原生首帧尚未运行，不宣称已复验通过。

新Windows四入口审查ZIP `release/Forterm-Native-0.2.2-dev.8-review-windows-x64.zip`，SHA `4bbb1f505d416243b753e21188014d246397a2f96ceeb8c4f0151fb9309eb438`，主PE `0a958b5c96432feccd37e21a127f6783ecccbdf1209292435324fd48f523803f`。Mac arm64离线预览 `native/evidence/ui-0.2.2-dev.8/native/Forterm-Connection-Review-dev8.app`，可执行SHA `fd112d047decf054943e76cc644e871f015332dd4b490337a6975c18b6db1d51`，签名通过但未启动。414源码/冻结tar/ZIP CRC与四AMD64摘要匹配，dev7原ZIP/预览摘要保持；身份及最小首帧复验方案 `docs/NATIVE-UI-REVIEW-DEV8.md`，检查点 `checkpoints/native-0.2.2-dev.8-review-20261028/`。

当前已完成的是经理dev7实际Mac UI检查，PID92827退出，7张原图已归档；首次深色末位裁剪仍是未验收项，dev8针对该项修复。Windowsdev7目标D:\Tools\Forterm-Native-dev7-test仍空，未成功复制/未新Windows执行；解锁请求已一次提出仍pending，程序员不触屏、不催问。上传仍按用户“以后再测”，不混入本次。旧原始记录不改，下文未回复/未启动等均是历史状态。

---

# dev.7真实Mac复核已完成；dev.8离线修首绘字体测量时序（2026-10-28）

经理已获用户许可并实际运行dev.7离线预览PID92827，已正常退出。深浅连接管理、静态四区、导航实拖约40逻辑px、700×433逻辑小窗固定更多/刷新、收起导航均有原图；7张截图及identity/result已校验摘要归档 `native/evidence/ui-0.2.2-dev.7/independent-native-mac/`。这仅是Mac实际UI与合成静态内容，不是Windows/生产上传或真实150%显示缩放；本轮分隔条键盘/重启持久化未实测，不扩大范围。下方“未回复/未启动”属于此前历史，不再是当前状态。

首次native-dark.jpg名称末位0/8确实有裁剪，切浅色/后续重排完整，外观还不能全部验收。已先看两张未改原图，名称列内空余很大；离线无窗口按原生runGL之前尚无字体缓存重置监听的生命周期建模，复现默认字体旧测量85.359375px保留，而当前PingFang SC需87.515625px，短2.15625px。明确代码顺序为dev7先建表格、后initChrome才SetAppearance，与该差异相符，不把其他GPU因素当已排除。

当前dev.8已将所选主题前置到newApp创建Window/首批控件之前，并让连接管理构造器保障同样顺序，initChrome只校验而不重复SetTheme。不加列宽、不补空格、不自动切主题、不改字体文件/终端字宽。新无窗口回归：同一首批0000/0004/0008/0012/0016的隐式测量与明确字体一致，主题仅设置一次；定向race通过5.166s。修前/修后原始JSON和日志在 `native/evidence/ui-0.2.2-dev.8/offline-font-probe/` 与 `dev8-before-runGL-font-probe.log`、`dev8-fixed-before-runGL-font-probe.log`，接续相关UI回归/新身份构建。尚未实际原生运行dev8，不能称首绘原生复验通过。dev7冻结包/源/原图均保护。

Windowsdev7复制已有用户授权；经理已建 `D:\Tools\Forterm-Native-dev7-test`，但文件粘贴不可用后Mac锁屏，当前目标目录仍空，未复制、未启动新WindowsEXE。解锁问题已一次提出、仍pending；程序员不重复催用户，不操作经理独占的远程UI。上传现场用户仍说稍后，本轮不混入生产上传/命令/重登或票据重放。

---

> dev.7预览独立复核已归档（2026-10-28）：经理确认中文sans软件图及离线/disabled-vault/静态终端范围，核对预览SHA1fd8b4c0…、source tar/manifest和413当前源码无差异。原记录 `native/evidence/ui-0.2.2-dev.7/independent-manager/review.json`，当前协调状态另存coordination-status.json（原JSON询问前状态未篡改）。经理已一次询问用户约3分钟UI-only前台复验，尚未回复，不执行；程序员不重复问、不同时占前台，预览/源/包身份保持，无重打或重测。仅整理NATIVE-REMAINING与复核交接；本轮UI不含上传，上传现场仍是用户明确的稍后测试，不能借此补生产操作。

# dev.7原生主工作台与字体/窄窗细节已实现，等协调实际原生复核（2026-10-28）

用户要求按当前XTerminal页面结构实际实现，本轮已完成Go/Fyne原生主工作台：280dp可折叠/拖宽分组树、协议筛选/搜索/常用操作、六列虚拟连接表格及行内连接/编辑/更多，堡垒机管理嵌入工作台，统一深浅中性主题/1px线/13dp正文，保留文件/终端/三向编辑/监控。1000行约130模板，不全量建6000单元，不读取凭据或自动探测。分组树Root branch识别修正、可访问名称/焦点、按下立即选中已验证。

经理只读查看dev.6软件图后指出中文宋体回退与小窗末尾操作发现性，dev.7已收敛：系统CJK sans主字体，当前Mac regular/bold实为PingFang SC，常用汉字直接覆盖；TTC仅内存提取校验，不打包/安装/下载系统字体。Windows配置YaHei/SimHei/JhengHei优先但未真实渲染，终端/编辑等宽资源和宽字栅格未改。右侧固定“更多”，窄窗可见并含刷新列表，软件点击验证通过。另修离线预览nil凭据服务问题，保留拒绝凭据服务、保存尝试明确拒绝不panic；普通应用凭据路径不受影响。dev.6保护为历史候选，后续预览用dev.7。

当前截图 `native/evidence/ui-0.2.2-dev.7/font-review-final/`，已打开核验深/浅1440、150%动作列、分组选中/hover及四区工作区；均为合成软件栅格，不冒充真实原生或Windows。最终race：字体/主题/工具栏/预览guard/工作区54.771s，行指针/guard9.635s，关联/停靠/控制器等19.333s；vet通过。一次1000连接库回调CPU27.609ms/9.966MB/70779alloc，Mac软件driver且不含初次字体装载，不当OS输入或Windows性能证明。

新审查ZIP `release/Forterm-Native-0.2.2-dev.7-review-windows-x64.zip` SHA256 `10cb5d779c029cc668c5889a9dc814628f19ec0c8a8758fdcc79b2465331a248`，主PE `bd1b10098570d76b88ac9a9cee25574bca94e15ce9dcb2f4498da392f465a9bd`。四AMD64入口/CRC/413源码与tar匹配，ZIP没有字体文件；dev.5/6原ZIP SHA均确认未改。检查点 `checkpoints/native-0.2.2-dev.7-review-20261028/` 含源码/差异/日志。

可运行但未启动的Mac arm64离线预览 `native/evidence/ui-0.2.2-dev.7/native/Forterm-Connection-Review-dev7.app`，可执行SHA `1fd8b4c0e027fb41d9b8f75c71c26eb5d7d8a22573e68d34e5369e2639329f0d`，adhoc签名通过。身份及源对应见同目录IDENTITY.json；字体族可在其运行快照核对。源manifest `d2a65dd4ab5f38bc8896c5ccf14c6559dc4edf2344872fd0a43ecbff230b8a12`，源码tar `61bcd37838806c20fbbb05e89ae4c8353387753791fdb9373017168270216959`。

用户在用电脑，未抢前台/重开原生/操作Windows App，未生产上传或登录。实际原生窗口截图与真实键鼠、Windows当前构建字体/按钮/高DPI/IME、未知原生上传现场阶段仍待。具体成果/源代码/截图索引及一次原生复核步骤 `docs/NATIVE-UI-REVIEW-DEV7.md`，由经理协调新的前台时间，不复用已完成的旧Mac上传确认；本轮不扩诊断框架、不重跑无变化全量、不重打旧包。

---

> 最新授权：按经理转交的XTerminal页面设计特征实际实现原生连接管理主工作台和统一视觉；另开新开发身份，冻结dev.5与旧包不改。dev.5经理独立复核已归档 `native/evidence/release-0.2.2-dev.5/independent-manager/verification.json`；控制器/关联/URI及隐私/关闭队列/短窗独立race通过，未知现场原因与Windows运行仍未确认。当前清单已重写 `docs/NATIVE-REMAINING.md`，清除dev2/dev3待审及picker待解锁的陈旧现状，旧清单保存在dev5检查点。用户暂缓的是上传现场验证，不是原生完善；先软件截图，不擅自占前台。

# dev.5续查完成两项确定修复；未知现场上传原因仍未结案（2026-10-28）

经理最新现场澄清：原生版已关闭，用户未给当时准确版本/错误文本；当前192.168.2.2为Electron0.1.7且有2.4MB上传成功项，不能反推原生是否运行dev.3或故障原因。相邻应用终端握手日志不属于SFTP，不分析或修改该应用。用户急用先保留有成功记录的旧版，不再盲目要求重装/重试。

本轮对比真正控制器路径与旧harness：新增串行软件Fyne驱动回环集成测试，实际launch.Parse --entry=xftp及App.Open连接，覆盖-newtab主窗口、-newwin独立来源窗口，再关联到另一终端工作区的真实上传按钮。取消不入队；中文/空格/%文件选择后的回调落在可见借用view，复用来源FileConn和Transfers；字节及目标一致。覆盖提示在可见owner，跳过原字节不变，确认后新字节一致；各认证1，计划临时密码清理且配置无保存连接。原harness主要直接NewWorkspace/SetConnection/UploadAt，缺少控制器/关联/独立窗口组合，本轮补足；不是实际Windows EXE或原生前台picker/IPC运行证明。

确定复现并修正（均保留失败日志）：
- 关闭借用文件视图时，未确认覆盖任务只等共享队列ctx，不等借用view.ctx，来源队列仍活造成旧确认一直等待。现在隐藏旧确认，安全跳过，不开启写入流、不关闭来源transport。失败 `upload-controller-route-before-fix.log`（其中Cancel按钮定位最初仅认英文导致的测试夹具失败已修为中英，不当运行时缺陷）。
- 隐藏队列展开时，外围file pane布局未重新保留footer高度，软件驱动测得allocated0、required229.078px，传输行被裁掉。显隐后刷新外层；新增/开始/失败项滚到可见区域，普通进度更新不抢滚动。失败 `upload-queue-layout-before-fix.log`；修后queue MinSize/分配回归通过。软件截图 overwrite-window.png 已打开核验：覆盖确认、展开队列和等待确认行均可见。

`native/evidence/performance-0.2.2/upload-dev5-final-race.log`：desktop10.696s/transfers1.496s/localfile1.358s/sshconn6.937s/ftpconn1.509s通过；包含新路径/关闭等待/小窗queue、阶段与隐私、picker/URI/SSH与FTP真实回环流/超时相关定向race。图片及两组result/stages JSON在 `upload-controller-route-dev5-final/`，明确softwareDriverOnly/syntheticLoopbackOnly，不替代Windows实测。

已冻结新审查ZIP `release/Forterm-Native-0.2.2-dev.5-review-windows-x64.zip`；SHA256 `a823930a95c8f57b797ab54ca205bb4ca320bf03d815c18a6486e0cb7a08d609`；主PE `1232dc1a477cdab3a2ffa72071738c4aca260b19a3d72f7d7031481b44f21bc9`。四AMD64入口、ZIP CRC/摘要及405条当前源码清单核对；检查点 `checkpoints/native-0.2.2-dev.5-review-20261028/` 含源码tar、清单、相对dev4差异和测试日志。相对dev4仅app.go版本、transfers.go运行时修复，新增upload_route_test.go；其余402条不变。dev.3三包/dev.4候选保持，不发新现场运行请求、不做生产上传。本候选未扩安装器/便携以免把未知现场问题当已解决；后续先独立审查再由经理协调必要的一次现场操作。

一次取得准确版本/失败阶段的最小现场方案：`docs/NATIVE-UPLOAD-FIELD-PLAN-DEV5.md`。不重复询问已答的上传现象，不复用旧票据；只在用户方便且愿意时一次正常上传并主动复制固定阶段JSON；无响应则如实保留，不能把缺事件当成功或0ms。Fyne回调前同步打开本地文件的限制仍保留，未凭猜测更换选择器。

---

# 现场上传仍未解决，dev.4仅为阶段可见性候选（2026-10-28）

最新现场再次反馈“上传选完文件仍不动”；经理仍在确认运行中的版本与提示，不能假定已运行dev.3，不以Mac回环或三包交付结案。本轮未占GUI、未生产上传、未重登或重放票据；已发布dev.3三包均保持。

静态审查UploadAt→Fyne storage.Reader→URI→reader.Close→EnqueueUploads→Queue.Add→worker Before STAT/覆盖确认→SSH/FTP文件通道/本地打开/远端打开/数据/关闭。确定的反馈缺陷：Queue.Add在队列关闭时返回空ID，但调用方仍显示成功，已修；STAT与覆盖确认以前共用含糊状态，且队列默认收起。未发现可直接证明现场原因的证据，不能猜服务器权限。

新0.2.2-dev.4：入队检查ID并立即展开队列；区分固定阶段（STAT、覆盖确认、文件通道、本地读取、远端打开、数据、关闭）。单文件选择流程最多64条内存事件，选择窗口关闭与回调分开，记录取消、URI成功、reader.Close和入队接受。Fyne在回调前同步storage.Reader的事实已确认；该调用如阻塞，会在回调之前，当前只增加可定位证据，没有据猜测更换选择器。更多菜单“复制上传阶段信息”仅复制版本、固定阶段/相对计时及最近20上传的状态，不含路径/文件名/错误文本/会话ID/凭据。

定向race通过：transfers1.318s、desktop4.160s、localfile1.195s、sshconn6.512s、ftpconn1.302s，覆盖阶段、关闭队列不虚报、隐私白名单/边界、软件picker取消/目标绑定、Windows URI、真实SSH/FTP回环流与超时。日志 `native/evidence/performance-0.2.2/upload-followup-dev4-race.log`。没有无变化全量重跑；dev.4未实际原生picker或Windows GUI验证。

审查ZIP `release/Forterm-Native-0.2.2-dev.4-review-windows-x64.zip`，SHA256 `35966a3be111bda9606e5ed9f455cec7daf069bd2901bc4a602e621849957594`；主PE `ef5d5ed21a4fdb3a253f43599e071b018720acc510c1e4004370a074c582397c`。AMD64四入口、CRC、包内摘要、冻结SOURCE-HASHES与当前源码一致；检查点 `checkpoints/native-0.2.2-dev.4-review-20261028/`，含源码tar/摘要/相对dev3差异和测试日志。未当新修复版要求用户盲目重装/复试；先依现场版本/状态决定最小下一步。未扩安装器/portable，以免把待定位候选冒充修复交付。

---

> dev.3经理独立复核已收到并归档 `native/evidence/release-0.2.2-dev.3/independent-manager/verification.json`：ZIP CRC/四AMD64入口摘要/400源码条目及tar一致；临时冻结副本desktop/localfile定向race通过，sshconn无匹配项不计测试通过。既有PID45327 Mac上传证明已核对。安装器和便携版已按同一冻结代码完成，身份见下节；生产与本构建完整Windows实测仍待。

# 0.2.2-dev.3 试用三包已就绪（2026-10-28）

按用户“现在要用、上传要好”的最新要求，提供完整应用试用版，不以诊断程序代替。普通启动进入完整软件；只有精确 --ui-check 才进入离线响应检查。保持原冻结 dev.3 业务 PE，未增加运行时改动。

- ZIP（四入口）：`release/Forterm-Native-0.2.2-dev.3-review-windows-x64.zip`，SHA256 `7a4e4fc9e44ebfc36a52d32c2602ff9703f30c2901b835b6b2ca74328f1abb6d`。
- 安装器：`release/Forterm-Native-0.2.2-dev.3-x64-setup.exe`，SHA256 `cdbdf85467fa96560b15a84871182b92c84c7ce85384875be27a71fed01f0951`。
- 便携版：`release/Forterm-Native-0.2.2-dev.3-x64-portable.exe`，SHA256 `924c32bceb4ecb96282f9bbf5a2d53d0ff285430fa3f241c96352f30201348d8`。
- 安装器归档 CRC 通过，解出81个应用文件逐字节匹配冻结目录；便携版嵌入原 ZIP 完整字节一次，四入口及便携 launcher AMD64。原 ZIP/旧版本未改。独立 Trial 安装目录、快捷方式及注册表项；配置与凭据仍按原应用规则保留，先退出旧进程再启用新版，避免 IPC 转交旧进程。
- 上传原生复验 PID45327 已闭合：REALPATH 拒绝明确提示、用户确认绝对目录后只读恢复、取消0队列、选中文/空格/%文件实际入队且远端字节与原目标一致、认证1。没有再重复占用前台或重跑无变化全量测试。当前用户的一分钟确认对应此项已完成复验，无需再次问。
- 验包记录 `release/VERIFICATION-Native-0.2.2-dev.3-trial.json`；使用说明 `release/USAGE-Native-0.2.2-dev.3-trial.md`；更新与现场清单 `release/RELEASE-NOTES-Native-0.2.2-dev.3-trial.md`。打包脚本 `native/packaging/installer-trial-022.nsi`，保留原0.2.1脚本。

限制：这是可用试用交付，尚非所有验收完成的正式版。真实生产目录/通道权限、此构建 Windows 实际点击/上传/高DPI、安装卸载未实机验证；此前小偏好页本地存图无效仍需补。不能宣称旧版Windows卡顿已完全解决，也不能保证服务端允许上传。后文“尚未三包”是历史状态，以本节为准。

---

> 用户现在需要正常软件且强调上传可用：确认dev.3普通启动为完整应用，只有精确 --ui-check 才进离线模式；上传业务代码与PID45327闭合复验一致，未发现新的已知正常上传恢复/队列/源绑定阻断。真实目录/服务器权限未知，不能保证生产端成功。按同一冻结业务PE立即补安装器和便携，独立试用安装目录/快捷方式/卸载项保留旧程序；ZIP原稿不改，无外观扩展、无变化全量不重跑。用户简短说明 `release/USAGE-Native-0.2.2-dev.3-trial.md` 已写，安装器/便携构建中。

# dev.3计时配对修订候选已冻结，供经理复审（2026-10-28）

- 新ZIP `release/Forterm-Native-0.2.2-dev.3-review-windows-x64.zip`；SHA `7a4e4fc9e44ebfc36a52d32c2602ff9703f30c2901b835b6b2ca74328f1abb6d`；主PE `e43bc9d67c87d361b37825f21588750362a3bf11a7e7e8e356552084fed9c91c`。
- 源码/清单/差异：`checkpoints/native-0.2.2-dev.3-review-20261028/`，SOURCE-DELTA.json/.diff。相对已审dev.2只改uiresponse/check.go和App版本字符串，新增button.go/pairing.go/pairing_test.go；其余395条冻结源码不变，总400条。应用业务逻辑/配置/上传/VT没有再改。
- 仅启用且无模态遮挡的测试按钮上，同阶段左键down/up才形成递增事件ID；pointer Tapped路径按FIFO消费，keyboard/直接调用等不能冒充配对。没有1秒或其他耗时上限，999/1000/1500/12000ms有效样本均保留数值；无法配对、阶段不符、手势取消、非单调顺序、阶段切换/关闭/容量等记unmatched/censored原因与计数，缺时延字段不填0。坐标仅进程内命中分类，不记录。
- marker只对未遮挡probe给下一Raster值；动作打开弹窗则明确censored，不能把操作者等待当绘制延迟。后续overlay/合并/阶段切换/关闭未观察也标censored。handlerCPU和native归属各自保留，报告不是popup可见或最终屏幕呈现时间。samples容量丢弃计数单列。
- `native/evidence/performance-0.2.2/dev3-pairing-targeted-race.log`定向race通过；没有无变化全量/重复上传/GUI/Windows运行。四AMD64入口/CRC/包内摘要/实际manifest文件及tar源码摘要一致。dev.2原ZIP SHA258d887c…保持，独立静态review记录已复制independent-dev2/verification.json；冻结diag1/diag2不改。

尚未获运行审查通过，不发新Windows运行请求；经理复审后再一次协调实际按钮样本。正式三包、正确新偏好页存图和最新高DPI四区组合仍未完成。原生picker PID45327归属/字节/认证1已闭合，不重复该上传。此修订不宣称旧0.2.1卡顿已修，也不把marker、队列或缺样本当最终显示延迟。

---

> 离线逐图复核补充：`virtual-library-native/library-last.jpg`确为0999末项，保留通过；`preferences-small.jpg`实际存成预览到期后的1000×732欢迎页，不能作小窗设置的可分享截图证据。同期CUA工具返回图曾显示700×458小窗偏好按钮可达，但本地存图未闭合；记录capture-assessment.json，正确截图仍列待补。已纠正审查索引，不把过期图当通过，不推断为运行时缺陷，也没有重开GUI/重复询问已完成时间确认。

> 经理独立审查入口已集中到 `docs/NATIVE-REVIEW-DEV2.md`：精确候选/源码身份，配置视图与认证一致性、缓存/虚拟库、上传恢复/URI、VT性能及手动检查模式的代码位置和证据索引。正式更新说明草稿为 `native/RELEASE-NOTES-0.2.2-draft.md`，明确未发行。冻结dev2候选/diag1/diag2和运行时本次未改，不重跑已通过上传或无变化全量。经理负责审查和新Windows运行协调；当前已完成的Mac时间确认不再重复询问。最终三包仍要实际Win反馈/高DPI与最终复核后才交。

# 原生picker上传复验已闭合，归属与字节/目标一致（2026-10-28）

用户允许约1分钟复验后，仅启动新唯一bundle cn.forterm.upload-proof2.20261028一次，等待exec输出UI_READY PID45327/绝对run目录，再按完整app路径绑定CUA，先核对AX标题“Forterm Upload QA · 45327”。实际确认绝对目录→取消第一原生文件选择→第二次选“中文 空格%20.txt”点打开→画面31B远端文件/1完成/成功页；进程正常结束并已归还控制。

`native/evidence/performance-0.2.2/picker-upload/run-45327/` 下 ready.json、upload-committed.json、result.json和cua-identity.json归属同一PID；两份成功结果完全相同。明确证明REALPATH拒绝可见、用户明确选择的绝对目录恢复、取消0队列、选中文件真正入队上传且远端字节一致、原目标 `/uploads/中文 空格%20.txt` 不变、认证1。正式此项通过，旧成功画面/超时矛盾run及discrepancy保留，不覆盖为通过。此前“等待确认超时”不得当应用失败或当前阻断。

本次是Mac合成服务/临时文件原生实操，不是生产上传或Windows实际上传。新0.2.2-dev.2审查ZIP与手动Windows响应模式仍待独立审查/获准验证；不改已冻结diag1/diag2，尚未正式安装器/便携/完整三包交付。剩余最新高DPI/四区与新认证路径组合、当前构建Windows实际按钮正样本、最终独立复核后才交正式版本。

---

# 0.2.2-dev.2审查候选已生成；尚非正式三包（2026-10-28）

审查ZIP `release/Forterm-Native-0.2.2-dev.2-review-windows-x64.zip`，SHA `258d887c098f2aba3530252551e91b151b90ba3e641186219b03ae813eddfb02`，主PE `62c41b96a3f329da477e4666487d2325f2ae28e8400ad6d5e6bfd0c880eaa148`。四入口AMD64、ZIP CRC、包内/构建摘要和实际SOURCE-HASHES文件摘要一致；检查点 `checkpoints/native-0.2.2-dev.2-review-20261028/native-source.tar.gz`。有BUILD-ID/REVIEW-NOTES/许可证/SHA，仅供独立审查，不应作为Windows卡顿已修复交付。旧版本/冻结diag1/diag2不变。

显式 `forterm-diagnose.exe --ui-check` 进入当前主程序离线手动响应检查（普通启动仍完整应用，不能混同）：0/1000合成配置、临时app ID、不读现有连接配置或OS凭据、不网络/本地终端，不经过正常实例IPC；一步一步人工执行后点下一项，不5秒自动跳过。静态枚举统计release到handler、handler CPU、返回到下个Raster，使用单调时钟，非最终显示时间。controller参数保护测试通过，仅诊断入口的精确单flag才转入；所有其他参数精确转交。尚未Windows执行，经理须独立审查并协调用户后再测，不自行占用远程UI或旧生产会话。

收到的真实Windows报告已按原始JSON核对，证据 `windows-field-report/`；没有持续queue/Swap阻塞但Forterm按钮样本0，不能称旧0.2.1已修；系统上报10.0.19045与先前Win11描述均保留。旧配置/按钮明确离线修复见下节：虚拟连接库1000项CPU回调1.4s→约7ms；值拷贝快照/缓存树；提交metadata显示视图不等凭据IO，后台一致LoginChain/TOTP读取及敏感字节清理，后台偏好/导入导出，短窗可滚动弹窗。最新全internal race/vet和dev2入口/目标测试通过，证据分别`latest-config-ui-full-race.log`、`latest-config-ui-vet.log`、`dev2-entry-ui-tests.log`（Mac，部分包合法Go缓存）。不代表Windows实际点击验证。

当前剩余：上传第一次Cua看见成功但控制记录不一致，正式仍未通过，已保留cua-record-discrepancy.json；待用户方便再用新唯一bundle/PID标题/绝对run目录/ready握手只启动一次短复验（已询问约1分钟，未擅自占屏）。连接库滚动末项与小窗设置实操图、两个实际callback20/22ms已保存。还要最新高DPI/四区组合、受控Win按钮正样本与独立复核，随后才冻结正式版本并出安装器/完整ZIP/便携三包。当前无正式installer/portable，不以诊断或审查ZIP代替目标。

---

# Windows报告续接与配置/按钮路径收敛（2026-10-28）

已读取并按assessment摘要核对三个原始Windows JSON，副本保存 `native/evidence/performance-0.2.2/windows-field-report/`。系统上报10.0.19045，与先前用户Win11描述不同，保留事实，不自行改口。新空配置开发版队列P95最大0.0731ms、SwapBuffers最大1.2124ms，但Forterm实际按钮样本0，不能称旧0.2.1卡顿已修，也不能把排队/Raster/Swap返回当最终DWM/RDP呈现。

明确离线发现与修复：旧连接库1000合成配置的回调约1399.9ms/466.8MiB分配，原实现为每条创建全部按钮；改虚拟可见行后约7.35ms/5.27MiB（Mac纯软件回调，2次样本，不是Win总点击时延）。配置Snapshot原JSON深拷贝1000项约3.18ms/1.34MiB/11555alloc，改值拷贝约0.055ms/0.246MiB/7alloc；新增连接树缓存，渲染行不再全配置Snapshot，搜索不污染用户分组折叠偏好。已加配置clone隔离/旧值语义、树缓存/搜索/行复用/小窗弹窗回归。

凭据与磁盘IO原共用metadata锁，后台保存时UI Snapshot也会等；新增仅供显示的已提交不可变ViewSnapshot，不等凭据/磁盘。认证改到后台LoginChain，在同一事务门内原子取当前realm+凭据并清理敏感临时字节，不能让旧realm配新密码；TOTP账户也用一致读取。慢synthetic Vault停顿时视图即返、登录等待提交后取一致新值，race/JSON不含敏感字段/显式空密码通过。偏好保存改后台IO。没有更换框架、禁线程安全检查、猜GPU或让用户重装。

用户已允许约3分钟本机合成GUI，实际CUA看到了取消→第二次选择中文/空格/%20文件→31B远端文件/1完成/成功页；随后看1000条连接库滚到末尾、小窗设置保存取消按钮可达，图片与callbacks20.08ms/21.91ms保存 `virtual-library-native/`。但上传控制器result.json却记录等待目录确认超时，与成功画面不一致，正式判定尚未通过；两份均保留在picker-upload，见cua-record-discrepancy.json。已向用户纠正先前过早的完整通过表述，不能把它当应用失败或完整验收。工具已改PID标题/唯一prefs/绝对证据路径，下一次须新bundle标识、只启动一次、ready后绑定；真实GUI控制已归还，没有继续自动占屏。当前不重复催用户。

正在准备当前主程序显式 --ui-check 离线手动模式：只接受单个精确flag、在实例IPC和正常controller解析前进入，其他CLI参数不会被吃掉；全新临时0/1000合成配置、无OS凭据/网络/本地终端。操作阶段由人点下一项而非5秒自动切换，统计固定action枚举和单调时钟的原生release→handler、handler CPU与返回→下一Raster（不是最终显示时刻）。尚未在Windows运行，须审查协调；冻结diag1/diag2不改。当前主程序三包未完成，实际Win按钮与正确归属的原生picker正向记录、高DPI新界面及最终独立复核仍要完成。

---

> diag2经理独立复核通过（2026-10-28）：ZIP CRC/AMD64/EXE、实际manifest文件SHA、源码tar SHA和380源码条目一致；与diag1仅main.go变化、report_test.go新增，其余378不变。取消/正常退出/完整可解码报告/四阶段及提交失败停止逻辑已读diff，独立冻结副本 `go test -race ./cmd/ui-diagnose` 通过1.550s。原始记录保存 `native/evidence/performance-0.2.2/independent-diag2/verification.json`。未执行Windows；后续同一待确认诊断动作使用diag2，不运行旧diag1，不重复询问。Mac仍锁屏、Windows新EXE运行授权仍待；没有更多已明确可离线推进项，不改冻结包/运行时或扩展测试。保留原生picker与新外观实操、Windows根因和最终应用三包未完成，收到解锁/授权/报告后继续同一任务。

# diag2最小取消修复包已独立冻结，原diag1保留（2026-10-28）

- 包：`release/Forterm-UI-Diagnostic-0.2.2-diag2-windows-x64.zip`（13.31MiB）；ZIP SHA `5df3725eb38b5d29bc1e6061c541cb093ac9887705bebbeefb5616f43e26f9be`；EXE SHA `a55903ef8493797c99caaa6e15d9a92acf6669d70b926059397789c5f2f117ae`。
- 外部校验：`release/SHA256SUMS-UI-Diagnostic-0.2.2-diag2.txt`；包内BUILD-ID/实际SOURCE-HASHES文件SHA/源码tar身份同步。
- 源码检查点：`checkpoints/ui-diagnostic-0.2.2-diag2-20261028/native-source.tar.gz`。以已审diag1冻结源码为底，仅改`cmd/ui-diagnose/main.go`并加`report_test.go`，其余378条源码摘要不变，总380条；没有混入当前应用后续性能/上传修改。SOURCE-DELTA.json在检查点。
- 只在“子进程正常退出+报告可解码+completed=true+四阶段齐全”时继续。报告缺失/损坏/取消/未完成/子进程异常都停止；最终报告临时写入/Sync/Close/同目录提交成功才标完成，失败不覆盖旧的未完成报告，不显示采样成功。汇总窗口明确取消/未完成。
- `diag2-final-targeted-test.log` 为最小冻结源码的定向race通过（完整/取消/损坏JSON/非零退出/缺失文件/最终写入与提交失败）；Windows x64交叉构建/PE/ZIP CRC/摘要核验通过，没有运行Windows，没有重做整套应用验证，采集范围与diag1相同。经理审查后更新待确认工具，不重复催用户。
- diag1仍是已审rev2 ZIP `52c2803f2cbb4b825ce6eb3bdf41ea50c18f69b6e03d616e91933d879d59b478`，没有改动。

现有history-long CPU样本只读定位：renderer.Refresh的行缓冲make累计约1.00s、ASCII builder扩容的WriteString累计约0.84s（总CPU样本12.84s，调用累计占比相互重叠）。不能用这些汇总解释758ms单峰，更不能推出Win根因。仅针对这两点复用每帧行scratch、复用未变化的不可变ASCII字符串、变化run一次定长分配；`renderer-allocation-targeted-race.log`通过，覆盖未变化0分配/变化最多1分配/字符串不别名可变scratch，以及既有13/14/16实际栅格/光标/宽格等。没有强开锁屏GUI或新增未经测量的延迟结论。

Mac手动解锁和Windows运行许可/报告仍待，原生选文件实操与Windows空窗根因、最终新应用三包尚未完成。当前不再扩展需求/更换框架/盲重跑。后续按既定待办恢复，不将诊断包当作修复版交付。

---

> 当前应用开发检查点已保存 `checkpoints/native-0.2.2-development-20261028/native-source-and-progress.tar.gz`，主开发PE SHA `b652755dbf7bc5fc0855a235ae77005d3dcdd278036751312e320e13b5f6b777`，不是交付版。最新完整internal race和vet通过；新真实SFTP REALPATH拒绝/不支持/超时恢复、明确直接读取、软件picker源/目录快照与Unicode/%路径、精确字宽/历史ring回归均通过。历史ring后的40秒P95为5.78ms，但最大758.56ms仍在，不声称完全无峰值；详见NATIVE-PERFORMANCE-0.2.2。
>
> 真实原生选文件/取消/上传步骤仍需Mac手动解锁，用户异步请求尚待回复；Windows空窗主诉仍需冻结diag1报告后定位。没有新正式三包，0.2.1保持；本阶段不再扩展需求，也不把无变化测试反复跑来代替现场。解锁后重跑cmd/upload-smoke（仅新合成回环和临时文件）：实际确认绝对目录→取消第一选择框验证0队列→第二次选择中文 空格%20.txt→核对远端字节/原来源路径/一次认证。经理独占Windows App，不同时操作它。

> 冻结diag1元数据修订2已由经理独立复核：ZIP `52c2803f2cbb4b825ce6eb3bdf41ea50c18f69b6e03d616e91933d879d59b478`；EXE仍`e7c7713f…`；实际manifest文件SHA和379源码条目、检查点身份匹配。独立记录已复制 `native/evidence/performance-0.2.2/independent-diag1/verified.json`。此diag1 EXE/ZIP不再改动，经理正在一次询问用户运行许可，Windows尚未执行。工作区另有未发布的diag2取消后不进入下一阶段判断及纯单元测试（不替代已审diag1，不打包/交付它）；应用开发和冻结诊断分别保留身份。

> 诊断包元数据修订2已冻结：同路径ZIP SHA为 `52c2803f2cbb4b825ce6eb3bdf41ea50c18f69b6e03d616e91933d879d59b478`，EXE仍为 `e7c7713f815195f97bca300b6754cb19270c4455df10bc363a2916f96f1e7878`（未重编译）。BUILD-ID.sourceManifestSHA256现在是包内SOURCE-HASHES.json实际文件字节SHA `10cc89d85991c63c397c0230e229838ff1ad8921e67140044ff37e3dba0fb75e`，原规范化entries摘要另存sourceEntriesCanonicalSHA256。包内/外部SHA与检查点BUILD-ID同步，原ZIP/元数据保存在检查点metadata-revision1/，源码tar未改。仅元数据修正，不当作新功能/重跑测试；Windows运行仍待一次具体授权。

> 本机Mac被锁定，真实CUA选择文件验收暂不能继续（2026-10-28，已向用户请求手动解锁）。合成picker probe PID9345已主动结束以避免把等待测试操作者的超时记为程序失败；记录 `native/evidence/performance-0.2.2/picker-upload/interaction-blocker.json`。没有操作Windows远程UI或生产上传。独立于锁屏的纯软件Fyne picker集成/race已通过：取消0队列；选择中文/空格/字面%20文件后真实回调进入队列、字节一致；对话框打开后改变路径仍绑定原来源与原目标。它不代替尚欠的原生CUA实操。后续仍要Windows离线报告/主诉定位、新三包与独立复验，不以诊断或当前本地测试结束。

> Windows一次主动离线诊断包已冻结供独立审查（2026-10-28）：`release/Forterm-UI-Diagnostic-0.2.2-diag1-windows-x64.zip`，13.31MiB，ZIP SHA `2967a969f4010cc45737f1b48caff1d903e9e51ad3829ab522406f1e6bffea99`，EXE SHA `e7c7713f815195f97bca300b6754cb19270c4455df10bc363a2916f96f1e7878`；`SHA256SUMS-UI-Diagnostic-0.2.2-diag1.txt`。源码检查点 `checkpoints/ui-diagnostic-0.2.2-diag1-20261028/native-source.tar.gz`，内含BUILD-ID/SOURCE-HASHES。一个开始按钮，两阶段自动继续，不替换0.2.1。报告主动落本地目录、最终窗口说明位置，不自动发送。标准/Forterm均空窗、隔离临时配置/禁用系统凭据，连接与本地进程入口阻断。真正GL查询、DPI/framebuffer、进程资源、UI队列、实际OnTapped和下一Raster反馈、原生SwapBuffers耗时分开；不把任何一个冒充最终屏幕呈现时刻。Mac实际按钮样本1次：release→OnTapped0.064ms、OnTapped→Raster0.942ms；四阶段SwapBuffers真实样本已录，属于Mac而非Windows。诊断工具在用户Windows尚未运行，经理将按规则取得一次具体确认后执行；程序员不控制远程UI。应用本轮修复/新三包仍未交，不以诊断结束任务。

# 0.2.2继续开发：Win11空窗卡顿/上传与原生界面（2026-10-28）

用户真实反馈为Win11 ZIP，未连接时任何操作均慢，全屏和小窗口均如此；新增上传选完文件后没动作/不动。最高优先是空主窗口，不以终端输出优化代替主诉。经理独占用户授权Windows App远程UI，仅只读/界面诊断，程序员不同时操作、不生产上传/重登/命令/审批。经理观察单时点等待链Forterm→dwm是线索，不能当根因；机器非内存耗尽，不能让用户结束DWM/禁安全。

原生运行时正在改（尚未交新三包，0.2.1三包保护）：VT默认逐格Text+Rect刷新改为ASCII同样式run/非ASCII固定格/背景分段，保留原始VT模型；cell宽统一使用精确字体advance（不再round），13/14/16长run/样式断点/宽格/指针/光标和实际彩色栅格新增测试。输出有界32KiB读取/约60Hz合并，保持字节顺序/背压；Fyne test driver原读边界不再等于渲染完成，已改字节完成barrier等待，未删VT断言。

Mac量测：最初约11969画布对象、低输出UI队列p95 383ms/突发912ms；仅run绘制后约249对象、突发p95约9ms且吞吐增加。真实profile已修defer顺序得到有效文件（原空profile保留），热点包括canvas.Refresh→EnsureMain→goroutineID/Stack；不能据此证明Win空窗慢已解决。固定相同82560字节与小块测试已保存，目标100×30/200×60的早期实际格数不同，JSON如实记录，正在收敛精确格数；不是降输出伪装提速。空主窗旧新Mac普通/全屏队列延迟均低，不能复制为Win结论。

Windows主动离线诊断 `cmd/ui-diagnose` 正在冻结审查：标准Fyne vs Forterm空窗，fresh临时配置/禁用OS凭据、禁止连接/本地进程；一个开始按钮，两阶段自动继续；真实GL vendor/renderer/version、framebuffer/monitor/DPI、进程CPU/内存、UI队列延迟、实际OnTapped计数/下一Raster反馈。新增GLFW本地小fork仅可选SwapBuffers耗时observer（默认nil原调用不变），测真实native SwapBuffers等待，不把队列延迟/Raster生成冒充显示器呈现时间。不读终端/剪贴板/账号/环境dump，不联网。尚未冻结ZIP，不运行用户Windows新EXE，经理将在需要时取得一次具体确认。

上传处理中：REALPATH/列表错误按步骤明确显示，不再吞为统一文案；只有明确的SSH_FX_OP_UNSUPPORTED且用户输入安全绝对目录、LSTAT证实目录时尝试只读LIST，并显示说明，禁止未知相对目录或权限/超时静默fallback。上传前要求当前输入目录已成功读取；Windows URI /C:/、中文/空格/字面%20路径只解码一次；失败计数/首次失败展开队列；传输更新合并/隐藏列表不重绘。合成目录失败/超时/Win路径和VT对齐新定向正在完成，生产拒绝根因仍未知。

界面沿XTerminal参考做中性近黑/细分隔/克制绿色/小行高、清晰空工作台和主按钮，保留三列/关联/原能力；去工作区重复标题与同一Status控件被两个父容器复用，编辑标签只在标题变化时全刷新。尚待真实组合截图、Windows诊断数据、最终三包/经理复验，不以本组结束。

---

> 0.2.1经理独立验包与真实CUA复验通过（2026-10-27）：132项ZIP/安装器、四AMD64入口、portable精确ZIP、241源码摘要均匹配；实际1050×650@150%多文件/展开25队列/末项详情/粘贴撤销/放大1450×950终端真实执行/缩回均通过。原始证据已保存在 `native/evidence/independent-0.2.1/`，详见 `docs/INDEPENDENT-VERIFICATION-Native-0.2.1.md`。仅追加验证记录，运行时和三包SHA不变；Windows实机与完整IME仍未验。无新增确定本地缺陷，后续等待现场/接口资料/新反馈。

> 0.2.1最终交付身份已冻结：安装器132文件逐字节匹配、最终ZIP/便携/安装包SHA已更新，源码恢复点 `checkpoints/native-0.2.1-final-20261027/native-source-and-docs.tar.gz`。旧0.1.7与0.2.0共六包SHA重核未变。后续依据现场反馈处理，不为填时间扩大需求。

> 输入补测已更新（2026-10-27）：独立原生对照窗口中，标准Fyne Entry的a/b/c后空格实际提交ABC，x/y/z后回车实际提交xyz；自绘代码编辑器d/e/f后空格提交“得分”，x/y/z后回车提交xyz。证据 `native/evidence/input-probe-0.2.1/result.json`、`committed-state.json`、`committed-standard-and-code.jpg`。此前普通字母未立即显示属于输入法组合等待，不能继续表述为字符丢失。候选/预编辑显示和提交回车顺序尚未完整验证，Windows键盘/IME仍未实机运行。此补测未改生产运行时或最终主PE。

# 原生0.2.1小高度修复三包已生成，明确测试范围（2026-10-27）

0.2.0因经理实际1050×650@150%列表/代码不可达和队列覆盖而撤回推荐，旧三包保留。0.2.1修复此问题，实际CUA/末文件/末队列详情/代码导航粘贴撤销/来源保持/常规尺寸恢复通过，完整internal race与常规浅色GUI通过。最终主PE：`3c7ae45123c07c33d17ee38ddaeee0bf76389c7273f4eecc508f1668132d2b9b`，不是旧候选。

交付：`release/Forterm-Native-0.2.1-x64-setup.exe`（11.8MiB）、`Forterm-Native-0.2.1-windows-x64.zip`（16.76MiB，四入口）、`Forterm-Native-0.2.1-x64-portable.exe`（19.18MiB）；`SHA256SUMS-Native-0.2.1.txt` 与 `VERIFICATION-Native-0.2.1.json`。NSIS无警告、7zz测试通过、实际132个应用文件全部匹配准备目录与最终主PE，便携精确ZIP。旧0.2.0三包SHA重新核对未变。

新证据与真实范围：`docs/NATIVE-FINAL-VALIDATION-0.2.1.md`。实际小窗口CUA图确为1050×650物理像素，常规实际拖大1453×954再缩回保持草稿/源/队列；不是780×620逻辑@150%替代。原失败图保留。

字符输入补测现已确认实际提交：标准Entry得到ABCxyz，代码编辑器得到“得分\nxyz”，此前普通字母等待输入法提交，不能写成丢失。未改运行时；候选/预编辑与提交回车顺序未完整验收，Windows键盘/IME仍待实机。见input-probe-0.2.1的JSON/状态/原图。

---

# 0.2.0被独立小窗口验收阻断，0.2.1修复验证中（2026-10-27）

经理实际1050×650物理窗口/150%发现文件行/代码正文高度不可用、展开25队列向上覆盖工具栏。0.2.0三包静态身份正确但存在此UI缺陷，已明确告知用户先不要使用，旧包保留不覆盖。

已改文件区为有明确滚动条的列（列表最少100dp）、队列150dp独立内部滚动、短窗口分配编辑正文高度并用文档滚动容器保留工具/代码/状态，禁止负尺寸压缩。当前0.2.1真实CUA完成：1050×650截图核对；切来源A/27文件、滚到末文件index26并实际选择；展开25队列、滚到batch-24/实际打开详情，控件不向上覆盖；代码Down/End定位、实际粘贴及工具栏撤销；切来源B再A保留状态；实际拖大为1453×954，源/草稿/选择/展开队列保持。证据 `native/evidence/small-0.2.1/actual-cua-result.json` 和同目录CUA原图jpg/live状态。定向/race通过，最终全internal race与常规浅色组合、最后三包仍在继续，当前不要发0.2.0旧候选。

Windows运行仍未验；不会把150%逻辑780×620误当1050×650物理。唯一代码编辑者继续完成此修复交付，不扩展需求。

---

# 原生0.2.0最终本地三包已生成（2026-10-27 19:20后，待Windows实机）

已包含用户最新“终端/文件同时显示、只隐藏所选文件协议顶部标签”修订，以及编辑器左/右/底部停靠、导航拖动/键盘/重启宽度。最终主PE SHA256：`29a4bcbc425fdd5bcd204ba065c0982e70ada7eeac5e207b38e0a12d9f30047e`（19:18交叉构建）；不使用checkpoints中的早期候选。源码没有后续运行时改动。

- 安装包 `release/Forterm-Native-0.2.0-x64-setup.exe`，约11.38MiB，NSIS构建无警告。
- 完整四入口 `release/Forterm-Native-0.2.0-windows-x64.zip`，约16.10MiB，CRC/每个PE摘要与目录一致。
- 单EXE便携版 `release/Forterm-Native-0.2.0-x64-portable.exe`，约18.53MiB；PE x64、内嵌完整ZIP逐字节一致。
- SHA与产物身份 `release/SHA256SUMS-Native-0.2.0.txt`、`release/VERIFICATION-Native-0.2.0.json`；本轮最终证据索引 `docs/NATIVE-FINAL-VALIDATION-0.2.0.md`。

最新desktop/workspace定向与race通过；完整internal race在前一最终布局前通过，后续仅改desktop布局与关联，相关包已定向/race覆盖。最新真实GUI `native/evidence/file-session-20261027-rerun/native-gui-smoke.json` passed：精确shell143字节、两非回显执行标记、ANSI、同transport四能力/认证1、25队列、保存确认/dirty取消、监控隐藏暂停、来源B认证1/同名草稿/顶部隐藏。新截图已实际查看。第一次本轮GUI的迟到shell焦点断言失败；日志保留，脚本当时未初始化文件区可见这一前提，现明确初始化filesVisible=true后完整重跑通过。失败时未采集面板/焦点对象快照，不能断言首选就是唯一原因；未删断言或改运行时焦点规则；文件区隐藏后关联恢复由新增定向测试覆盖。

安装器已用7zz实际完整性测试通过，并解出112个应用文件与准备目录逐字节比对一致（包括最终主PE/四入口）；便携内嵌ZIP也已逐字节核对。可恢复源码/文档检查点为 `checkpoints/native-0.2.0-final-20261027-1923/native-source-and-docs.tar.gz`。

旧0.1.7三包SHA保持。WindowsConPTY/SID管道/IME/DPI/系统凭据/实际安装卸载及生产堡垒机仍未运行，不称Windows全部测试通过。厂商门户仍待脱敏契约。没有推送、上传发布、生产网络/历史票据操作。

---

# 用户确认文件会话与终端同时显示（2026-10-27）

用户最新明确：所选文件协议会话在当前工作区与终端同时显示，只隐藏其对应顶部标签。保留显式关联、连接与传输/草稿；解除最后引用恢复顶部标签，不关闭来源、不按IP合并、不重新认证。已补齐“文件区先前收起→选择来源自动展开”的交互，并保留当前终端对象与选中工作区。`native/evidence/file-session-association-20261027.log` desktop/workspace定向通过，涵盖收起后关联、并排、顶部去重、解除恢复和连接不关闭。

此前左/右/底部代码停靠、窄窗保留首选、导航拖动/即时键盘/隐藏恢复/重启宽度已通过定向与实际窗口验证；证据在 `native/evidence/release-0.2.0/final-dock-dpi150/` 与 `native/evidence/layout-native/nav-actual-result.json`。早期0.2.0候选已移入checkpoints保护；最终三包仍需在本轮最新源码上重新生成核验，Windows实际运行未验。

---

# 原生持久化与认证组接入并验证（2026-10-27，继续迁移）

- 已接连接库/分组树、非秘密导入导出、系统凭据、账户/TOTP、偏好和持久化主机指纹；CLI URL标记temporary，连接库API拒绝临时票据保存，导入重新分配ID不复用原凭据；配置/系统凭据补偿回滚有新Go测试。
- `evidence/system-vault-smoke.json` Mac真实新合成钥匙串Set/Get/Delete通过（未读现有项目）；`library-system-vault-gui.json` 合成保存profile重载配置/指纹+从OS凭据明确连接真实回环SSH一次认证通过，配置/导出无秘密。Windows凭据管理器仅交叉编译，未运行。
- TOTP旧规则严格回归与账户canonical-key保存回读码/race通过；经理隔离11例复验也已通过同一源码快照，不再无变化重复该组；TOTP实际显隐/复制/窗口生命周期仍待组合。
- 加密私钥与原生跳板链逐跳指纹真实回环/race通过，现有agent socket平台接口与可设置心跳/已开SFTP只读探测已写，定向真实fixture测试正在执行。
- 偏好已接主题/字号/心跳；编辑区底部/右侧可聚焦自绘分隔、独立比例/拖动/方向键/首选持久化已接入，可窄窗暂用底部。全局快捷键按旧Ctrl/Cmd+Shift约定，不抢终端常用按键，modal打开时不触发。
- 经理对review2深色ANSI和次级文字对比度反馈已修正：默认16色复用旧版可读深浅色板，渲染时保留应用真彩/OSC颜色，不重建连接；需补实际色板截图。分隔即时键盘问题定位为DoubleTappable单击等待导致即时按键先送旧焦点，已改pointer-down捕获；定向回归通过，实际键盘位移仍待验证。

当前视觉基线保持用户已认可的0.0.2-dev.2，不重新设计。尚须双终端、完整剪贴板/账户生命周期、窄窗/DPI/持久化/真实交互组合及发布产物复核。Windows运行环境仍未解决，不把交叉构建当运行通过；最终安装包/便携/四入口ZIP未交付。继续执行NATIVE-REMAINING，不以本组结束。

---

# 原生连接库/系统凭据/TOTP与认证继续完善（2026-10-27）

已沿获用户认可的0.0.2-dev.2界面继续接入settings非秘密配置、连接分组树/库CRUD、账户/TOTP、偏好与主机指纹持久化。Mac实际新合成OS钥匙串Set/Get/Delete通过（修正底层SDK未找到返回nil,nil的语义），合成保存profile重载配置/指纹后从系统凭据明确连接真实SSH认证一次通过；秘密不进JSON或导出，临时票据拒绝保存与失败回滚有测试。Windows系统凭据仅交叉构建，未运行。

TOTP已按真实0.1.7严格规则补齐5–300周期、URI独立默认、重复/未知/userinfo/fragment/空label、规范Base32填充/尾位与输入界限；RFC6238三算法、前导零、周期边界/账户canonical-key回读码、race通过。早期不兼容未进入最终包，详见NATIVE-TOTP-PARITY。

加密私钥与原生SSH跳板链/每跳指纹真实回环和race通过。agent平台入口已写，fixture尚待；可设置心跳/已开SFTP探测已接入代码和偏好，完整禁用/超时组合尚待。编辑区已换可聚焦拖动分隔与独立底部/右侧首选，快捷键按旧Ctrl/Cmd+Shift约定接入，尚需实际组合/高DPI验证。

默认ANSI16色已改为旧版验证过的深浅可读色板（仅默认索引，应用真彩/OSC保持），次级文字提亮；实际深浅色板及键盘分隔图待补。完整后续收敛见 `docs/NATIVE-REMAINING.md`，不因当前组/截图结束任务；最终Windows安装/便携/ZIP仍未交。

---

# 原生整体界面重做第一轮已形成（2026-10-27 17:47，0.0.2-dev.2）

按用户“页面太丑、继续”的新优先级重做真实原生UI：默认炭灰深色/分层面板、同步浅色、13dp文字与统一图标/细分隔、紧凑顶栏；加入最左连接/会话导航与明显收起/固定恢复入口。文件区改为单行上传/下载/新目录/刷新/更多，路径旁筛选/排序，文件图标/大小/选中行；次要操作保留在更多和右键，移除14个大按钮格与重复状态堆叠。

中央终端/原生代码编辑占主体，代码保存/撤销/停靠/空白与换行放紧凑工具栏/菜单，源/dirty在标签中、编码/行列在底栏，当前行高亮。监控改为CPU真实双采样差值、内存/磁盘轻量细条、网络双采样速率与独立负载；缺采样显示等待，不把负载当CPU；原始/proc移到展开详情。测试服务仍是明确合成本机连接，截图不冒充生产指标。

已实际运行/检查深浅完整窗口，二进制与图片摘要在 `native/evidence/screenshots/redesign-20261027/review2/capture-metadata.json`：深图 `dark-cua.png` 17:46:25，浅图 `light-cua.png` 17:47:32，版本0.0.2-dev.2；与16:58旧白色图独立保存。CUA实际点击来源A/B、文件更多菜单、主题按钮，实际拖动文件分隔条+30dp均有可见结果。键盘分隔条尝试后位移未获视觉确认，尚不宣称通过；需进一步修复/验证。

同版本真实回环仍通过非回显终端标记/精确输入/真实ANSI、代码保存确认与取消、dirty关闭取消、25项串行追加、隐藏监控暂停/恢复、15秒心跳无注入、两个来源同名草稿/隐藏顶部重复SFTP标签。新指标解析测试与workspace/codeedit定向通过。当前截图只完成外观第一轮，不是全功能迁移或最终Windows安装包。

接下来继续多来源共享/关闭与目录状态组合验收（已修复忙时路径请求被旧结果覆盖的缺口）、分隔键盘/窄窗高缩放、保存连接/安全凭据/TOTP/指纹/心跳设置与剩余布局持久化；最终Windows NSIS/便携/四入口ZIP、SHA和经理复核仍未完成，不以阶段截图收尾。旧0.1.7及原型0.0.1包保持。

---

# 原生FTP/文件操作组已接入，继续工作区与持久化（2026-10-27）

新增 `remotefs` 统一接口和真正的 `ftpconn`（jlaffaye/ftp 0.2.4 + 本地元数据/安全Command修订），不把FTP转SSH，不自动重登。FTP大文件上传下载、队列、目录浏览/新建/改名/删除已通过真实本机FTP回环及实际Fyne窗口 `evidence/ftp-ui/result.json` / `ftp-gui.log` / `native-ftp-window.png`，认证1且无SSH transport。FTP metadata超时关闭失效控制连接，后续不复用临时密码。控制路径与认证CR/LF/NUL拒绝。FTP仍不提供安全在线文本编辑（与0.1.7边界一致）。

新增 `fileops`：绝对安全路径、父路径链接检查、删除先完整规划≤2000项/32层/120秒、FTP链接树拒绝/不跟随目标、逐项类型检查、并发非空目录RMD失败停止、不递归删除新增子项；重命名不覆盖同名、普通权限保持特殊位且期望值冲突检测/应用后核对。真实FTP临时目录+Unix特殊位/链接/非空并发/race通过。原生文件区已接入新建目录、重命名、删除、权限、复制路径、右键菜单和文件双击；最终完整工作区/键盘/DPI交互仍待。

严格终端新结果为 `evidence/strict`（输入143字节、预期/实收SHA一致、非回显随机执行标记、真实PTY ANSI、234绿色像素、焦点交互第二命令）；旧 `evidence/next` 不再证明shell执行，问题副本保留。连接前键盘无重放、延迟250ms shell就绪保留文件输入框焦点的新回归与严格GUI通过。源码已把全量终端粘贴预览替换为虚拟原生只读代码视图，不再截4096字；还需专项确认/取消/焦点测试。

当前UI仍为开发布局，不能以这些阶段截图当“好看/完善”已验收。下一项是0.1.7多来源三列工作区、明确导航收起/固定恢复、隐藏已关联文件顶栏、来源独立状态/草稿/共享生命周期，继而完整连接库/凭据/TOTP/指纹/心跳设置和统一深浅主题/高DPI。最终Windows安装包/便携/ZIP与经理复核未完成，持续推进，不结束任务。旧0.1.7和原型0.0.1包未动。

---

# 原生队列/严格终端验证继续推进（2026-10-27，完整迁移未交付）

流式普通文件传输已去除原型1MiB限制（文本编辑仍限制1MiB），32KiB缓冲、进度、私有临时下载、独占/确认覆盖提交；Windows用无REPLACE的MoveFileEx支持非NTFS独占提交。每连接队列实现FIFO、20+批次/追加、进度、排队取消、活动取消停止其余排队、清理；故障/取消不自动重传。元数据10秒与数据停滞30秒看门狗、取消后的SFTP重置确认，保持SSH/exec。大于1MiB真实流式回环、延迟写/元数据超时、取消下载保留本地目标/清理临时文件、队列25项串行追加及race通过。真实GUI25项（含4MiB）队列已通过，批量目录勾选与拖入入口已实现但完整交互/最终高DPI仍待。

经理指出旧 `evidence/next` 图含5printf失败，而contains标记检查可匹配回显；已保存到 `evidence/failures/terminal-false-positive-20261027`，撤回旧passed对“shell执行成功”的证明，不将其他独立队列/编辑/监控断言一并误判。历史5的具体来源无原始输入记录，未断言已确定；确实发现并修复了新VT组件连接前键盘会排队重放的缺口，以及延迟shell就绪可能夺取输入焦点。

新的 `evidence/strict/native-gui-smoke.json` / `strict-terminal-gui.log` / `strict/native-live-window.png` 已严格通过并查看：非回显随机执行标记、服务端精确143字节命令及匹配SHA、PTY真实ANSI字节、终端区域234个绿色像素；文件/编辑/监控焦点交互后第二非回显标记执行，输入字节只含两条预期命令；延迟250ms的shell就绪保留文件输入框焦点；编辑字符不入终端。认证1/SFTP1、25项流式队列、保存取消/确认、dirty关闭取消、隐藏监控/恢复/15秒无注入仍通过。四能力后端测试也改为检查真正shell输出的随机ANSI标记，而不只丢弃输出。

当前新增独立Go FTP实现与统一remotefs接口正在开发，尚未联入最终多来源工作区或通过FTP回环；保留协议区分、不自动重新登录临时票据。0.0.1和Electron旧包不动。继续FTP/文件操作、工作区和持久化/统一主题，未完成全功能或Windows安装包，不能以本组通过收尾。

---

# 原生四能力核心组通过，继续完整迁移（2026-10-27，0.0.2-dev.1）

原生终端已切换到 `internal/vtwidget` + 本地修订 x/vt 0.1.0，保留完整 grapheme cell、10,000 行历史、前后/不区分大小写的历史搜索、VT 按键/鼠标协议与原生画布。原 Fyne Terminal 分支仅作为0.0.1历史验证保留。组合符号、跨write的ZWJ/旗帜/肤色、宽字符边界、ANSI、备用屏幕、选择/复制、paste和resize等新探针与 race 检查通过。已修正候选上游的跨 write grapheme 与宽格换行，以及彩色背景续格继承问题。

新增 `internal/codeedit` 原生代码编辑器（BaseWidget自绘，非TextArea）：行号、Chroma高亮、缩进线、原生选择/拖动、剪贴板、UTF-8/BOM/LF/CRLF、尾空格、混合换行只读/明确转换、Unicode grapheme删除与有界撤销。后台 `sshconn/text.go` 15秒边界、普通文件/UTF-8/1MiB、两次冲突检查、同目录临时原子替换、首存排他创建；为确保初始0600新增本地pkg/sftp.OpenFileWithMode（发送OPEN属性，不依赖事后chmod）。两次冲突、0600 wire属性、边界、原字节和保存前后撤销的回环/race通过。

真实Fyne窗口综合 `evidence/editor-confirm-gui.log` 与 `evidence/next/native-gui-smoke.json` 全部通过：终端+SFTP+代码编辑+exec监控同窗，认证1/SFTP1；保存取消不改远程、真实确认按钮保存成功、dirty关闭取消保留工作区；监控手动/用户自动、隐藏10.3秒无新增、恢复继续，15秒心跳无shell输入注入。截图已查看，代码行号/高亮/dirty和四区真实存在；当前外观仍是开发布局，尚未完成用户要求的统一主题/三列多来源/高DPI验收。

`evidence/four-capabilities-race.log` 独立验证真实shell输入、流式API前的上传、编辑保存、exec并发，一次SSH认证/一次SFTP。新代码亦已交叉构建为 `native/evidence/forterm-native-next-windows.exe`（开发快照，非最终包）；修正ConPTY初始尺寸int16签名。没有Windows运行证明。本机有VMware Fusion但running VMs=0且vmInventory为空，未找到已配置可用Windows VM；不连接Windows App中的远程账户，不以缺Windows现场而停下本地迁移。

接下来实际推进原生FTP/SFTP流式大文件和每连接传输队列、文件操作/元数据超时与恢复，再完成0.1.7工作区/持久化与统一深浅主题。保存/关闭等UI还需在完整工作区中继续组合回归。全功能对等与最终Windows安装包仍未完成，不把当前组通过作为最终答复停止。

---

# 原生完整迁移继续开发（2026-10-27，0.0.2-dev.1）

用户已明确要求继续完善原生版、保留原先全部功能、测试后交付 Windows 安装包，并追加“好看、完善些”。当前任务是完整迁移，0.0.1 ZIP 仅验证阶段，不是本次最终交付。唯一源码编辑者仍为本聊天，不发其他聊天消息，不开并行编辑者。

已进入下一组实际实现：同 SSH 独立 exec 资源监控后端/面板，8 秒超时、64KiB 上限、防重叠、手动/用户启用10秒自动刷新、隐藏暂停与恢复；回环并行/拒绝/超时与共享 transport 的 race 测试通过，GUI综合验证正在进行。未将双/三能力冒充四能力。

终端正在从单 rune 网格迁至原生 grapheme VT 核心（charmbracelet/x/vt 0.1.0，保留本地修订与许可），增加原生画布与有界滚动历史、搜索、按键/鼠标协议。新探针已通过连续与跨 write 的组合符号、ZWJ emoji、旗帜/肤色、双列边界；候选上游的跨 write 合并与宽格延迟换行缺陷已修正。新画布原0.0.1 VT能力定向检查通过，但实际GUI仍在核验，不能把开发中源当最终包。

下一步仍须安全原生代码编辑器（不是 TextArea 替身）、四能力与权限/隐藏/超时测试，随后真实 FTP/SFTP 流式大文件与队列/文件操作、0.1.7 三列多来源工作区/导航收起/顶部标签规则、连接库和安全凭据/主机指纹/可设心跳等。最后原生 Windows ZIP、安装包、便携版与经理复核。Windows运行环境暂无，继续完成可在Mac原生/回环验证的部分，不以此等待停工。

美观验收新增：统一深浅主题/中文字体回退/图标/间距和焦点状态，清楚的来源、活动终端、连接与dirty；窄窗口/高缩放不能裁关键操作。形成真实工作区后检查深浅主题、四区、窄窗口/DPI截图及实际点击/拖动/键盘，不能用设计图替代。生产厂商 API 仍缺脱敏契约，是旧版也存在的独立缺口，不阻塞原生已授权功能。

---

# 原生迁移第一阶段已交付（2026-10-27 14:31，整体迁移仍未完成）

已完成独立 Go/Fyne 原生验证版 `0.0.1-native-prototype`。**这是实际原生程序，无 Chromium/Electron/Node/WebView；不是完整替代 0.1.7，后续应继续原生功能迁移，不能因旧标签任务已交付而回退旧任务。** Fyne 是自行绘制界面，不等同 Windows 系统原生控件。

## 本轮实交付

- `release/Forterm-Native-0.0.1-windows-x64.zip`：14,826,959 字节（14.14 MiB），四个 PE x64 入口；主程序 forterm-native.exe 26,228,736 字节（25.01 MiB）。完整独立目录，不覆盖旧 Electron 版。
- `release/SHA256SUMS-Native-0.0.1.txt`，ZIP SHA256 `6ffdf7606be7f4baa88700c135ce1ab8c32cef97196e1d1fa2124303887b6c28`；`VERIFICATION-Native-0.0.1.json` 包含入口摘要、尺寸与 DLL 导入，ZIP CRC 通过。
- `release/Forterm-Native-0.0.1-macos-arm64.app`：Mac 原生包，已实际打开脱敏诊断窗口并截图检查，正常关闭；无网络请求。
- `native/README.md` / `MIGRATION.md` / `VALIDATION.md` / `WINDOWS-ACCEPTANCE.md`；同版 Windows ZIP 内包含这些说明、许可证和关键测试证据。
- `release/NATIVE-TEST-EVIDENCE-0.0.1.json`：最终源摘要、GUI/终端结果、构建事实和未完成项；`checkpoints/forterm-native-0.0.1-source-20261027-143118.tar.gz` 为原生源码恢复点。

## 实测与修复

- 原生实际 Fyne 窗口、真正 VT 组件与回环 SSH shell/PTTY，不是普通文本框。SFTP 与 shell 一次认证共用 transport；上传下载字节一致、三个 PTY 尺寸事件、15 秒 SSH 心跳一次且交互输入字节零新增。原生窗口截图 `native/evidence/native-live-window.png` 已检查。
- 中文连续双列、DSR 列数、跨 SSH 数据包 UTF-8、半格覆盖与右边界换行、备用屏幕、ANSI 色块、选择复制、Ctrl-C/方向键、paste hook/bracketed paste、resize 均有定向通过证据。修复了原始中文丢字、UTF-8 分包和关闭管道无限错误循环，失败压缩日志保留。
- 11 独立进程精确参数 IPC、内存 HMAC 去重、显式空密码与未提供密码区分；不记录原始参数或票据。独立测试这些层，不宣称已用 Windows 控制器完成 11 进程现场验收。
- shell 与 SFTP 两向权限拒绝隔离；上传超过 1MiB 在创建远程文件前拒绝，下载先临时文件后替换、目标存在需确认。launch/instance/sshconn 的 race 检查通过；不扩称 GUI 全部无竞争。
- Windows ConPTY 入口交叉构建通过；修正上游等待失败 log.Fatal 导致整应用退出、使用 SystemRoot 及幂等关闭。但没有在 Windows 运行，ConPTY/输入法/剪贴板/DPI 仍待现场。
- 首次 GCC 16 的 OpenGL cgo 探测耗时超过六分钟；改用 Clang + 同一 MinGW-w64 headers/libs/linker，完成原生四入口构建。`native/scripts/build-windows.sh` / `clang-mingw.sh` 可复现。
- 旧 Electron 0.1.7 ZIP/setup/portable 三包重新核对原 SHA 均匹配；无源码回退、reset/clean、推送或发布。

## 下一项（原生迁移任务仍有效）

先补原生终端 grapheme/scrollback 与 Windows 运行验收，再迁移同会话资源监控与安全编辑，覆盖四能力并行/拒绝/超时/隐藏暂停；随后队列、设置、连接库、原生文件源关联与顶部标签规则。完整矩阵见 `native/MIGRATION.md`。当前无原生远程编辑、监控、FTP、持久密码/连接库及完整队列，不能声称已与 0.1.7 对等。不得复用旧 Electron 测试作为新 Go 实现的证明。厂商契约未知不阻塞这些本地迁移任务。

---

# 原生迁移进行中（2026-10-27）

用户“不用 Chrome 内核”的原生迁移仍为有效任务；旧 0.1.7 布局交付不能替代此任务。唯一编辑目录仍为 forterm。新增独立 Go 模块 `native/`，没有改变旧 Electron 发行包。Fyne 2.8.2 + 原生 VT 组件，不使用 WebView/Electron/Node。

本轮已修复原生终端关闭管道后的错误循环，以及 UTF-8 分包缓冲、中文双列光标/覆写/渲染背景的问题。真实 Fyne 窗口 + 本机合成 SSH/SFTP 已通过：一次认证、同 transport 文件上传下载、窗口 resize、默认 15 秒 SSH 协议心跳且无终端输入注入；截图 `native/evidence/native-live-window.png`。Windows 交叉编译正在进行，尚未交付原生 Windows 包；不宣称 Windows 运行或完整替代 0.1.7。仍需收敛完整终端回归、跨进程入口、迁移矩阵与原生包。

原生代码尚缺远程编辑器、资源监控、FTP、安全凭据持久化及完整队列等。此前失败日志保存在 native/evidence/original-termprobe-failure.log.gz，后续成功证据独立保存，不将旧 Electron 测试当原生结果。

---

# Forterm 0.1.7 交付完成：三列文件标签与连接栏收起（2026-10-26）


最终包独立复核已通过：经理直接从15:38最终ZIP提取asar，在隔离macOS Electron+两合成SFTP中验证了多来源/同名dirty/重复双击/导航/150%+6队列真实滚轮及点击，根滚动[0,0]、pageerror0、初始认证[2,1]。已读其JSON并匹配最终asar `420553db5a625e1081c50fce5cd455c2ef62460e5fadcc3990bea784663fdf30`，JS c6b5523c… / CSS 7af95d77…；见release/INDEPENDENT-VERIFICATION-0.1.7.json。此前“待最终包独立复核”的状态由此更新；不是Windows原生/生产现场。此后只改交付文档，运行时和三包未变。

已完成用户最新布局及“缩起来”的更正：左文件多来源标签、中央终端/安全编辑分屏、右监控；最左整块连接/堡垒机导航可在顶部明确收起，工作区顶部固定展开入口支持Enter/Space和焦点交接，原宽/开关重启保留。不是锁定或密码锁。源码/界面/Windows三包版本均0.1.7，0.1.6及更旧包保留，未推送或上传发布。

## 本版行为

- 文件/监控分别收起/拖宽，偏好持久化/重置/旧布局迁移；窄中央暂底部分屏但不修改左右首选，PTY实时适配；导航收起不影响文件/目录/草稿/终端/监控/连接。
- 主进程FileWorkspaces保存多引用/active，旧active投影与linkFiles单一替换兼容；新UI用attach/select/detach。来源标签协议/名称/连接状态可见，目录/筛选/排序/滚动独立，非活动来源也隐藏顶部重复标签。
- 文件来源切换保留各来源同名文档和dirty；编辑器显示来源且始终保存/传输到原session。离线草稿可编辑、保存禁用，不影响另一个可用来源；关闭文件标签仅解除引用，dirty/传输有说明确认，文档及传输保留，最后引用解除恢复顶部文件标签。
- 双击目录进入、文本原地分屏/重复聚焦，不增加顶层会话或窗口；后台目录刷新不会吞掉文本双击。不支持文本/FTP明确提示，无自动下载/执行。
- 选中文件标签只自动滚入自身横向视口，不抢焦点；外部聚焦隐藏来源直接到所属工作区并选中来源；双终端候选只用可见工作区，实际输入不串会话。

## 最终验证与修复

- 类型/构建、4项布局/引用迁移单元、8套定向Electron（three-columns/navigation/reachability/clipboard/workbench/bastion-access/ui/burst）通过；真实两独立SFTP及FTP、源绑定保存、目录/筛选/滚动、重复/不支持双击、断线/dirty、共享/运行上传、真实拖动/捕获取消/键盘/重启/150%均覆盖。监控隐藏10.3秒无新增请求，恢复继续；无额外认证。
- 新增150%+六条展开队列后的文件行完整可达检查发现并修复真实高度挤压：来源视图内部滚动+200px最小文件面板。最终100条控件测量0失败，不滚动隐藏根掩盖裁切；修复后的三列/导航/剪贴板及综合/入口检查再次通过。
- 最终截图8张，已查看三列/150%/固定导航恢复入口/队列文件行/浅色ANSI等；原生capture避免CSS视口截断。
- 最终三包重新构建（包含上项组合修复），ZIP CRC/438条目、四x64 PE、ZIP与win-unpacked入口/asar一致、0.1.7包内版本、6个dist与asar逐字节一致、Windows ConPTY、三包SHA256通过。0.1.5/0.1.6旧三包校验保持。
- 没有无变化复跑旧P0/BOM全量或厂商API；具体提供器仍为空、登录禁用、0门户请求，无假资产。

交付路径：

- `release/Forterm-0.1.7-x64.zip`（完整四入口，约170MiB）
- `release/Forterm-0.1.7-x64-setup.exe`（NSIS，约136MiB）
- `release/Forterm-0.1.7-x64-portable.exe`（便携版，约140MiB）
- `release/SHA256SUMS-0.1.7.txt` / `release/VERIFICATION-0.1.7.json`
- `release/RELEASE-NOTES-0.1.7.md` / `release/VALIDATION-0.1.7.md` / `release/WINDOWS-ACCEPTANCE-0.1.7.md`
- `release/TEST-EVIDENCE-0.1.7.json`：14份日志、6个runtime、8张截图摘要，4项unit/8套UI/100条控件范围
- `release/reachability-0.1.7.json` / `release/screenshots-0.1.7/` / `release/WORKSPACE-LAYOUT-0.1.7.md`
- `checkpoints/forterm-0.1.7-source-final-*.tar.gz`：原未跟踪源码本地恢复点，恢复到新目录审阅，不能覆盖用户修改。

经理已独立复核开发快照（/tmp/forterm-manager-layout-0.1.7.json，JS d54542ae… / CSS 1f0bb8c8…），最终版后来加入选中标签自身滚动与队列低高度修复，具体新摘要在TEST-EVIDENCE；最终三包仍供经理独立核验，不宣称已完成其最终验收。

## 后续边界

Windows原生DPI/ConPTY/剪贴板/safeStorage及真实控制器/生产堡垒机仍待现场；本机Mac回环与交叉构建不冒充现场通过。未签名。门户内置登录/资产仍待脱敏契约，暂不能直接登录；FTP安全在线编辑/排他新建仍不可用。该本地阶段不再主动扩展，等待最终独立复核、用户接口资料或现场反馈继续，不重复催用户。

---

# 0.1.7 发版前组合修复中（2026-10-26）

三列/多文件源/导航收起、剪贴板、综合工作区、入口及11进程去重已通过。新增“150%且六条展开队列后文件行按钮完整可达”检查实际发现列表高度被工具栏挤压；已定位，正改为来源视图内部滚动并保留最小列表高度，再做组合/源状态回归。已生成的本轮0.1.7包尚未最终交付，修复后将重建；0.1.6旧包保留。最终状态以随后新的记录为准，不把当前包当最终验收版。

经理开发快照独立复核已通过，证据在/tmp/forterm-manager-layout-0.1.7.json；该证据不含本次新增低高度队列组合，不能代替最终包。最后 selected 文件标签自动滚入自身横向视口，三列/导航专项已再次通过且不抢焦点。后续只收敛此实际缺陷与打包，不扩展框架，不复跑无变化P0/BOM全量。

---

# 0.1.7-dev.1 开发中：左文件多标签、中央编辑分屏、右监控（2026-10-26）

用户新布局要求正在实现，尚未出0.1.7包，0.1.6旧包保持。已完成三列布局/独立宽度与可见性/导航折叠/旧布局迁移，主进程真实多来源关联与active兼容映射，左侧文件协议/名称/状态标签及选择/追加/解除IPC。切换保留各来源目录/筛选/排序/滚动和各来源文档，编辑器标题明确来源，保存/传输仍捕获原连接；非活动源同样隐藏顶部重复标签，解除/关闭最后所属工作区恢复。

中央编辑器保留左右/底停靠，窄窗口暂用底部分屏并保留偏好，终端不被侧栏挤掉。普通文本双击原地打开/复用安全编辑器，文件夹进入目录，FTP不支持时明确提示、不会自动下载。无厂商请求或新假资产。

类型/构建及4项布局/旧关联迁移定向单元已通过。真实Electron综合脚本正在扩展/核对：目前已实际覆盖两独立来源状态、来源绑定保存/草稿、目录/文本/重复/不支持双击、三列/拖动/停靠/收起、取消与共享/传输保护；正在完成隐藏监控暂停、150%完整组合/可达性、旧脚本迁移和最终版本回归。断线来源保留可选择的元数据，离线草稿可编辑但不能保存；可用来源的编辑保存不随浏览标签错误禁用。

完成验证与截图后必须生成0.1.7完整Windows三包/SHA/说明/现场清单；不要只停在开发代码。当前有效任务不依赖厂商API契约，后者继续等待用户脱敏资料。不复跑无变化旧P0/BOM全量，不擅自推送/发布。

---

# Forterm 0.1.6 阶段版交付完成（2026-10-26）

已按用户新增“同时显示时隐藏已选文件协议会话顶部标签”要求，将实际改动生成Windows可用阶段包，供经理最终包独立校验。当前源码/包/界面版本均为0.1.6。**门户内置登录/资产列表仍待厂商适配，目前不能直接登录堡垒机。** 生产提供器注册为空，账户登录禁用，不产生门户请求或合成资产；不是把接入基础包装成真实厂商接通。

## 最终0.1.6验证与交付

- 类型、构建、真实标签关联专项、未适配/零门户请求界面、原UI入口smoke和11进程实际去重通过。两张最终0.1.6原生截图已逐张查看：关联源隐藏并可编辑，150%下待适配登录禁用控件可见。
- 标签验证覆盖SFTP/FTP切换、解除/共享关联与最后所属工作区关闭恢复、底层连接保留、隐藏源实际编辑保存/dirty保护、快捷键/关闭其他跳过隐藏源、外部聚焦转所属工作区，无额外认证。
- 21项基础层定向测试属于此前开发快照；本阶段核对6个被测源文件摘要均未变，证据单独标明复用，未伪称重跑。无变化的旧P0/BOM/全量旧协议未重复运行。
- 三种Windows x64包已交叉构建并核验：ZIP CRC/438条目、四x64 PE、ZIP与win-unpacked四入口/asar一致、包内0.1.6、6个dist与asar逐字节一致、Windows ConPTY齐全、三包SHA256。0.1.5三包与原SHA256一致，历史发行包保留。
- 未签名、推送或上传发布；macOS回环与交叉构建不是Windows原生/生产现场验收，详见现场清单。

交付路径：

- `release/Forterm-0.1.6-x64.zip`（四入口完整运行时，约170 MiB）
- `release/Forterm-0.1.6-x64-setup.exe`（NSIS，约136 MiB）
- `release/Forterm-0.1.6-x64-portable.exe`（便携版，约140 MiB）
- `release/SHA256SUMS-0.1.6.txt`、`release/VERIFICATION-0.1.6.json`
- `release/RELEASE-NOTES-0.1.6.md`、`release/VALIDATION-0.1.6.md`（同docs）、`release/WINDOWS-ACCEPTANCE-0.1.6.md`
- `release/TEST-EVIDENCE-0.1.6.json`（9份最终日志、6个runtime和2张截图的摘要）、`release/FOUNDATION-EVIDENCE-0.1.6.json`（21项开发快照及被测源未变的证据）
- `release/screenshots-0.1.6/`，以及版本化的 `BASTION-CONTRACT-TEMPLATE-0.1.6.md` / `BASTION-ACCESS-DESIGN-0.1.6.md`
- `checkpoints/forterm-0.1.6-stage-source-*.tar.gz` 本地可恢复源码，保留原未跟踪Git状态，恢复须解压到新目录审阅。

经理已独立复核此前标签开发快照（/tmp/forterm-manager-linked-tabs.json），最终三包仍待其独立核验，不冒充已通过。本轮工作收敛于已授权标签改动与阶段交付，不再扩大接入框架；具体厂商提供器等待用户正在收集的脱敏契约，再按真实接口继续。不重复催用户、不请求旧Cookie/Token、不猜厂商API。

---

# 0.1.6-dev.1：关联文件会话隐藏顶部标签（2026-10-26）

用户要求“同时显示时，所选文件协议会话隐藏顶部对应会话”已实现。按明确关联隐藏 SFTP/FTP 的独立顶部标签，底层文件连接、编辑文档和传输保持；更换/解除关联后原文件标签恢复；共享源在仍有工作区关联时继续隐藏，最后一个关联工作区关闭后恢复。关联关系保留时收起面板不恢复重复标签。

标签切换快捷键和“关闭其他标签”只针对可见工作区，不会误关隐藏文件源；外部聚焦隐藏源会转到其所属可见工作区。旧关联若形成环仍保留一个可达入口。既有UI辅助脚本改按会话ID定位标签，避免把连接数组下标当标签下标。

类型、构建、真实Electron专项和适配后的综合workbench通过：SFTP/FTP关联切换/恢复、共享源/最后所属工作区关闭、隐藏源实际编辑保存/未保存切换保护、快捷键/关闭其他/外部选择、无额外认证。已查看原生截图 `test-results/linked-file-tabs-0.1.6-dev.1.png`。日志 `typecheck-linked-tabs` / `build-linked-tabs` / `linked-tabs` / `workbench-linked-tabs` 的 `*-0.1.6-dev.1.log`。

修改在当前开发源码中，0.1.5发行包未重打；不冒充已安装Windows客户端更新。堡垒机具体提供器继续等待脱敏契约，以下接入基础结论不变；没有重复旧P0或厂商请求。

---

# 0.1.6-dev.1：堡垒机真实接入基础，具体提供器等待脱敏契约（2026-10-26）

用户新授权已落实为代码，当前源码/包版本标记0.1.6-dev.1，未重打0.1.5或制作宣称已接通登录的新安装包。0.1.5三包和校验保留；唯一源码编辑者仍是当前程序员聊天，未向其他聊天发消息。

## 已完成并定向验证

- `bastion-provider.ts` + `bastion-service.ts`：主进程独立接入服务；账户/身份/MFA/资产分页与账号协议/审批/一次性兑换/真实通道就绪，受限类型、IPC、preload与账户接入面板。
- 密码经主进程safeStorage回调临时使用，解密后复核账户版本；手动密码仅本次。主动提交才计算TOTP，窗口活跃性在解密后再次核对；无自动登录/提交验证码。
- 门户句柄、Cookie/Token/sid/授权票据/临时SSH密码不进renderer、导出、日志/错误。资产/账号/游标为本地随机引用，旧身份/跨资产/协议越权拒绝。最终描述显式交Sessions，绝不用门户长期密码兜底SSH或把sid拼URL。
- 登录/MFA和同选择申请在途去重，默认15秒请求/60秒连接等待；超时/中断/取消时一次性兑换可能unknown，锁定重复点击且不重试。审批仅主动刷新、不重复申请；本地取消与服务端撤销ack区分；到期仅本地清理。
- SSH必须terminalStatus ready、SFTP须真实通道成功才显示ready。门户注销/到期/取消不关闭已认证transport，避免绕过现有草稿保护；未认证连接尝试可停止。用户主动关闭正在连接的会话立即更新操作状态。
- 生产注册为空，界面待适配且登录禁用、无假资产。测试适配器只在tests/，无生产env开关，不提供任意renderer HTTP代理。
- 21项定向测试全部通过（14接入服务+4账户+3 TOTP），含本机合成HTTP和真实SSH/SFTP、挑战/成功/过期、分页/账号协议、审批三分支、重复点击、消费后响应未知不重放、注销/配置变化/晚到结果与SSH生命周期、通道权限降级、秘密投影与错误脱敏。
- 类型/构建、真实Electron待适配/零门户请求/受限IPC/150%完整原生截图，以及原UI smoke（真实PTY、诊断/Xftp/新窗口/共享会话移除）通过。未复跑无变化的P0/BOM或全量旧协议测试。

已查看 `test-results/bastion-unavailable-0.1.6-dev.1.png`。日志为 `typecheck`、`build`、`bastion-service`、`bastion-access-ui`、`ui` 的 `*-0.1.6-dev.1.log`。实现与测试/限制细节见 `docs/BASTION-ACCESS-DESIGN.md`。

## 用户资料与真实未完成项

`docs/BASTION-CONTRACT-TEMPLATE.md` 已补中文F12 Network清单：Preserve log/All，正常登录→MFA→资产搜索/分页→展开资产选账号/协议→一次SSH申请到启动，Fetch/XHR/Doc/本机HTTP或WS/协议唤起Initiator；不制造失败或重复审批。原始HAR/完整curl留本机，sanitized也必须人工检查产品token/body/URL等；先给关键请求脱敏结构或截图。已知get_sso_cmd/set_double_approval_sid/get_operator_client_by_resource不用重复手抄。

具体厂商、登录/MFA/资产/账号和sid之后最终连接来源仍缺契约，所以真实登录/资产/一键终端尚未接通。没有猜华为CBH、读取旧凭据或访问生产审批。当前临时描述仅SSH/SFTP密码；若厂商要求证书、工具/SDK或隔离网页登录，须按真实契约适配。隔离Web窗口的无Node/preload、独立内存会话、固定origin/nonce与清理设计已写文档，但尚未加载任何远程网页。

收到新脱敏资料后直接从provider注册及具体字段映射继续；不要把本基础层当作完整接入交付，不重新询问已经明确的产品版本/目标流程。

## 0.1.5 独立复核新证据

经理已通知最终包独立复核通过，并已读取 `/tmp/forterm-manager-release-0.1.5.json`：三包SHA、ZIP/438项/四x64 PE、asar/runtime/ConPTY匹配；从发布ZIP直接运行的隔离UI验证了权限特殊位、非空删除确认/取消、新文件首存/自动刷新、右侧编辑/拖动/三向停靠、终端/一认证、150%菜单/pageerror0。仍是macOS Electron与合成本机服务，不冒充Windows/生产现场。经理据此向用户交付0.1.5，程序员继续当前新接入任务；旧门槛“待最终独立复核”已由本条更新。

---

# Forterm 0.1.5 本地交付完成，待最终独立复核与 Windows 现场验证（2026-10-26）

用户重新授权的累计本地功能已合并实现，并生成新版本完整 Windows 三包。不是因旧时间边界收尾。唯一源码编辑者仍是当前程序员聊天；未向其他聊天发送消息、未提交/推送/发布，旧发行包保留。

## 本轮完成

- P0：每文件连接串行队列、排队状态、文件间隙元数据、覆盖确认在队列中、列表失败保留/重试/过期请求防护、空闲合并刷新、取消/停滞/元数据超时清理；仅重置文件通道，SSH 保留，不自动重试或重放临时票据。
- 统一资产工作区：终端/文件/在线代码编辑/监控，可显式关联独立 SFTP/真实被动 FTP，继续可复用本 SSH 的 SFTP；不按代理 IP/临时用户名自动配对，文件编辑/传输绑定原文件源，监控绑定终端；共享源/草稿/传输有生命周期保护。
- 代码编辑器默认右侧，右/左/底停靠与实际拖动、持久化/重置/收起；CodeMirror 行号、语法、行列、缩进参考与选项，空白/换行标记默认关闭并可记住。保留原 BOM/统一 LF或CRLF/尾空格/无尾换行，混合换行明确转换；修复含 BOM 粘贴撤销时 BOM 元数据残留，原 BOM 策略保持。
- 新文件为内存草稿，首次显式排他保存才落盘，同名/并发同名拒绝；新文件/保存成功按原文件源通知目录合并刷新。
- 文件/目录/空白区、编辑器、终端、标签右键和键盘菜单；真实非空递归删除、完整路径/子项确认、取消与部分失败数量、根/遍历/链接/恶意项/数量深度时间界限；权限 rwx/八进制、特殊位保留、读回核验、未知不当000。
- 原生剪贴板和快捷键统一、CtrlC选区复制/无选区中断、多行与尾换行预览、无额外Enter、异步焦点/会话失效；代码及普通输入框标准编辑，错误/空/超限可见。完整浅色 xterm ANSI/光标/选区与代码主题，不重建连接。
- 左侧堡垒机多账户组与搜索/折叠/维护，长期密码与验证器密钥独立 safeStorage 加密、保留/替换/清除、标记不回显、无秘密导出；标准 TOTP 主动查看/倒计时/复制、隐藏停止，保留手动验证码，无自动登录或终端注入。
- 保留默认15秒可设心跳、四种能力权限降级、Xshell/Xftp精确参数转交、入口重复启动去重与临时凭据仅内存规则。

## 验证与交付

- 60 项综合单元/回环协议通过；类型/构建通过。
- 真实 Electron：账户、统一工作区、五类实际分隔条、99条125%/150%控件可达性、综合代码/菜单/关联、新文件自动刷新与取消/同名、生命周期、完整原生剪贴板、BOM历史、批量上传、外部入口、11进程去重均通过。
- 额外 file-guards 专项：真实编辑器 dirty 注册阻止删除/权限修改且不弹破坏性确认；实际运行上传保护子树；确认期间新产生的 dirty 在执行前再次拒绝，原字节保留。
- 初轮入口压力出现测试子进程未退出；测试已等待完整界面并增加15秒子进程超时，清理隔离测试后最终独立回归通过。没有改动或放宽产品去重规则，也不声称已确定该初轮现象的根因。
- 12张最终实际截图已收集，查看默认右侧、单行工具栏、账户、浅色ANSI/选区、高缩放完整原生菜单/工作区等关键图；99条控件测量0失败。
- 新版 ZIP CRC/438条目、四x64 PE、0.1.5包内版本、6个dist与asar逐字节一致、Windows ConPTY与三包SHA256均核验。

交付文件：

- `release/Forterm-0.1.5-x64.zip`（四入口完整运行时，约169 MiB）
- `release/Forterm-0.1.5-x64-setup.exe`（NSIS，约136 MiB）
- `release/Forterm-0.1.5-x64-portable.exe`（便携版，约140 MiB）
- `release/SHA256SUMS-0.1.5.txt`、`release/VERIFICATION-0.1.5.json`
- `release/RELEASE-NOTES-0.1.5.md`、`release/VALIDATION-0.1.5.md`（同 `docs/VALIDATION-0.1.5.md`）
- `release/WINDOWS-ACCEPTANCE-0.1.5.md`、`release/TEST-EVIDENCE-0.1.5.json`、`release/reachability-0.1.5.json`、`release/screenshots-0.1.5/`
- `checkpoints/forterm-0.1.5-source-final-*.tar.gz`：未跟踪源码的独立可恢复本地检查点，恢复须解压到新目录审阅，不能覆盖现有用户修改。

经理此前已独立复核 P0 开发快照及 BOM 定向修复。上述最终包已形成具体可复核成果，最终全集/包的经理独立验收尚未宣称通过。

## 真实边界与后续

Windows 原生 DPI/ConPTY/剪贴板/safeStorage、真实控制器及生产堡垒机待现场。三包无代码签名。厂商登录/资产/票据接口缺契约，内置登录/资产尚未接通；本地账户/TOTP是真实可用功能，算法匹配待厂商确认。FTP仅被动明文，在线安全编辑/排他新建明确禁用，无FTPS/主动模式；SITE CHMOD和原mode可验证才开放权限。删除不可回滚，SFTP最终检查存在协议级竞争窗口；重要文件ACL/所有者/硬链接保留不作承诺。

本轮不再主动扩展需求。后续处理最终独立复核或用户现场反馈，不能将时间流逝当作验收通过；有效协调跟进保持原状态，未操作它。

---

## 以下为 0.1.5 开发过程历史（已由上文最终记录更新）

# 0.1.5 开发中：账户/TOTP、统一资产工作区与编辑器（2026-10-26）

当前唯一代码目录仍为 `/Users/xianyu/IdeaProjects/forterm`，0.1.4 是最后已交付稳定版本。以下新功能正在实现与回归，尚未交付，不能当作厂商内置登录已接通。

## 最新核对与续接点（2026-10-26 旧 heartbeat 收尾）

本次收到 `forterm-24` 的旧截止指令，但交接记录已确认用户于 2026-10-26 重新授权，旧 2026-10-25 截止时间不适用于后续明确工作。调用 automation_update 删除该旧 ID 返回 `not_found`，旧跟进已不存在；未修改协调聊天的新自动跟进。本轮仅核对现有证据与更新交接，没有取消后续授权、扩展功能或重新运行无变化测试。

- 最后稳定交付仍为 0.1.4：ZIP（四入口）、NSIS、portable、SHA256 与验证/现场清单完整保留。0.1.5 没有发行包，不能当作已交付。
- `test-results/unit-0.1.5.log`：54 项单元/本机协议测试通过；后加 text-format 测试尚未计入该结果。
- 已通过真实 Electron 的 accounts、workbench、clipboard、lifecycle 四套回归。覆盖账户加密与主动 TOTP、单行工具栏、编辑器三向停靠/拖动/收起/跨会话缓存、独立 FTP/SFTP 关联与共享连接生命周期、浅色 ANSI、原生剪贴板与多行确认、权限与非空删除基本路径、高缩放及键盘菜单。
- 最新 `bom-history-0.1.5.log` 通过：真实含前导 BOM 的浏览器 ClipboardEvent 分别粘贴到无 BOM/有 BOM 原文，撤销恢复内容与 dirty；切文件和停靠后重做、显式保存保留原文件 BOM 策略。修复为粘贴不改变原文件 BOM 策略。macOS 原生剪贴板会剥离前导 BOM，该专项不是 Windows 现场验证。
- 最新 typecheck/build 日志通过。P0 的协议、Electron 和经理独立快照证据维持原结论，未因编辑器变化重复运行。

发版前仍须完成：新增测试的最终综合核对；补充破坏性操作/取消/错误边界和新增文件后的列表刷新检查；复核最终截图；保留入口去重与四能力行为的适当回归；生成并校验新的 0.1.5 Windows 三包、更新说明、SHA256 与现场验证清单。旧 UI 脚本有 textarea/底部编辑器假设，不能未经适配宣称全部通过。

明确限制不变：堡垒机登录/资产/票据兑换缺厂商契约，尚未接通；FTP 在线安全编辑与排他新建未开放，能力提示必须真实；Windows 原生 DPI/剪贴板/生产堡垒机仍待现场验证。没有读取真实票据、请求生产接口、推送或发布。

## 本轮全部明确需求

- 左侧“堡垒机”分组保存多个非秘密账户条目，搜索/折叠/新增/编辑/删除；长期登录密码和验证器 seed 与 SSH 密码及控制器票据完全分开。
- safeStorage 加密保存、标记回显、保留/替换/显式清除；TOTP 默认 SHA1/6 位/30 秒，支持严格 Base32 / otpauth TOTP 参数；主动查看/倒计时/复制，隐藏和关闭停止，无自动登录/SSH 注入。
- 所有同类头部紧凑单行；纯 SFTP 主机/类型/监控/终端一行；目录/筛选/排序/操作同一行，窄面板明确横向滚动按钮。
- 编辑器默认右侧，右/左/底可选，尺寸拖动、持久化和旧布局迁移；移动/收起不丢文件、草稿、滚动/光标。
- CodeMirror 代码编辑器：行号、缩进参考线、光标行列、语法高亮、语言与缩进选项；空格/Tab/LF/CRLF 标记默认关闭，用户开关开启，开关不改内容。
- 保留 BOM、统一 LF/CRLF、尾空格和无尾换行；混合换行先只读保留，必须由用户明确转换后才可编辑，不能静默归一化。
- 同一资产工作区显式关联独立 SFTP/真实 FTP，继续保留本终端 SSH 复用 SFTP；不按共享代理 IP/临时用户名自动匹配。草稿/运行传输与共享连接的生命周期须回归。
- 浅色主题同时更新所有 xterm ANSI/背景/光标/选区，编辑器一起变；不重建 SSH/PTY，不丢输出、选区或滚动。

## P0 多文件上传稳定性（优先，尚在扩展回归）

- 已暂停新增功能扩展先修上传卡住。按文件连接建立串行传输队列，queued/running/终态，文件间隙优先目录元数据；其他连接独立推进。
- 覆盖检查/确认进入队列，未开始前不打开流；列表失败保留最后目录及内容，空闲后合并刷新，可取消元数据并防过期结果覆盖。
- 元数据超时、传输停滞/取消清理只重置 SFTP 通道，保留 SSH transport；停止未开始队列，不自动重写/重放票据。FTP 同样串行，关闭或取消不自动重连。
- 7 项 P0 协议回归已通过（1 文件句柄上限、24 文件哈希、追加、停滞超时/取消/恢复、元数据拒绝/不回应、多连接、真实 FTP 批次）。
- 真实 Electron 24 文件界面回归已通过：0 并发拒绝/传输中目录拒绝、逐文件 SHA256、失败列表保留重试、取消后恢复浏览且终端可输入、一次 SSH 认证。
- 正在补传输时切目录、心跳协调、慢 stat 目标保护及剩余综合测试。当前没有打包或宣称全部新功能已验收。

经理又用独立服务器与不可变开发快照（main SHA256 `bfa96b2122ec6164da4dc61e4ef53505367aea1e609dbcb44c2d92c73a6df541`）复核 P0：20+4 文件逐个 hash、最大 1 句柄、0 打开/目录拒绝；默认 30 秒停滞阈值约 30.7 秒后活动与排队均明确 error，SFTP 可恢复浏览与再次上传、SSH 仍一认证并可输入、pageerror=0。仅代表 P0 开发快照，不是最终全集/安装包验收，也不证明生产根因。

## 必须合并交付的累计项

除上文账户/TOTP、工具栏、关联、编辑器及浅色终端外，还包括：新文件首次显式排他创建、文件/空白/终端/标签/代码编辑右键、完整复制粘贴、多级非空目录删除（用户已确认目录有子项）和权限读写。菜单、删除、权限为合并发版重点，不能遗漏或以仅空目录替代。需覆盖会话绑定、确认、取消、部分失败、根/遍历/链接/恶意项边界、644/755/特殊位及 FTP 能力限制。

## 已实现 / 验证中

- 已核对 RFC 6238 和 Google Authenticator Key URI Format，主进程 TOTP、独立账户存储、IPC 与配置 UI 已加入。
- 8 项新增单元/协议测试已通过：RFC 全算法向量、6 位前导零/周期、严格配置解析、加密/清除/旧库/导出/失败边界、真实 FTP 列表/上传下载与协议入口。
- 单行工具栏、可停靠编辑器、CodeMirror、独立文件关联、完整 xterm 深浅调色板已进入工作区，正在真实 UI 与格式敏感测试。
- 不能把尚未回归的代码当最终版本。继续补齐测试、修复边界、截图与新版本 Windows 包。

## 明确边界

堡垒机厂商未确认、登录/资产/sid 兑换接口仍缺契约。本轮账户和验证码是本地真实功能，不发厂商请求、不复用旧 Cookie/Token，不伪造成功。FTP 是真实独立被动 FTP；当前在线安全编辑仅 SFTP，FTP 明示下载修改上传，取消 FTP 传输会关闭该文件连接，不影响终端。

---

# 堡垒机内置接入准备（2026-10-26）

## 新确认的目标与事实

用户明确要求“在 Forterm 内登录堡垒机、查看资产，再一键打开终端”，尽量不依赖本机单点控制器，但继续遵循服务端 SSO/MFA、授权、审批与票据。

用户提供：厂商“应该是华为的”（推测未核实）；产品“运维审计管理平台”；主版本 `V3.0`；当前 `3.0.12 信创20240305`；版权 `© 2008-2014`。主聊天仍在核对厂商与官方文档，不能直接套华为云 CBH，也不能按同名/同版本检索线索确定提供器。

历史三个接口据主聊天归纳只证明 sid 与客户端选择；登录、授权资产列表、sid 到最终 SSH 端点/临时认证信息的链路仍缺完整契约。没有读取旧 Cookie/Token 或生产历史请求。

## 本轮已完成

- 更新 `docs/BASTION-INTEGRATION.md`：确认事实、SSO 工具依赖的判定条件、最小接口资料、已核对的复用位置、状态成功门槛及后续测试计划。
- 新增 `docs/BASTION-CONTRACT-TEMPLATE.md`：只收结构/状态的最小资料模板，不收真实秘密或猜测端点。
- 静态检查 `launch.ts`、`schema.ts`、`Sessions`、IPC、preload、Prompt 和 Store：现有代码没有门户身份/资产 HTTP 提供器；可复用 SSH 与通道层。
- 明确两个不能混同的状态：portal sid 不是 SSH 凭据；`Sessions.connect()` 返回运行 ID 只表示任务创建，不代表 shell ready。未来提供器在主进程完成授权/兑换并直接交给 SSH，renderer 不接收原始票据。
- 明确错误脱敏、登录窗口隔离、审批取消/超时、一次性消费、重复点击及可用通道降级边界。

## 下一步 / 当前缺口

主聊天取得产品与版本匹配的官方文档，或授权测试环境中的新流程脱敏结构。最少补齐三组：登录/身份确认（含挑战）、授权资产/账号/分页、申请/审批/sid 兑换完整连接描述。必要时确认官方 SDK 与本机工具的受支持调用方式。

只有契约明确才实现具体 provider，并先用本机合成 HTTP+SSH 验证。是否可完全去掉本机控制器尚不能承诺。当前没有创建登录假界面、运行厂商请求或宣称内置接入成功。

本轮仅文档与静态检查，不改运行时代码、不重复类型/协议/UI 测试、不新增版本或重打包。0.1.4 与历史产物保持原样，当前已工作的控制器入口不变。

---

# Forterm 0.1.4 修复进度

2026-10-26：独立复核指出 0.1.3 在最小窗口与 150% 缩放时侧栏实际控件越界，原外框检查不足。本轮针对控件可达性修复，不无变化重跑协议测试。

## 已实现并验证

- 顶部导航和主题栏固定，连接列表/工具各自滚动，低高度紧凑样式；窄工具栏提供横向滚动。
- 欢迎页与纯 SFTP 页面建立完整滚动路径，防止下方连接或文件操作被隐藏根容器裁掉。
- 以完整控件高度约束文件/监控分区，修复“有正高度但按钮无法完整显示”。
- 实际复现 150% 下展开六条传输记录挤掉工作区的问题，限制抽屉最大 min(180px,20vh)，队列内部滚动；刷新与在线保存已验证。
- 控件级回归 99 条记录通过：视口、所有裁切祖先、中心命中；禁止程序滚动隐藏根容器掩盖问题。
- 125% / 150%，空列表 / 40 多条连接，鼠标与 PageDown / 真实 Tab 到导入 / Enter、实际临时 JSON 导入导出、主题往返、设置、长列表编辑、文件/监控/在线保存、分屏工具栏和窗口恢复通过。
- 类型、构建、实际布局、统一工作区回归通过；最后队列修复后再次通过相关类型/构建、控件级和指针布局。

## 0.1.4 交付完成

2026-10-26 10:00（上海）：包含长队列组合修复的最终三种 Windows 包已生成并核验。0.1.3 及此前包保留；没有推送或上传发布。

- `release/Forterm-0.1.4-x64.zip`（四入口完整目录，约 168 MiB）
- `release/Forterm-0.1.4-x64-setup.exe`（约 135 MiB）
- `release/Forterm-0.1.4-x64-portable.exe`（约 139 MiB）
- `release/SHA256SUMS-0.1.4.txt`、`release/VERIFICATION-0.1.4.json`
- `release/RELEASE-NOTES-0.1.4.md`、`release/VALIDATION-0.1.4.md`
- `release/reachability-0.1.4.json`：99 条实际控件记录，0 失败；含视口、控件矩形、裁切及命中结果、设计滚动容器的位置。
- `release/screenshots-0.1.4/`：9 张原生窗口截图，已检查空/长列表、125%/150%、纯 SFTP、编辑/监控、六条队列、分屏工具栏和窗口恢复。
- `release/WINDOWS-ACCEPTANCE-0.1.4.md`

完整性核验通过：ZIP CRC，438 个条目，四入口 x64 PE，ZIP 与解包四入口/asar 摘要一致，0.1.4 版本，6 个 dist 文件与 asar 一致，Windows ConPTY 模块存在。

实测样本：150% 长列表、700×412 CSS 视口中，app/sidebar clientHeight=scrollHeight=412、scrollTop=0；导入/导出 bottom=365.17；主题 bottom=401。工具区在 overflow:auto 中可通过滚轮/PageDown/原生 Tab/Enter 到达；未程序滚动隐藏的根容器来伪造可达性。

Windows 原生 DPI、鼠标/键盘及生产堡垒机仍待用户现场验收。本轮修复没有因未知厂商登录信息而中止；登录/资产接口继续由主聊天澄清。协议实现未改动，没有无变化重跑协议测试。

---

# Forterm 0.1.3 开发进度

2026-10-26 用户重新授权继续开发；旧 2026-10-25 截止时间不再适用。唯一编辑目录：`/Users/xianyu/IdeaProjects/forterm`。

## 本轮已实现

- 五类可拖动分隔条：连接栏、文件/监控面板、编辑器高度、文件/监控比例、双终端上下/左右比例。
- 指针捕获、取消/失焦/丢失捕获清理，窗口级松手兜底；方向键、Shift、Home/End、Enter/双击重置和全局重置。
- 尺寸/方向与独立比例持久化，按容器限制实际尺寸，窗口放大恢复偏好；正常关闭立即保存最终尺寸。
- xterm 帧合并实时适配；修复 shell 延迟就绪时首个 PTY 尺寸可能未同步的时序问题。
- 文件导航/筛选在监控切换与面板收起后保留；监控开关保留，隐藏暂停、展开恢复，仍防止重叠。
- 编辑器可收起保留草稿，展开恢复高度；大文本字节统计缓存，避免每次拖动重编码整份内容。
- 纯 Xftp 查看监控时使用完整监控侧栏，不产生空白文件区，不重新认证。

## 验证与交付完成

2026-10-26 09:20（上海）：0.1.3 已完成本轮明确的可拖动布局任务，生成新版完整 Windows 包；不是沿用 0.1.2 的交付结论。

- 34 项单元/回环协议测试通过：心跳、四能力、编辑冲突、权限降级、传输取消、布局解析及收缩约束。
- 原桌面 smoke、统一工作区 smoke、实际指针布局 smoke、11 进程 burst 通过。最后指针清理与原生窗口稳定等待后，相关类型、构建及完整实际布局测试再次通过。
- 实际布局测试包含五类拖动、两个终端方向、捕获取消/重用、键盘/双击/全局重置、极端尺寸、重启与立即关闭持久化、250ms 延迟 shell 首次 PTY 尺寸同步、编辑焦点/草稿、收起恢复、自动监控暂停恢复、真实 SFTP 上传及展开队列、1050×650 和 125% 缩放。全过程服务端只有一次认证和一次 SFTP 通道开启。
- 五张本轮截图已逐张查看，缩放使用完整原生窗口捕获避免工具截图裁切；未把示意稿当实测。
- 完整性核验：ZIP CRC 通过，438 个条目，ZIP 与解包目录四入口/asar 摘要一致；四个 x64 PE 入口；版本 0.1.3；6 个 dist 文件与 asar 逐字节一致；Windows ConPTY 模块齐全。

交付路径：

- `release/Forterm-0.1.3-x64.zip`（约 168 MiB，四入口完整目录）
- `release/Forterm-0.1.3-x64-setup.exe`（约 135 MiB，NSIS）
- `release/Forterm-0.1.3-x64-portable.exe`（约 139 MiB）
- `release/SHA256SUMS-0.1.3.txt`、`release/VERIFICATION-0.1.3.json`
- `release/RELEASE-NOTES-0.1.3.md`、`release/VALIDATION-0.1.3.md`
- `release/WINDOWS-ACCEPTANCE-0.1.3.md`、`release/screenshots-0.1.3/`

详细证据：`docs/VALIDATION-0.1.3.md` 与 `test-results/*-0.1.3.log`。历史 0.1.0–0.1.2 包保持原样，未 reset/clean/推送/发布。源码仍保留既有未跟踪状态。

## 后续与真实边界

本机实现/打包没有阻塞。Windows 原生拖动、ConPTY、系统缩放、触摸/笔及真实堡垒机通道权限仍需用户现场验证，不能把 macOS 回环测试和交叉构建等同于现场通过。CI 已加入 packaged layout smoke，但没有远程运行。

堡垒机登录/资产接口由主聊天澄清；现有控制器入口保持。尚无厂商/版本、官方授权与资产/票据接口契约，不猜测生产协议、不读取历史凭据。接入边界见 `docs/BASTION-INTEGRATION.md`。收到新契约或现场反馈后可据此继续实施。

---

# Forterm 0.1.2 开发进度

更新时间：2026-10-24 19:07（上海）。唯一代码编辑目录：`/Users/xianyu/IdeaProjects/forterm`。

## 当前目标

用户追加的优先需求已纳入：同一 SSH transport 中按需使用终端、SFTP、在线文本编辑和资源监控，不重复认证或消费临时票据。通道拒绝应隔离，不拖垮其他能力。

## 已完成

- 统一会话：SSH 与 Xftp 均可按需使用同 transport 的 shell、SFTP、在线编辑与 exec 监控，不重复认证；通道拒绝相互隔离。
- 默认 15 秒 SSH 保活、已打开 SFTP 只读探测、设置持久化和每会话快照；关闭清理、无响应不堆积、禁用后无探测。
- UTF-8 在线编辑，最多 12 个、每个 1 MiB；二进制/符号链接拒绝、BOM/CRLF 保留、双次冲突检查、0600 临时文件写入后恢复原普通权限位再替换、草稿关闭/断线/重载保护。
- 文件筛选/排序/路径/隐藏文件，上传完成刷新，传输取消与已结束队列清理。
- 监控手动或主动启用每 10 秒刷新，防重叠，隐藏后停刷，异常不拖垮终端。
- 主题/字号/侧栏/分组折叠持久化，快捷键帮助，分屏方向及第二会话选择，激活标签同步输入焦点，搜索框输入隔离。
- 32 项单元/回环协议测试、原 UI smoke、工作区 UI smoke、11 进程 burst 全部通过；最终权限修订后完整 32 项、类型、构建与工作区 UI 再次通过。
- 真实 UI 截图已检查并修复紧凑布局重叠、浅色侧栏对比、监控错误可见性。详见 `docs/VALIDATION-0.1.2.md`。

## 交付完成

0.1.2 最终三种包已于 2026-10-24 19:07（上海）生成并通过完整性核验，包含权限恢复与分屏输入焦点修订。已提前达到交付门槛，停止主动扩展，不等待截止日期空转；后续按用户新反馈继续。

- `release/Forterm-0.1.2-x64.zip`：约 168 MiB，含四个入口与完整共享运行时。
- `release/Forterm-0.1.2-x64-setup.exe`：约 135 MiB，NSIS 安装包。
- `release/Forterm-0.1.2-x64-portable.exe`：约 139 MiB，自解压便携版。
- `release/SHA256SUMS-0.1.2.txt`：三种发行包的 SHA256。
- `release/VERIFICATION-0.1.2.json`：ZIP CRC、438 个条目、ZIP 与解包目录的四入口/asar 摘要相同、四个 x64 PE 入口、0.1.2 包内版本、6 个 dist 文件逐字节一致、Windows ConPTY 预编译模块验证通过。
- `release/RELEASE-NOTES-0.1.2.md`：简明更新与升级说明。
- `release/screenshots-0.1.2/`：九张真实 Electron 界面截图。
- `docs/VALIDATION-0.1.2.md`：验证证据、日志索引和真实边界。

## 下一步 / 阻塞

没有本机开发或打包阻塞。Windows 及真实堡垒机现场验证需要用户回来操作，不能由 macOS 交叉构建代替；按 `docs/WINDOWS-ACCEPTANCE.md` 验收。没有推送、上传发布或远程 CI 运行。源码仍为原先未跟踪状态，未 reset/clean/提交；历史 0.1.0 和 0.1.1 发行包全部保留。

## 现场验证边界

所有新协议验证仅使用合成本机回环服务。本机为 macOS；Windows、新编辑保存的服务器兼容性、真实堡垒机权限和强制超时仍需现场验证。没有连接生产堡垒机，没有使用真实临时凭据，没有推送或发布。

