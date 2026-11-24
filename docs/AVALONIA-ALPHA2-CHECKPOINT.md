# C# + Avalonia alpha.2 兼容入口与认证检查点

2026-10-30；继alpha.1原生终端/SFTP纵向原型后，继续实现原有堡垒机入口与认证边界。此检查点已冻结，完整迁移未完成。原Electron/native包、alpha.1包、用户运行窗口均未修改/切换，未读真实凭据、未连接192.168.2.2/生产或SSO。

## 本阶段实际完成

- C#正常主程序与xshell/xftp/forterm-diagnose四入口。wrapper使用ProcessStartInfo.ArgumentList，不拼接命令行、不转小写参数值、不重复解码。核心parser仅选项/协议大小写不敏感，保留标题、用户名、密码、私钥路径；URL值一次百分号解码，空密码和缺密码区分，端口/路径/查询/畸形转义拒绝。FTP解析后明确未迁移，不假装SSH或消耗票据。
- 当前用户NamedPipe有界帧/ACK（1MiB、128参数、每项32768），内存消息无argv/endpoint文件；本机Mac临时目录socket长度限制被实际测试发现并用短哈希pipe名修正。主实例用三秒HMAC内存gate接纳11次burst中的第一次，保留不同票据/显式新窗/空与缺密码区别；无自动重连/自动重放。新窗口、实际跨进程GUI交付仍需现场。
- 启动临时连接不存连接库；缺密码时先询问，取消前没有TCP/认证；显式空密码不被当成缺密码。xftp非FTP改成SFTP文件工作区，不开空白终端通道。诊断路径只输出脱敏结构，不启动GUI或网络。
- keyboard-interactive支持多轮、有回显/无回显问题、16轮/16问题/1024文本边界、UI取消/45秒总认证边界。正常手动连接仅服务端单一首轮密码问题可使用当前输入一次；临时票据任何追加问题均明确输入。密码已提交后的追加验证和再次要求密码不自动重用旧值。输入只在内存，字节数组用后清理；16问题最后一项可达、取消清除UI字段。
- 私钥core API/显式-i支持SSH.NET格式，未加密/加密owned密钥回环通过；加密口令UI/agent/跳板尚未完成。测试确认公钥握手有unsigned probe+signed两次检查、一条TCP且零password requests，不误把两次key检查当两次登录。
- 同一transport rekey缓存本次已明确核对的指纹，仅接受同指纹；不复用已取消的认证token去重新弹指纹。不持久化信任，known_hosts迁移待。
- 输出队列128×32KiB有界。溢出在接收线程外关闭该PTY，避免阻塞整个SSH消息循环；owned 6MiB flood验证只终端通道退出，SFTP仍可写、连接数仍1。
- 所有运行时代码为C# + Avalonia，不借Go后台。现有终端/SFTP/原生StorageProvider/版本化元数据与深浅卡片工作区保留；正常认证“测试连接”也接多轮问题。元数据为独立Forterm/AvaloniaAlpha2；可用FORTERM_AVALONIA_DATA明确指定隔离路径，该值也参与IPC名字，不自动迁移旧配置。

## 本工程验证

31项全部通过5.9041s，日志avalonia/evidence/alpha2-versioned-evidence-tests.log。覆盖原9项VT/双PTY+SFTP/一条TCP认证/1MiB+13逐字节/拒通道/ownedUnixPTY，新增parser/去重/真实当前用户pipe转交、多轮/追加/取消/重复密码/owned加密私钥/rekey/反压隔离以及16问题UI。纯软件/headless与真实owned回环，未使用旧Go测试替代。

Mac实际执行self-contained xshell/xftp主转交的诊断路径（当前存证xftp）及独立诊断入口，redacted JSON在alpha2-mac-xftp-diagnostic.json和alpha2-mac-diagnose.json，recognized=true/networkRequested=false，未见任何地址/用户/密码值。打包app再次运行--runtime-check，Version=0.3.0-alpha.2/Arm64/Skia32/HarfBuzz9/VisibleWindowsCreated0，且DOTNET_ROOT指向不存在目录。它不代替正常GUI首帧。

9张最终软件PNG改按程序集版本写入avalonia/evidence/ui-0.3.0-alpha.2/，SCREENSHOTS.json有尺寸/SHA；已看深浅/窄窗/真实VT和认证末项。早期ui-alpha1硬编码目录被后续测试复用，不能再用其当前内容单独证明冻结alpha1；已在alpha1报告补记并留发现时副本。后续按新版本目录绑定证据。未伪称原生截图/物理150%/系统picker已通过。

## 冻结可运行产物

- Mac：release/Forterm-Avalonia-0.3.0-alpha.2-macos-arm64.app；arm64/adhoc验证通过，未启动正常GUI。apphost SHA `175a0e0b7c454d26a2935f354ff3c6bd255594cb27b45a93d03152a50272d1f8`。
- Windows四入口self-contained ZIP：release/Forterm-Avalonia-0.3.0-alpha.2-prototype-windows-x64.zip；CRC/四个AMD64入口通过，SHA `ebc3f4384ac3594410e09cba92171d3453219a601c26929dd0baf4c1d55a07d2`。Windows未运行。
- .NET apphost可以在两应用版本间字节相同，不能单凭Forterm.exe判断版本。实际Windows Forterm.dll SHA `6b11830361ae59a9adc4a95cd18dd162ef92895be8bc62e530dc0a3771228d0a`；Core SHA `d111997755cfa15d2afffbeaaf61c12920f2068cfe47bf40a284a6ab247dcb9c`。全payload与主程序/四入口摘要另见release/PAYLOADS-Avalonia-0.3.0-alpha.2.json，与ZIP和Mac包实际文件匹配。
- 430份源码/锁文件/许可证/工具源码全部与tar匹配，且交付时当前源码再次匹配manifest。检查点checkpoints/avalonia-0.3.0-alpha.2-20261030/；manifest SHA `5b5772119a5694741a7cdc6168ab915d60ab13e199936d6da73141eac6db1439`，tar SHA `f1a4df56832b55282995cce8e5feb83944a0c42957c569346120ca03104a7263`。
- release/IDENTITY-Avalonia-0.3.0-alpha.2.json、SHA256SUMS-Avalonia-0.3.0-alpha.2.txt。

## 依赖与尚未完成

最新递归237 PE import（含OpenConsole子目录与四入口）无vcruntime/msvcp或未解析静态项，均随包/.NET self-contained/已识别Windows系统或API set；Mac18个load-command报告见avalonia/evidence/native-imports.json。所有包内许可/第三方notice和官方上游许可随包。动态PInvoke/系统版本/干净Windows实际运行仍待，不能宣称零系统依赖或所有环境不用额外组件；未复制System32 DLL、未要求用户装VC/.NET。Win目标10/11x64（ConPTY最低1809），Mac目标14+。

完整矩阵avalonia/docs/MIGRATION.md仍有明确缺口：agent/跳板/系统vault/known_hosts、可配置心跳与完整超时、同会话UI克隆与导航折叠、文件懒树多来源/安全下载覆盖/复制移动权限sudo、FTP、远程文本编辑/冲突/三向分屏、真实监控四区与逐核趋势、完整串行传输队列/偏好持久等。没有假监控数据或未实现开关。加密私钥core通过不等于用户入口已有口令UI。

经理可协调当前独立C#候选实际Mac窗口/原生picker与物理缩放验证；程序员没有切换任何用户窗口。Windows实跑/完整新安装器/portable/生产验证均未完成，旧dev3上传现场失败与门户契约缺口继续保留。完整重写继续，不能以此原型当最终全部交付。
