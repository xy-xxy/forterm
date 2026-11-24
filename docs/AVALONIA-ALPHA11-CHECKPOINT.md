# Avalonia 0.3.0-alpha.11 检查点（2026-10-30）

本地TOTP账户阶段已冻结，完整回归176项通过，耗时1分16秒。严格Base32与otpauth独立参数、RFC6238三算法/大时间向量、独立元数据+系统vault、失败回滚/输入保留、账户变化不转用旧秘密、默认隐藏/当前周期复制/条件清剪贴板/关闭清零、无秘密导出与目标变化保护已实现。

测试只用公开RFC密钥和内存vault，不访问门户/真实秘密。35张headless软件图含新账户小窗失败和公开验证码，已实际查看新图；不是原生GUI、Windows、系统picker或真实vault证明。

- sourceFiles: 519
- sourceManifestSHA256: 420bccfb0217952326290aaea3d913e158d55073dffc55258b661c6825cf6c36
- sourceArchiveSHA256: 96e11694f1ac0d4a66287529b3465045e3b02773ed7614d14173f51f8fbd264f
- checkpoint: /Users/xianyu/IdeaProjects/forterm/checkpoints/avalonia-0.3.0-alpha.11-20261030
- macApp: /Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.11-macos-arm64.app
- macExecutableSHA256: 7575bab7cda50072d2cdb1b523bffabd286e7d23d32e5ef250ab616cec782e31
- windowsPrototypeZIP: /Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.11-prototype-windows-x64.zip
- windowsZIPSHA256: 29c1a0670d0da94dd56c55eb218c91f4a9da8f7a69ae8d88c801355eea3a1dc1
- windowsMainExeSHA256: 18abc6f124c709f89c322d98a629e5358bae0aa74a05d27baca5c96fe789f7c7
- symbolsSHA256: bc840365fd3948958d692c6f3235ef545b5d7c947c84b9057267249da1b5c792

正常无PDB，240PE双表/18Mac原生依赖无未解析/VC外部导入，Win10 icu1903+边界保留。包后无窗口runtime-check与合成xftp诊断通过，519源码一致。继续布局快捷键、连接配置导入导出、最终Windows安装器/portable及现场清单；未原生GUI/Windows/生产验证。旧包保持。
