# Native 0.2.1 验证说明

环境：macOS arm64、Go 1.26、Fyne 2.8.2；Windows x64由Clang/MinGW-w64交叉构建。测试仅新建合成本机SSH/SFTP/FTP服务、临时目录和新合成OS-vault项目，不读取真实票据/系统已有凭据、不访问生产SSO或审批。

## 原生新证据

- `native-integration-race.log`：新codeedit/desktop/fileops/FTP/IPC/launch/metrics/settings/SSH/transfers/VT/workspace均通过；历史0.0.1 termprobe最初有测试就绪轮询竞争，保留失败记录，随后`legacy-probe-readiness-race.log`以首个读取边界同步修复并通过，不删断言。
- `agent-heartbeat-race.log`：实际本机认证代理socket、5秒可设心跳、仅已打开SFTP只读探测、禁用零请求、停止清理、无终端stdin/无额外认证。
- `auth-jump-race.log`：实际加密私钥、direct-tcpip跳板链与每跳指纹确认。
- `settings-race.log` / `totp-parity-race.log` / `account-vault-race.log`：秘密不写JSON/导出、导入新ID、配置失败凭据回滚、严格旧版TOTP/RFC6238三算法/前导零/5–300边界及账户规范seed回读。
- `system-vault-smoke.json`：Mac真实系统钥匙串新合成条目Set/Get/Delete；`library-system-vault-gui.json`：保存配置重载、已记住指纹、从OS凭据明确发起真实SSH一次认证。`account-totp-gui.json`：公开合成seed实际验证码显示/复制/关闭。Windows凭据未实际运行。
- `clipboard-native-gui.json`：完整6757字符原生只读预览、取消零输入、确认原字节/不追加Enter、工作区切换阻止旧目标、大小/控制字符边界；VT单元另覆盖bracketed paste。
- `stream-cancel-race.log` / `fileops-race.log`：大于1MiB真实流式回环、25项队列/追加/串行/取消、停滞与元数据超时保持SSH、取消下载保留原文件/清理临时文件、FTP不重登、目录边界/链接/并发非空/特殊位。
- `multi-source/result.json`：owner+source目录隔离、解除后草稿仍保存到B、owner关闭保留共享后端、最后引用恢复来源顶栏，B认证一次。
- `terminal-split/result.json`：两真实shell/输入隔离、两方向输出保留、关闭peer保持first；主题切换不重新认证/不丢输出；实际执行ANSI16色板深浅截图与真彩覆盖。`palette-override.log`验证真彩和OSC自定义色不被默认主题重写。
- `layout-native/actual-keyboard-result.json`：实际CUA指针按下后Shift+Right 280→312、Home170、End480、Return280，含活模型快照及原始截图。
- `screenshots/redesign-20261027/review2/`：用户认可的第一轮深浅真实窗口、CUA来源/菜单/拖动/主题操作及二进制摘要。新高缩放组合在`dpi150/`，其首次窄窗截图发现中央偏窄，已调整侧栏压缩优先中央，补测记录以最新结果为准。

## 终端旧检查更正

旧evidence/next截图曾出现5printf，旧contains检查可能匹配回显，不能证明shell执行。失败副本完整保留。新严格脚本使用非回显随机标记、服务端精确字节/SHA、真实PTY ANSI及终端区域颜色；切换编辑/文件/监控后再执行第二命令，断言无字符串到终端；连接前按键不排队、迟到shell不夺焦点。

Windows原生运行仍待：ConPTY、SID管道、剪贴板/IME/DPI、系统凭据、安装卸载及生产控制器/权限。交叉构建和PE/ZIP/SHA检查不等同Windows现场。厂商门户API仍等待脱敏契约，原版也未接通，不伪称完整门户适配。

最新布局验证：`release-0.2.0/final-dock-dpi150/native-gui-smoke.json` passed，包含三方向同一草稿/光标/撤销历史与窄窗首选恢复；`layout-native/nav-actual-result.json` 为实际导航拖动/键盘/收起恢复/重启宽度。`file-session-association-20261027.log` 验证文件区曾收起后显式关联会自动展开，当前终端保持，仅所选文件协议顶部重复标签隐藏，解除后恢复。


## 0.2.1 小高度阻断修复

0.2.0的780×620逻辑@150%图并不能覆盖1050×650物理窗口。经理随后在后者发现文件/代码正文不可达以及队列向上覆盖，0.2.0保留但不推荐使用。

0.2.1新增 `small-layout-0.2.1-directed.log` / `small-layout-0.2.1-race.log`：短窗口不将文件列表/队列压成负高度，滚动内容保持真实范围。`small-0.2.1/actual-cua-result.json` 与CUA jpg：实际1050×650@150%时文件行/代码正文可见；来源A27文件/25完成队列，真实轮滚/点击选择末文件、展开并滚到batch-24、打开最后项详情；代码键盘导航/粘贴/工具栏撤销，来源切换保持，实际拖大至1453×954后仍可用。不是程序强制滚根容器或只测矩形。Windows系统DPI/输入法仍待实机。

0.2.1最终全模块race：`release-0.2.1/native-final-race.log` 全internal通过；`regular-0.2.1/native-gui-smoke.json` 常规浅色1150×760逻辑窗口组合通过，包含三方向草稿/光标/历史保持、精确shell输入与同transport四能力。small实际CUA的普通字母模拟未作为通过证据（Mac输入法/合成键事件有未确认行为）；代码正文可达由实际鼠标、Down/End、剪贴板粘贴与撤销证明。真实Windows键盘/IME仍按现场清单验证，不能以剪贴板测试代替全部文本输入路径。

后续输入对照补测：`input-probe-0.2.1/result.json`、`committed-state.json` 和 `committed-standard-and-code.jpg`。标准Entry实际按a/b/c后空格提交ABC，x/y/z后回车提交xyz；代码区d/e/f后空格提交“得分”，x/y/z后回车提交xyz。此前没有立即显示普通字母为输入法组合等待，不能写成字符丢失；候选/预编辑显示和提交回车顺序未完整验收，Windows实际IME仍待。对照探针不编入生产主程序，未改运行时。
