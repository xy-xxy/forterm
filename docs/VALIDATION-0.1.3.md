# Forterm 0.1.3 验证记录

环境：macOS arm64、捆绑 Node.js 24、Electron 45。所有协议服务均为本机回环与合成凭据，目录为自动清理的临时目录。未连接生产堡垒机、读取真实票据、推送或上传发布。

## 自动化证据

| 检查 | 结果与证据 |
| --- | --- |
| TypeScript | 通过；`test-results/typecheck-0.1.3.log` |
| 单元 / 协议 34 项 | 全部通过；`test-results/unit-0.1.3.log`。包括心跳设置、真实保活/SFTP 探测、调度清理、四能力并行、认证/指纹/跳板、通道降级、编辑冲突/权限/编码/大小、下载取消、布局输入恢复与收缩约束 |
| 构建 | 通过；`test-results/build-0.1.3.log` |
| 原桌面 smoke | 通过；`test-results/ui-0.1.3.log`。本地 PTY、保存连接、脱敏诊断、Xftp 入口、新窗口与共享移除 |
| 统一工作区 smoke | 通过；`test-results/workspace-0.1.3.log`。四能力一次认证、编辑保存/冲突、窗口和会话关闭取消、未保存重载阻止、断线草稿、分屏输入隔离 |
| 布局实际指针 smoke | 通过；`test-results/layout-0.1.3.log`，详见下方 |
| 11 进程 burst | 通过；`test-results/burst-0.1.3.log`。11 次诊断一份计数 11 报告、零网络；11 次 SSH 一会话/一连接；不同票据独立 |

最后增加窗口级指针清理与布局测试的原生窗口稳定等待后，重新构建、类型检查并执行完整实际指针测试。协议实现未改动，未为无变化重复执行协议测试。桌面脚本对本轮关键交互均有覆盖。

## 实际拖动覆盖

`npm run test:layout` 启动独立 Electron 和回环 SSH/SFTP 服务：

- 使用 Playwright 的真实 pointer/mouse 输入拖动五类分隔条（包含两种终端方向），在分隔条外继续移动和松手，断言尺寸/比例随指针改变且松手状态清理。
- 使用方向键、Enter、双击和全局重置；拖到极端位置后保持终端/编辑器正尺寸，检查上限/下限。
- Pointer cancel 用合成取消事件触发，随后再次真实拖动；不是触摸屏设备现场验证。
- 重启应用检查尺寸恢复，并清空旧存储后立即正常关闭，验证关闭时写入当前布局快照。
- 用服务端 SSH `window-change` 记录验证实际 PTY 尺寸变化，拖动不向 shell stdin 写入数据。
- 将 shell 通道响应延迟 250ms，验证打开后、任何拖动前，服务端已经收到首次尺寸通知。
- 输入未保存草稿后拖动、收起/展开编辑器、切换/隐藏/恢复面板，验证草稿与编辑焦点、原高度保留。
- 同一 SSH transport 打开终端、文件、编辑和监控，服务端只进行一次认证和一次 SFTP 通道开启。
- 通过隔离测试进程的原生文件选择框注入合成上传文件，实际执行 SFTP 上传并展开/清理传输队列；队列变化不使其他区域溢出，编辑文件未被保存。
- 用户开启自动监控，隐藏面板等待超过 10 秒，验证没有新增 exec 请求；展开后恢复，开关保留。
- 1050×650 原生窗口、125% 页面缩放与缩放后的实际拖动，断言所有可见区域正尺寸、位于视口内，无文档水平溢出。收缩不覆盖偏好布局。

测试曾捕获首屏尚未完成尺寸测量时，裸坐标输入可能错过移动中的分隔条。脚本现先显示/聚焦原生窗口并等待分隔条 actionability/stability，再发真实指针事件；同时组件增加窗口捕获阶段松手与取消清理、无按钮移动恢复。

## 真实截图

- `test-results/layout-workspace-0.1.3.png`：四能力与编辑器收起入口。
- `test-results/layout-split-0.1.3.png`：可调整比例的左右双终端。
- `test-results/layout-transfers-0.1.3.png`：实际上传后展开传输队列。
- `test-results/layout-compact-0.1.3.png`：1050×650 窗口。
- `test-results/layout-zoom-0.1.3.png`：125% 页面缩放；使用 BrowserWindow.capturePage 获取完整原生内容，避免 Electron/Playwright 在页面缩放下按 CSS 尺寸截取导致图片裁切。

已检查上述渲染截图。小窗口为终端和编辑器保留空间，文件/监控各自内部滚动；需要更大的文件区域时可拖动或收起编辑器。

## 发行包与现场边界

最终 Windows 包已在 UI 回归后生成；由 `scripts/verify-release.mjs` 核对 CRC、ZIP 与解包四入口/asar 摘要、x64 PE、版本、dist 与 asar 逐字节一致以及 ConPTY 模块，已全部通过，生成 SHA256 和 `release/VERIFICATION-0.1.3.json`。ZIP 包含 438 个条目，四个入口均为 x64，ZIP/解包内容一致，6 个 dist 文件与 asar 一致。日志见 `test-results/release-verification-0.1.3.log`。

Windows 原生拖动/缩放/ConPTY、触摸/笔输入、真实堡垒机通道权限和内置登录仍需现场验证。Windows CI 已加入 packaged layout smoke，但本轮没有推送或远程运行。新增登录/资产对接边界见 `docs/BASTION-INTEGRATION.md`。
