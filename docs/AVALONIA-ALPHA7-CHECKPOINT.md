# Avalonia 0.3.0-alpha.7 检查点（2026-10-30）

FTP与工作区终端生命周期阶段版，完整C#迁移继续，旧包保留。

## 实现与验证

- FTP协议入口、保存协议选择/显式认证测试、独立文件来源/上下传/目录树/UTF-8与BOM文本和另存新名，接入同一工作区。FTP本身不提供SSH终端/exec或SSH跳板，不能绕过配置直连；可明确关联到已有SSH工作区，同时显示终端、FTP文件、文本编辑和SSH资源监控。
- FTP一条已认证控制连接串行，兄弟视图复用认证并引用计数。关闭自动重连/SSL会话轮换/高层自动重试；协议NOOP只在空闲通道，默认继承15秒设置。LIST有4MiB/20000条目边界，原生流式上下传完整SHA/版本复核，完成回执单独检查；226/250或RNTO回执丢失不会伪报成功、重放凭据。字面中文/空格/%20路径回环正确。
- 标准FTP没有通用原子排他/安全替换；已有目标拒绝覆盖，编辑保留原文并另存新名。不声称FTP随机临时项具有SFTP EXCL/0600保证，或新名竞争具有原子排他保证。FTP父目录链接/目录身份复核仍需补强，下一阶段先处理，再用于文件操作；旧SFTP父目录LSTAT守卫保持。
- 修复标签切换、连接库和来源关联重挂载时原生控件解绑外部PTY读写。工作区整个生命周期持有reparent保护，实际关闭显式Dispose；隐藏期间仍接收服务端输出，返回后原PTY输入，认证/shell各1。此项修正alpha.6已补记限制。
- 队列工作移到后台，Dispatcher只负责展示；标签顺序固定。协议/账号端点变化时不得留空沿用旧保存密码；仅名称分组修改或同SSH账号SSH/SFTP视图切换可保留。测试连接同样不会将旧密码发送新端点；切换到私钥时清除旧密码，不错误要求新密码。

最终120项全量47秒通过（avalonia/evidence/alpha7-final-validation.log）。包括FTP上下传/另存/提交与完成ACK不明/取消提交点/NOOP及GUI四区，SFTP/认证/跳板/agent/心跳/安全编辑/来源/保管库原回归，以及保存端点与认证方式切换守卫。全是自有回环/假保管库，未访问真实vault/agent或生产。

26张软件图与尺寸/SHA位于 avalonia/evidence/ui-0.3.0-alpha.7/SCREENSHOTS.json。ssh-ftp-four-areas.png、ftp-connection-test.png已实际查看，四区两来源认证各1；仍不是原生GUI、物理高DPI/IME或Windows运行证明。

## 冻结身份

- sourceFiles: 486
- sourceManifestSHA256: 223084b334fe2c30789b5103957b828bc6919976c2a1ab0a0fdf3162eae71caf
- sourceArchiveSHA256: 345a19573abfdb50d99ebb273d141c7c7c719ff0945acd8088d1de80886bc4d8
- checkpoint: /Users/xianyu/IdeaProjects/forterm/checkpoints/avalonia-0.3.0-alpha.7-20261030
- macApp: /Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.7-macos-arm64.app
- macExecutableSHA256: 0e3c27d1a0719f56782aed9a355021bc9e8be06268d357c6f1bbfbaaa60ee6e9
- windowsPrototypeZIP: /Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.7-prototype-windows-x64.zip
- windowsZIPSHA256: 323cbeca75e24cc8fe9ddcec3175eda4f36e781b89ca531854d1e4f005130630
- windowsMainExeSHA256: 6bf3e8d5146b5f46ffe43f75571fcfdd10f8d4a15819f8403cb6833a180f2f24
- symbolsArchive: /Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.7-symbols.zip
- symbolsSHA256: fb1bca0e459f4816aa1c7737d58691cbe3e87413c2e369c5007adbf0e0ba5b71

Win ZIP 52,300,331字节，展开121,701,865字节；Mac125,331,969字节。正常包无PDB，symbols另存。四AMD64/ZIP CRC/签名/486源码逐项一致；PAYLOADS/SIZE/SHA256SUMS在release。新增FluentFTP55.1.0纯托管MIT，锁文件/源码提交与许可保留；240PE完整常规/延迟导入、18个Mac原生依赖未见未解析项或VC外部导入。Win10联合icu.dll仍有1903+边界，旧1809不保证。包后Mac只做无窗口runtime-check和合成xftp诊断，无真实凭据访问。

## 继续与待验

优先补强FTP父目录链接守卫、SFTP拒REALPATH后明确绝对目录恢复，再文件复制移动/权限sudo、TOTP及布局快捷键完整清单。最终安装器/便携版未出；原生GUI、系统picker、物理输入/IME/150%、真实vault、Windows和生产仍待经理协调，不抢前台、不重跑历史Go上传。
