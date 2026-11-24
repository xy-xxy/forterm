> 当前状态更新：经理已按用户许可完成本版实际Mac检查PID92827并退出，7张原图/结果已归档 `native/evidence/ui-0.2.2-dev.7/independent-native-mac/`。首深色名称末位裁剪尚未验收；dev8已离线定位并前置主题初始化，后续候选与首帧方案见NATIVE-UI-REVIEW-DEV8。Windows7复制仍未成功/未执行，解锁请求pending。下文“尚未运行/未回复”是本版执行前的历史计划；原始证据JSON保持原样。

# 原生连接管理与工作区视觉复核 · dev.7

当前可审查结果已实际实现。参照用户指定结构，保留Forterm品牌，未复制参考产品图标、字体文件、私人连接名或截图。前台原生复验尚未进行；本轮用户在使用电脑，不自行启动预览或操作Windows App。

## 固定身份

- 完整Windows应用审查ZIP：`release/Forterm-Native-0.2.2-dev.7-review-windows-x64.zip`，SHA256 `10cb5d779c029cc668c5889a9dc814628f19ec0c8a8758fdcc79b2465331a248`。
- 主PE：`bd1b10098570d76b88ac9a9cee25574bca94e15ce9dcb2f4498da392f465a9bd`。四AMD64入口与CRC通过，SOURCE-HASHES/源码tar见 `checkpoints/native-0.2.2-dev.7-review-20261028/`。
- Mac arm64离线预览：`native/evidence/ui-0.2.2-dev.7/native/Forterm-Connection-Review-dev7.app`。可执行文件SHA256 `1fd8b4c0e027fb41d9b8f75c71c26eb5d7d8a22573e68d34e5369e2639329f0d`；adhoc签名已核验。
- 预览与源码对应：同目录 `IDENTITY.json`，manifest SHA `d2a65dd4ab5f38bc8896c5ccf14c6559dc4edf2344872fd0a43ecbff230b8a12`，源码tar SHA `61bcd37838806c20fbbb05e89ae4c8353387753791fdb9373017168270216959`。
- dev.5、dev.6和所有旧包保留。dev.6预览有nil凭据服务的已知问题，已在dev.7修复，后续实际视觉预览用dev.7；该问题不在普通应用的系统凭据路径。

## 页面与性能

连接管理是主工作台页：约280dp分组树，可收起、拖宽、键盘调整/持久化，折叠状态保留，搜索不污染折叠偏好。右侧协议筛选、搜索与常用操作；六列表格为系统/名称/地址/状态/备注/操作，虚拟模板复用、列宽适应、横向滚动。未知系统/备注留空；状态由已有会话的本地连接状态取得，不做探测或假延迟。单击选择不认证，双击或行内连接按钮才是明确连接动作；按下鼠标即给出选择与焦点反馈。

堡垒机管理也在主工作台标签中，账户行虚拟化。终端工作区保留多来源文件、终端、三向编辑、资源监控与状态/草稿；所有栏可收起拖宽。无Chromium/Electron/CEF/WebView，新UI不修改已认证transport复用、临时凭据/票据规则。

1000条合成连接在1440×900时130个可复用单元模板，0自动建连/凭据读取。最终一次连接库回调CPU27.609ms、9.966MB、70779alloc（Mac软件driver，不含初次字体装载，不是OS输入到屏幕延迟或Windows结论）。

## 字体与窄窗修订

Fyne的资源字体解析器只支持单字体。UI非等宽文字现在优先读取系统CJK无衬线字体：Mac PingFang SC→Hiragino Sans GB→Heiti；Windows YaHei→SimHei→JhengHei，缺失时回退原Segoe UI/Fyne策略，不假定字体已安装。选中的TTC face仅内存转换为校验后的SFNT，保留glyph/布局表；不下载、安装、写出或分发字体。终端/编辑等宽资源、字宽和栅格代码不变。

当前Mac主regular/bold实际均为PingFang SC。常用中文覆盖、SFNT校验和/边界、深浅主题与150%软件渲染已验证，截图中标题/表头/连接编辑更多均已核对为无衬线。Windows只完成路径与交叉编译，实际Windows字体链仍需现场确认。

窄窗固定右侧“更多”按钮，其菜单有刷新连接列表/新建/导入/导出；软件指针已验证能打开并找到刷新项。横向滚动可到达操作列。实际鼠标/键盘/触控板表现仍待原生复验。

## 当前证据

`native/evidence/ui-0.2.2-dev.7/font-review-final/`：

- `manager-selected-tree.png`：分组引导线、筛选、选中/hover与完整表格。
- `manager-dark-1440.png`、`manager-light-1440.png`：深浅主题。
- `manager-dark-1050.png`、`manager-dark-150percent.png`、`manager-150percent-actions.png`：小窗、canvas真实1.5倍率（700×433逻辑空间约1050×650栅格）、操作列与固定更多。
- `workspace-dark-bottom.png`、`workspace-light-right.png`、`workspace-dark-left-1050.png`、`workspace-dark-150percent.png`：同一合成工作区的四区/三向停靠，未连接、不造指标。
- `font-selection.json`及每张管理页JSON：字体来源/族名、合成边界、模板数、0自动连接/凭据读取；不含字体文件。

以上都是软件栅格截图，不是实际原生前台或Windows截图。

测试日志在 `native/evidence/performance-0.2.2/`：`dev7-font-toolbar-final-race.log`（54.771s）、`dev7-final-input-guard-race.log`（9.635s）、`dev7-ui-regression-race.log`（19.333s）、`dev7-vet-final.log`、`dev7-ui-callback-bench.log`。覆盖字体/边界、主题缩放、菜单、输入焦点、虚拟化/回收动作、预览凭据拒绝、既有关联/停靠/控制器tab与newwin上传回环。

## 独立复核和协调状态

经理已复核本版深色软件图的中文无衬线效果，以及离线隔离/拒绝凭据/静态终端代码边界；预览可执行SHA及source tar/manifest、413当前源码身份核对无差异。原始记录 `native/evidence/ui-0.2.2-dev.7/independent-manager/review.json` 保持原样。

最新消息：经理已经一次询问用户是否方便约3分钟UI预览，尚未答复；没有执行授权或运行结果。原JSON内not requested yet为写入时历史状态，最新状态另记coordination-status.json。程序员本轮只整理证据，未改运行时/包、未重测、未再次询问或占前台。上传仍按用户“稍后再测”，不混入本轮UI复核。

## 经理协调后的原生窗口复核

准备成果已经可运行，本轮未启动。获得新的前台时间后，只启动上述dev.7离线预览一次，按绝对app路径绑定，核对标题 `Forterm UI Review · 0.2.2-dev.7 · PID <实际PID>`；不复用此前上传测试的时间确认。

预览有1000条明确标注的合成配置，使用诊断App和拒绝凭据服务，不经过正常实例IPC，不读真实配置/系统密码，不建连接、不运行本地进程。虚拟终端输出来自内存reader，输入丢弃，不执行命令；监控保持未获取。保存凭据尝试明确失败，已验证不会空指针。

建议一次检查：默认管理页文字/分组/表格→选组与筛选→1050×650及700×433逻辑空间→横滚到行操作→固定更多打开刷新→Tab/方向键焦点与侧栏分隔→深浅切换→菜单“统一工作区”查看三向编辑/文件/监控。无需连接或上传。Mac菜单的700×433是逻辑可用空间，不能代替实际Windows150%缩放验证。

可在启动时为 `FORTERM_UI_PREVIEW_EVIDENCE` 指定本项目证据目录；仅该独立预览每秒记录PID、版本、字体族与布局计数到current.json，以绑定截图和进程，不保存输入或私密数据。截图使用该实际窗口，避免预览到期页或别的实例。结束后关闭预览并归还前台。

原生上传现场原因仍未确认，用户以后正常小文件操作的阶段JSON由经理协调；本轮不生产上传/重登/票据重放。当前未出dev.7正式安装器/便携，先做原生视觉复核与必要现场验证，再冻结正式交付。
