# Forterm Native dev.10 连接界面与滚动候选（2026-10-29）

本轮根据用户正在使用dev9的反馈“能滚动，但滚不到下面的内容”以及XTerminal参考图完成原生修订。当前完整Mac候选正常cmd/forterm，可实际使用终端/SSH/SFTP/FTP等既有功能，不是合成预览。程序员未启动GUI，未抢占用户前台，dev9旧包/现有会话不变。

## 改动

- 新建/编辑连接改为四类导航、同一份输入的分类定位、右侧圆角分组卡片、地址与端口同排、用户与密码同排，主文字14dp/辅助12dp/输入约32dp；绿色主要操作及固定footer。窄窗口导航改顶部、内容行上下排列，按钮换行。
- 为Fyne覆盖式滚动条预留独立gutter和上下16dp；外层有界，展开/收起和resize按新宽度重排再测量完整内容。末项完整进入视口，footer保持外置。
- 确认鼠标在Entry上时，其内部滚动容器能抢走纵向滚轮；现转交外层表单，长输入仍保留横向光标跟随。原生焦点遍历、点击和程序聚焦使用同一路径揭示当前字段，无轮询/按键注入。
- 认证16问题、文件权限、本次密码同类窗口共享有界视口，长状态可通过信息按钮完整查看。
- 保留显式系统密码保存/移除、仅保存确认丢弃、保存并连接的本次密码、存储失败保留、测试连接45秒/取消/单飞/指纹认证等规则；未新增未实现的代理/编码/初始化开关，未改终端协议/生产操作。

## 验证

- 最终定向race通过341.634s：保存profile→vault→显式连接、本次密码/拒写保留、后台测试、短窗边界、回收编辑目标；深浅/1150×850、1050×650、800×500、700×433+canvas1.5x、560×500、480×500；实际滚轮命中、滑块拖动、展开/收起、新旧高度、分类末项、Tab焦点、缩小再放大、quick。
- 实际轨道点击/滑块/长输入横向跟随与纵向转交的单独race通过16.177s。16问题认证取消与权限窗口末项有定向测试。打开权限页无远程枚举/更改。
- go vet ./internal/desktop通过。这里只重测变动相关项，未无变化重跑SSH/传输全量。首次软件回归确实捕获Entry吞滚轮导致不到底，修复前日志保留；中途旧源race主动终止以让位最终源码验证，不算通过。
- 软件截图17张位于native/evidence/ui-0.2.2-dev.10/review/，逐图尺寸/SHA在SCREENSHOTS.json；已查看深色顶部、浅色、150%底部、窄窗、上下排列、认证末项及权限底部。截图不是原生系统DPI实测。
- Mac arm64正常构建+adhoc签名验证通过；Windows四入口AMD64交叉构建，ZIP CRC/逐入口摘要通过。

日志：native/evidence/dev10-final-race.log、dev10-track-race.log、dev10-vet.log。恢复点checkpoints/native-before-dev10-scroll-20261029/native-source.tar.gz。

## 冻结产物

- Mac：release/Forterm-Native-0.2.2-dev.10-test-macos-arm64.app
- Mac可执行SHA256：`6fd50be6b0b0afe7c8192ca6690042cb5f5869d485caf47c57eceed86f184d6c`
- Windows审查ZIP：release/Forterm-Native-0.2.2-dev.10-review-windows-x64.zip
- ZIP SHA256：`83b45e2b2db285c134dbb94e1abc06001f61a409f7cab6e6183b881daff890e6`
- 主PE：`416927ad4e720b21f040e8ba33147bbdf7bdcf52cb80cad817fa044abce929d6`
- 463条Go/C/ObjC源码及tar逐项匹配；SOURCE-HASHES SHA：`21e3c9cdc0a5a056264cf0531efb698335efc22bac6f08b660fad76a92024f5d`；tar SHA：`fbfa72f149fb574694a60647c344e05773af29e21adfd02688d71f9c1ef4dedb`。
- 检查点checkpoints/native-0.2.2-dev.10-review-20261028/（脚本目录日期后缀沿用，实际构建29日）。内嵌权限助手另有EMBEDDED-SOURCES.json并与tar匹配。相对dev9仅10项新增/变化，无删除。
- 身份与摘要：release/IDENTITY-Native-0.2.2-dev.10-test-macos-arm64.json、SHA256SUMS-Native-0.2.2-dev.10-test.txt；更新说明native/REVIEW-NOTES-dev10.md。

## 待现场核验

1. 先审软件图，再由用户或经理协调正常Mac候选启动，验证真实鼠标滚轮/轨道/拖动/Tab/原生缩放；尚未执行本轮GUI。
2. 正常应用共用cn.forterm.native实例IPC；dev9仍运行时新入口可能转交旧窗口。由用户保存并退出旧版后切换，或经理明确指定独立IPC和空设置目录并行；不强关会话，不把旧窗口当dev10。
3. Windows实际运行/系统DPI/原生选择器、生产堡垒机上传/权限仍未验证。旧上传复验授权未混用成本轮系统界面验证。
4. 当前是正常Mac与Windows审查ZIP；尚未生成dev10安装器/便携版，不能说原任务所有交付和现场验收完成。保留dev9全部已实现能力及其真实验证边界。

未推送/发布、未读取真实票据、未生产登录/上传/提权，未动用户窗口。
