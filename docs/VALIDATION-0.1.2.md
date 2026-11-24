# Forterm 0.1.2 验证记录

开发 / 验证环境：macOS arm64，捆绑 Node.js 24，Electron 45。所有 SSH/SFTP 测试使用临时目录、回环地址和合成票据；没有连接生产堡垒机，没有使用真实认证信息，没有推送、发布或上传产物。

## 已通过

| 验证 | 证据 | 范围 |
| --- | --- | --- |
| TypeScript | `test-results/typecheck-0.1.2.log` | 主进程、IPC、renderer、测试类型检查 |
| 单元 / 回环协议 32 项 | `test-results/unit-0.1.2.log` | 启动解析与脱敏、去重、认证、指纹、跳板、SSH/SFTP、加密存储、保活与编辑边界 |
| 最终构建 | `test-results/build-0.1.2.log` | 主进程、preload、React/xterm 产物 |
| 原桌面 smoke | `test-results/ui-0.1.2.log` | 中文页面、连接保存、本地 PTY、脱敏诊断、Xftp 模式、新窗口与会话删除同步 |
| 新工作区桌面 smoke | `test-results/workspace-0.1.2.log` | 设置与外观重启恢复、快捷键、文件筛选/排序/导航、四能力一次认证、显式保存、冲突、编辑器/会话/窗口关闭取消、阻止未保存重载、监控隐藏停刷、断线草稿、分屏标签焦点/搜索输入隔离与最小窗口 |
| 11 进程 burst | `test-results/burst-0.1.2.log` | 11 次诊断一份报告计数 11 零连接，11 次 SSH 一会话一连接，不同票据独立 |

最终权限修订后重新运行完整 32 项协议测试、类型检查、构建和工作区 UI，全部通过。分屏输入焦点随后补充了标签切换与搜索框隔离验证，类型 / 构建 / 工作区 UI 再次通过。原桌面及 burst 测试此前通过；最后修订不改变其入口或认证路径。

## 协议边界证据

- 保活：真实回环服务收到 SSH `keepalive@openssh.com`；未打开 SFTP 时没有主动创建文件通道；打开后只读 realpath 探测复用同一通道；关闭后请求停止；shell stdin 始终为空。关闭总保活或仅关闭 SFTP 探测时，没有后台 SFTP 查询。
- 调度器：虚拟时钟测试在途上限为一个；无响应超时停止，迟到响应不恢复堆积；错误响应仅等待下个周期，不立即重试；stop 可重复调用。
- 同会话：Xftp 接入后并行 shell、文件列表、在线编辑、监控，服务端记录一次认证、一个 shell、一个 SFTP 通道。
- 拒绝：shell / exec 拒绝时 SFTP 可读；写权限拒绝和替换失败保留原文件；SFTP 拒绝时终端与监控仍可用，无重新认证。
- 编辑：1 MiB 上限、无效 UTF-8、二进制和符号链接拒绝；UTF-8 BOM / CRLF 保留；打开后远程变化、写临时文件期间远程变化均触发冲突；临时文件清理；临时文件先以 0600 创建，写入后显式恢复原普通权限位，避免服务器 umask 导致原 0664 变成 0644；权限恢复被拒绝时不替换原文件。
- 下载取消：原本地文件保持原内容，`.part` 文件清理，清理队列保留运行任务。
- 桌面关闭确认测试对隔离测试进程的 Electron 原生消息框返回值进行注入，验证主进程取消分支和草稿保留；编辑器确认通过真实 renderer dialog 取消。测试未代替 Windows 原生消息框人工验收。

## 界面截图

均为真实 Electron 渲染、本机合成服务数据，无设计稿替代：

- `test-results/welcome.png` / `welcome-light.png`：深色 / 浅色工作区。
- `test-results/heartbeat-settings.png`：心跳设置。
- `test-results/unified-workspace.png`：终端、文件、监控、编辑器同时显示。
- `test-results/unified-workspace-compact.png`：1050×650 最小窗口，可滚动，无文档横向溢出。
- `test-results/split-workspace.png`：双终端左右分屏。
- `test-results/editor-disconnected-draft.png`：断线和冲突时保留草稿。
- `test-results/diagnosis.png` / `burst-diagnosis.png`：脱敏和重复调用合并。

截图已人工逐张检查主要工作区与断线状态；修复了最小窗口下文件 / 监控区域重叠，浅色侧栏低对比和监控错误被紧凑布局隐藏的问题。

## Windows 包验证

`scripts/verify-release.mjs` 已通过并生成 `release/VERIFICATION-0.1.2.json` 和 `SHA256SUMS-0.1.2.txt`。ZIP 含 438 个文件，CRC 检查通过；四个入口与 app.asar 在 ZIP 内和解包目录中摘要一致；四个入口均为 x64 PE；包内版本为 0.1.2；6 个 dist 代码 / 样式 / sourcemap 文件与当前构建逐字节一致；Windows ConPTY 预编译模块存在。日志见 `test-results/release-verification-0.1.2.log`。

## 尚需现场验收

- Windows 10/11 上安装、便携启动、ConPTY、快捷键、文件选择器、关闭确认、系统加密；本机仅可交叉构建和静态检查 Windows 产物。
- 真实堡垒机的一次性认证、独立 shell/SFTP/exec 通道授权及并发限制。
- 实际服务器 OpenSSH 原子 rename 扩展、所有者 / ACL 等文件策略。回环测试覆盖标准 SFTP rename 成功与拒绝；不等价于所有服务器的扩展支持验证。
- 长时间空闲、休眠恢复和服务器强制时限；不声称心跳保证不断线。

无代码签名；没有运行远程 CI。Windows CI 已加入 packaged workspace smoke，需用户以后自行推送触发。
