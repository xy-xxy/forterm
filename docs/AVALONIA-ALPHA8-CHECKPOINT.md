# Avalonia 0.3.0-alpha.8 检查点（2026-10-30）

路径恢复、文件复制移动与目录操作阶段版，完整迁移仍未完成。

## 实际行为与测试

- FTP目录逐级LIST复核父节点，拒绝链接、重复或未知节点，不静默穿过链接。
- SFTP初始化REALPATH仅在明确PermissionDenied/OperationUnsupported时保留已认证文件通道，当前目录记为未知，不猜根目录、不发文件探测。用户明确确认已知绝对路径后，LSTAT/LIST成功才允许上传并建立探测锚。取消不读写、不重新认证；超时/其他错误仍失败该通道，终端保留。
- 文件区私有复制/剪切/粘贴、Ctrl/⌘C/X/V（列表聚焦）、新建目录、普通文件/目录改名、确认删除普通文件或空目录。源/目标与版本固定；移动在目标提交确认后再核对并删除源。源变化/删除未明明确部分移动，不自动重试。SFTP新目录原子请求0700，FTP遵循服务器权限策略；非空目录拒绝删除，无递归删除/目录复制。
- 本阶段复制全部16MiB内存、同名目标一律拒绝，只是阶段限制，不能当旧Go功能对等。旧Go仅同FTP控制连接复制受16MiB限制；后续必须恢复SFTP/独立跨源32KiB有界流式、有序资源预留、>16MiB实测和SFTP明确安全覆盖，FTP无原子保证时拒绝。详 docs/AVALONIA-STREAMING-COPY-PLAN.md。

最终单次134项50秒通过，日志avalonia/evidence/alpha8-final-validation.log。四种SFTP/FTP复制移动、源变化保留、同名/关闭源保护、目录CRUD与非空拒绝、GUI剪贴板等待时切换仍写原目标、目录取消不删除、REALPATH恢复和FTP父链接等通过。29张软件图及尺寸/SHA在avalonia/evidence/ui-0.3.0-alpha.8/SCREENSHOTS.json，均自有回环，不是原生GUI/Windows/生产验证。

验证过程修正两个夹具问题：两次旧运行重叠写同一日志，原稿保留且不采用；随后串行仍出现dead channel，定位Paramiko接收通道缺强引用，现持有至关闭，失败项定向3项及最终单次全量通过。没有放宽应用超时。

## 冻结身份

- sourceFiles: 496
- sourceManifestSHA256: 600f68c267903a38428c173ad6d5ce064679454b69e0e4ac4e82c460b0ed6e97
- sourceArchiveSHA256: 7e99332436e9fdb27dedaf39f5333b9373bfdba54606a467a50615a65b2144f4
- checkpoint: /Users/xianyu/IdeaProjects/forterm/checkpoints/avalonia-0.3.0-alpha.8-20261030
- macApp: /Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.8-macos-arm64.app
- macExecutableSHA256: c57e1e768110b42f843214ba8c2d9bfc259760098d4517471ca126dd62fc9b48
- windowsPrototypeZIP: /Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.8-prototype-windows-x64.zip
- windowsZIPSHA256: 1a2cb366a7d66ea9899b151f7d5948299263b7f4807f48fe950d5161a42771fb
- windowsMainExeSHA256: 21e917a6895246ab356801292914b44f40f145fab8ee8bcef8340bdd30285b7e
- symbolsArchive: /Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.8-symbols.zip
- symbolsSHA256: a62273dc3b4a7f1cc79c1317b621517f85c2747bba35a1c0a9f29c351bd98ad8

Win ZIP52,324,608字节、展开121,766,265字节；Mac125,394,433字节。正常包无PDB，symbols另存，DLL/许可完整。四AMD64/ZIP CRC/签名/496源码逐项一致；PAYLOADS/SIZE/SHA256SUMS在release。240PE常规/延迟导入、18个Mac原生依赖无未解析项或VC外部导入，icu.dll Win10 1903+边界保留。包后Mac仅无窗口runtime-check/合成xftp诊断，无真实凭据访问。

## 下一阶段

alpha.9先恢复大文件流式与SFTP安全覆盖选择，保留取消/身份/SHA/有序预留/无额外票据登录。随后权限递归/sudo、TOTP、布局快捷键及最终Windows安装器/portable。原生GUI/IME/系统picker/真实vault/Windows及生产现场仍需外部协调；不抢前台或重做旧Go上传。旧包保持。
