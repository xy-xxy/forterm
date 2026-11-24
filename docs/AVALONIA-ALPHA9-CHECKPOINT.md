# Avalonia 0.3.0-alpha.9 检查点（2026-10-30）

收敛经理指出的大文件复制与SFTP覆盖对等缺口。当前仍未完成权限sudo、TOTP、布局快捷键与最终三包。

SFTP及独立来源SFTP/FTP采用32KiB缓冲流式复制，不整文件入RAM、不落本地明文中转，不新增认证连接。源与目标资源按稳定ID有序预留、去重并逆序释放；FTP兄弟视图共享物理控制连接资源，后台上传/下载/编辑也单源预留。FTP源EOF后显式确认完成ACK再释放控制通道并复核源。保留长度/身份/SHA、目标临时项校验、源读后变化停止、取消保旧和回执不明核对。

仅同一FTP控制连接复制限16MiB且缓冲用后清零；同资源移动使用已核对改名，可移动大文件。SFTP已有目标提供明确确认，固定目标原版本，服务端posix-rename支持时才原子替换；复制保留旧目标权限，原地移动保留源权限。FTP未确认安全替换仍拒绝。移动只在目标提交确认后复核/删除源，部分失败不重试。

最终145项54秒通过（avalonia/evidence/alpha9-final-validation.log）。实际17,825,806字节四种独立SFTP/FTP来源组合，单FTP大复制拒绝/大移动、SFTP大移动覆盖、保权限/拒绝/目标变化/取消保旧、源读后同大小内容变化以及双向并行预留和GUI覆盖确认均通过，认证计数保持1。30张headless软件图/尺寸/SHA在avalonia/evidence/ui-0.3.0-alpha.9/SCREENSHOTS.json，覆盖确认图已实际查看；不等于原生GUI、Windows或生产验证。

## 冻结身份

- sourceFiles: 502
- sourceManifestSHA256: d4f2121c55eed14a8fe82867395d069eb29f8a6e587ffef80a10587ffa384532
- sourceArchiveSHA256: 0ab3fcf435cba30a9662bc088c8883ccffd92885e91dbe822967419e73a40d01
- checkpoint: /Users/xianyu/IdeaProjects/forterm/checkpoints/avalonia-0.3.0-alpha.9-20261030
- macApp: /Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.9-macos-arm64.app
- macExecutableSHA256: e3e49859d9feffc821e84cb817029d19718175ef078eb97974edeb4f76ba37cd
- windowsPrototypeZIP: /Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.9-prototype-windows-x64.zip
- windowsZIPSHA256: a1e257c3b64597f834c3556270fc288c3c4abbd454764cf80f2a0205c5b9364a
- windowsMainExeSHA256: c7a0fea2aa9dc2b12522cd3786bcd55535dd93acccdee69eb3bfde30964364b5
- symbolsArchive: /Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.9-symbols.zip
- symbolsSHA256: ae19e3dee6c7994d87ec3879a739043068f37cc4c0c1ea321de2874595bd6463

Win ZIP52,338,393字节、展开121,800,402字节；Mac125,427,713字节。正常包无PDB，symbols另存。四AMD64/ZIP CRC/签名/502源码一致，PAYLOADS/SIZE/SHA256SUMS在release。240PE双表与18个Mac原生依赖无未解析/VC外部导入，icu.dll Windows10 1903+边界保留。打包后Mac只无窗口runtime-check/合成xftp诊断，无真实凭据访问。

下一阶段权限递归/sudo（仅已装Python3及既有exec能力，不安装、不向交互终端输入）、TOTP、布局快捷键与最终Windows安装器/portable。原生GUI/IME/系统picker/真实vault/Windows/生产仍待外部协调；旧包不动，不抢前台或消息其他聊天。
