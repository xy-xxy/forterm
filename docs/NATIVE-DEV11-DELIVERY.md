# Forterm Native dev.11 按钮候选（2026-10-29）

用户看dev10后要求继续调整按钮，本轮实际完成原生组件与底栏。dev10原包保留，用户正在运行的dev9窗口未切换。新包正常cmd/forterm，非合成预览、无WebView。经理已看第一轮软件图确认当前方向；最终GUI仍未亲验。

## 实现

- 同一受控原生connectionButton：34dp高、6dp圆角、14dp正文，横向留白约16dp，测试最小120dp/取消80dp/主按钮144dp。测试与次按钮透明底细边框；主按钮绿色、宽度足够。图标独立16dp并有8dp间距。
- 底栏左侧测试连接和运行时取消测试；右侧取消、保存并连接、描边下拉入口。仅保存信息在原生菜单，提示明确“更多保存方式：仅保存信息”，原有密码丢弃确认/显式系统保存规则不变。
- 宽度按当前可见按钮测量，窄窗/测试中换行，保持footer与滚动区分离；关闭/状态/更多图标为30dp、可访问名称及tooltip，标题改为实际居中。
- 默认/hover/pressed/disabled/focus状态清晰。保留原生按钮可访问角色，Enter/Space触发与焦点保留，禁用不触发。仅刷新该控件；没有昂贵的全局hover刷新。
- tooltip在原弹窗内的独立浮层，MinSize不影响表单，不另装focus manager，鼠标悬停保持Entry键盘焦点；底栏提示自动向上避让触发控件。菜单也向上展开并检查边界，Escape返回保留输入、方向键+Enter触发仅保存确认。
- 保留dev10完整滚动范围、gutter、折叠展开、Tab揭示、窄窗和深浅主题；本轮没有改协议/凭据生命周期、增加自动命令或生产操作。

## 验证

- 最终相关race通过80.854s：profile系统保存与显式登录、仅本次密码/保存并连接、vault拒写保留、显式测试/取消/单飞、共享viewport/真实轨道和长输入，以及新按钮/菜单/tooltip。
- 最终六尺寸滚动/展开/拖动/聚焦/resize/150%回归通过8.800s。按钮软件审查13.236s，深浅、560/480窄窗、运行态、焦点/禁用状态、菜单和提示。vet通过。
- 15张最终软件图：native/evidence/ui-0.2.2-dev.11/final/，SCREENSHOTS.json含尺寸和SHA；已查看深浅底栏、窄窗运行态、五种状态、向上菜单和保持Entry焦点的tooltip。软件渲染并非OS高DPI或物理键鼠。
- 日志native/evidence/dev11-final-race.log、dev11-final-scroll.log、dev11-final-layout.log、dev11-final-vet.log。迭代图/日志保持，不覆盖历史证据。未无变化重跑全部SSH/传输测试。

## 冻结交付

- 正常完整Mac：release/Forterm-Native-0.2.2-dev.11-test-macos-arm64.app；arm64/adhoc签名核验通过，程序员未启动。
- Mac可执行SHA：`4cdb2f372d3ca31cca6a2ba69bdb331a7ac337a164382d502032a838c368fc03`。
- Windows四入口审查ZIP：release/Forterm-Native-0.2.2-dev.11-review-windows-x64.zip；CRC/AMD64/入口摘要通过，未运行。
- ZIP SHA：`fbbb84d052d90dc4dbf163a389fcd4d5ccdadfd77be743dd6d4c2202ee8db63a`；主PE：`417bb5732f404dceb69b32f3fd8d121064dc2fedc20886c82b52b7ed6f23ddfe`。
- 465项Go/C/ObjC源码与tar逐项匹配，相对dev10只5项新增/变化，无删除；manifest SHA `28bce893f5257d45f552e9627c3413957b1e7d6581e141904d5317336fc9b419`，源码tar SHA `8bade168724d6a36fb3fb813f1fdbcb12b2c7809b17f475f9a0e765d420a082d`。权限助手内嵌txt另有EMBEDDED-SOURCES摘要且与tar相符。
- 检查点checkpoints/native-0.2.2-dev.11-review-20261028/（日期后缀为沿用脚本，实际29日），身份release/IDENTITY-Native-0.2.2-dev.11-test-macos-arm64.json，摘要SHA256SUMS-Native-0.2.2-dev.11-test.txt，说明native/REVIEW-NOTES-dev11.md。

## 后续实际边界

正常多版本仍共用cn.forterm.native IPC。dev9未退出时新入口可能转交旧窗口；需用户保存退出旧版，或经理明确隔离IPC/空设置目录并行，不能强关/不能把旧窗口当11。新Mac尚未原生首帧/物理键鼠/缩放/选择器验证，Windows仅交叉构建；生产堡垒机/上传/权限未碰。新安装器与便携版尚未生成，不称所有原任务现场交付完成。未推送、发布或读取真实票据。
