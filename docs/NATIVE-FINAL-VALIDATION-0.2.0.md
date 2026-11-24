# Forterm Native 0.2.0 最终本地验证索引

2026-10-27。唯一源码编辑者在本机macOS arm64完成以下验证；Windows运行不是已通过项。所有协议认证均为合成回环，未使用历史票据/生产SSO/审批接口。

## 最终包身份

最终主PE `29a4bcbc425fdd5bcd204ba065c0982e70ada7eeac5e207b38e0a12d9f30047e`，包含19:03后的左/右/底部停靠、导航宽度和最新关联自动展开。三个实际交付物及各自SHA见release/SHA256SUMS-Native-0.2.0.txt；release/VERIFICATION-Native-0.2.0.json包含主程序/三个入口、包、依赖及便携payload对应关系。checkpoints中的候选不是此次交付物。

## 新原生证据（均在native/evidence）

| 检查 | 结果及证据 |
|---|---|
| 完整原生internal race | release-0.2.0/native-final-race.log通过；后续只改desktop布局/来源关联，相应包另有下列新定向和race |
| 最终布局定向/race | release-0.2.0/layout-final-race.log，desktop/codeedit/settings通过 |
| 文件协议来源关联 | file-session-association-20261027.log、file-session-association-race-20261027.log通过：文件区先收起→选择自动展开、终端对象/所选工作区保持、只隐藏文件协议顶部重复标签、解除恢复且不关闭连接；workspace图共享/循环可达/SSH不隐藏 |
| 最新实际窗口 | file-session-20261027-rerun/native-gui-smoke.json passed及native-live-window.png，实际查看；终端、来源B文件、编辑、监控同窗。真实PTY非回显标记、143精确输入字节、ANSI；认证1/SFTP1、25队列、保存确认取消、dirty关闭取消、监控暂停恢复/协议心跳无注入、来源B一次认证/同名草稿/顶部去重 |
| 左/右/底部停靠与150% | release-0.2.0/final-dock-dpi150/native-gui-smoke.json passed，editorLeftRightBottomSameDraftCaretHistory；同一编辑器/草稿/光标/版本、窄窗改底部且保留left首选、放大恢复。截图已查看 |
| 导航宽度 | layout-native/nav-actual-result.json；实际CUA拖动184→224、Right→232、收起固定恢复232、重启232；nav-restored.png |
| 键盘分隔 | layout-native/actual-keyboard-result.json，实际Shift+Right/Home/End/Enter；新定向覆盖pointer-down及时聚焦 |
| 双终端/颜色 | terminal-split/result.json，两真实shell/隔离输入/两个方向/peer关闭；ansi-dark.png、ansi-light.png实际查看，palette-override.log为OSC/真彩 |
| 多来源生命周期 | multi-source/result.json，共享owner目录隔离、detach后草稿仍保存B、owner关闭保留共享后端、最后引用恢复、B一次认证 |
| 流式队列/文件操作/FTP | stream-cancel-race.log、fileops-race.log、ftp-race.log及ftp-ui/result.json；大于1MiB/25项追加/停滞/取消/不重登、链接/非空并发/特殊位 |
| 编辑安全/代码编辑 | codeedit-race.log、text-race.log与最新GUI；BOM/换行/尾空格、撤销、双冲突、0600初始创建、排他保存、大小/文本限制 |
| 系统凭据/连接库 | system-vault-smoke.json、library-system-vault-gui.json；真实Mac新合成钥匙串与保存profile重新载入/指纹/明确SSH认证一次。settings-race.log、account-vault-race.log |
| TOTP/完整粘贴 | totp-parity-race.log、account-totp-gui.json、clipboard-native-gui.json；经理另11例独立复验；6757字符完整预览/取消0输入/确认原字节/无额外Enter/旧目标拒绝 |
| Agent/跳板/心跳 | agent-heartbeat-race.log、auth-jump-race.log；真实代理/逐跳指纹，默认15秒/设置禁用/仅已开SFTP/不叠加/无stdin/不重认证 |
| 产物 | package-result.json，installer-build.log、installer-archive-test.log、installer-extract.log均在release-0.2.0；最终VERIFICATION记录解包主PE/四入口摘要、ZIP CRC、便携精确payload与无Web依赖。旧0.1.7三包SHA重新核对不变 |

## 保留失败与真实边界

历史evidence/next存在可能回显假阳性，不能证明shell执行；失败副本保留，以新严格原始输入/执行标记为准。此次file-session-20261027首次smoke的迟到shell焦点断言失败；file-session-gui-20261027.log/JSON保留。脚本此前未明确初始化文件区可见这一前提，现已初始化且完整原断言保留、重跑通过；失败时没有面板/焦点对象快照，不能把旧首选断言为唯一根因。另有新增隐藏→关联恢复定向/race。未修改运行时shell聚焦规则以迎合测试。

尚未做的本地组合：未逐一穷举所有文件批选/drop、右键菜单与所有缩放的交叉组合，未作长时间高并发/多窗口压力验收；对应功能有后端边界及实际UI抽样，不能称全部组合穷举通过。没有已知确定的本地测试失败未处理，当前交付用于Windows实机验收。

Windows10/11 x64尚无可用运行环境，ConPTY/实际控制器精确参数与SID命名管道、系统凭据、中文IME/剪贴板/DPI、安装/卸载及真实堡垒机通道权限必须按WINDOWS-ACCEPTANCE-Native-0.2.0现场验证。未签名、未推送发布。厂商门户登录/资产API待脱敏契约；不属于原版已经可用的能力，不猜端点。
