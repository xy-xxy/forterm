# Avalonia 0.3.0-alpha.10 检查点（2026-10-30）

权限预览、递归计划与安全sudo阶段版，完整迁移仍待TOTP、布局快捷键和最终三包。

只读预览最多2000项/32层、60秒，子项先于目录，普通位000–777配9个rwx勾选，特殊位逐项保留；递归文件权限留空保留，链接/特殊节点跳过。应用前逐项复核，变化/取消停止，报告确实完成数，不回滚已完成。SFTP普通文件用已打开句柄FSTAT/FSETSTAT；目录用原路径LSTAT/SETSTAT（SFTP无inode/CAS，不能声称原子排他）；FTP只在权限已知且SITE HELP确认CHMOD时支持。队列计数单位为项。

sudo只复用原SSH独立exec，不向交互终端写命令，不安装agent/Python。既有/usr/bin/python3、安全dir_fd/O_NOFOLLOW、sudo策略及用户明确确认路径空间一致是前提。预览将exec device/inode等身份与SFTP元数据核对；固定root，随机本次提示后才询问一次本次密码，拒绝通用提示/自动使用登录密码，stdin发送后清零。fd助手再次核对device/inode/size/mode/mtime/type后fchmod，链接替换拒绝。exec被拒绝保留文件与终端。

最终154项全量74秒通过（avalonia/evidence/alpha10-final-validation.log），含普通权限/特殊位/变化/部分取消、FTP、原SSH随机拆分提示/清零/通用提示不索取/链接替换保护/拒exec，以及UI预览取消/确认/参数过期/小窗/身份确认。全部在自有临时目录及合成SSH中执行，sudo由夹具模拟root校验，未执行本机系统sudo、真实vault/agent或生产操作。33张headless软件图及尺寸/SHA在avalonia/evidence/ui-0.3.0-alpha.10/SCREENSHOTS.json，已查看权限预览/小窗/sudo结果，不是原生GUI/Windows证明。

## 冻结身份

- sourceFiles: 512
- sourceManifestSHA256: 586aca505ebb9c74cf037c49d85412f6fe484b0a0b70dc9652d435dd38d175a7
- sourceArchiveSHA256: 49b6b784d268b62a7fdd3a98692386268eac72e84515eac3cd915c1335c8c453
- checkpoint: /Users/xianyu/IdeaProjects/forterm/checkpoints/avalonia-0.3.0-alpha.10-20261030
- macApp: /Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.10-macos-arm64.app
- macExecutableSHA256: 2b7b40a29d5a8758e592609a9e0f8865017e22cb571087e62abee4a4c5c26bb6
- windowsPrototypeZIP: /Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.10-prototype-windows-x64.zip
- windowsZIPSHA256: 284b0d334793f9b3dd4d60303d83f6354fda8b738437bb297b9b8f4cb0bcb20a
- windowsMainExeSHA256: b3439ba9dec6b54774292dbf54ba4db202eebedeaa0407e36bcb5190690e83a4
- symbolsArchive: /Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.10-symbols.zip
- symbolsSHA256: 62a6c471b591bae9b4b267573d1b10bc4ed5b10083971c46cd756068dfd601d2

Win ZIP52,363,626字节，展开121,863,793字节；Mac125,489,688字节。正常无PDB，symbols另存，DLL/许可完整。四AMD64/ZIP CRC/签名/512源码一致；PAYLOADS/SIZE/SHA256SUMS在release。240PE双表/18个Mac原生依赖无未解析/VC外部导入；icu.dll Win10 1903+边界保留。包后Mac只无窗口runtime-check/合成xftp诊断，无真实凭据访问。

继续TOTP账户安全存储/显隐复制与导出边界、布局快捷键和最终Windows ZIP/安装器/portable；原生GUI/IME/系统picker/真实vault/Windows/生产仍需外部协调，不抢前台。旧包不变，不向其他聊天发信。
