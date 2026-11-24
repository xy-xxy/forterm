# Forterm Avalonia 0.3.0-alpha.12 本地交付验证

本轮本地功能迁移与三种Windows包已交付。真实原生GUI、系统picker、真实OS凭据、Windows/IME/性能/高DPI及生产堡垒机未验证；旧Go“选完文件不动”的现场失败未据此结案。门户API脱敏契约缺失，内置门户登录/资产仍不可用，不猜接口。

完整回归188项通过（47秒，avalonia/evidence/alpha12-final-validation.log）；新增便携功能10项定向通过（81毫秒，alpha12-portable-tests.log），包括12KiB参数、空字符串/中文/引号、无shell、SHA完整性/目录边界/重复大小写/链接拒绝。不把两次测试写成一次198项全量。此前通过的无变化全量没有重复跑。

43张软件渲染图在avalonia/evidence/ui-0.3.0-alpha.12/SCREENSHOTS.json，尺寸与SHA齐全。已实际查看三向停靠的同SSH四区/编辑器置左置底、大字号小窗、账户小窗失败/TOTP公开向量/配置确认图。测试认证一次、同PTY与草稿跨布局保留，鼠标拖动和方向键调整持久。上下排列探索图因挤压文件列表未采用，保留在alpha12-layout-exploratory-not-final。

四入口ZIP、安装器和便携版293载荷逐文件核对。正常包无PDB；symbols另存。NSIS安装器为32位通用引导壳，实际四入口及C#便携入口为AMD64，不能将安装壳误当32位应用载荷。安装到当前用户独立版本目录；卸载精确删除本包文件、不删除用户配置/vault/未知文件/旧版本。便携是C# self-contained单文件、原ZIP SHA后解压、ArgumentList转交、等待主进程后清理本次应用临时目录；.NET自身使用标准本用户单文件运行机制。

537源码清单/源码tar/包后源一致，Mac正常包adhoc深签名和无窗口Skia/HarfBuzz/xftp合成诊断通过。240个PE常规+延迟导入/18Mac原生依赖无未解析与VC外部导入，Windows icu.dll 1903+边界保留，静态检查不等于干净Windows已运行。

补充校验器还解析C#便携文件的175项.NET bundle元数据：所有173托管程序集（含新便携模块）与已核验来源摘要一致；其余为deps/runtimeconfig，未携带额外native条目。Windows宿主本身的常规/延迟导入另行检查，无VC外部导入。SDK RuntimeList.xml中coreclr/clrjit标记DropFromSingleFile，不能错误要求它们以独立原DLL原样出现在单文件中；单文件宿主和原ZIP均确实存在。

## 包装校验的失败及修正记录

原冻结工具tooling/package-windows.py成功生成两包后，尝试在ARM Mac加载Windows-x64托管程序集，被CLR拒绝架构；日志alpha12-windows-packages-errors.log保留，没有执行Windows应用。冻结537源/包不改。补充scripts/verify-avalonia-alpha12-packages.py对已生成包原地核验：Windows便携中完整管理模块与原ZIP逐字节一致，实际bundle格式和所有管理运行库摘要核对，同一冻结源码编译的Mac校验器对同一最终ZIP实际解压校验（零窗口/零子进程）。证据alpha12-portable-mac-final-payload-check.json。该结果不是Windows执行证明。

补充校验器自身SHA及副本在checkpoints/avalonia-0.3.0-alpha.12-packaging-verifier-20261030；与原537源检查点分别保留，可恢复。再构建时使用原打包器完成编译后用此补充校验器替代跨架构加载步骤。重复执行会拒绝覆盖已有最终验证文件，不重写旧包。

IDENTITY文件记录首次冻结时installerPortableGenerated=false；最终状态以DELIVERY及VERIFICATION-windows为准，二者为后续生成的三包交付记录，没有改写冻结历史。

## 冻结身份和三包

- sourceFiles: 537
- sourceManifestSHA256: 05e0e47073ed43cbda17ac0428526272df45f57ad21fd4f721c40192b35ec42c
- sourceArchiveSHA256: e0019d42e3338afc80a76951f66200441ca1ae1096a73bbea147861b31ee5003
- windowsMainExeSHA256: 7b415f62aa6be040c43dae722c43d76494140429a31aade4445df258ebceee37
- macExecutableSHA256: 84e080b4fa1df85afdef99782e8b13c77148dffff2a4364b0cee7ae18d002a42
- windowsZIPSHA256: 7453997c11a3366b4117302146c74f4bc00cfe507e1fcf0c5721841065f83ae4
- installer: Forterm-Avalonia-0.3.0-alpha.12-windows-x64-setup.exe；51,203,175字节；SHA256 060918fd88d6b98abe73a56dae78b9275470f7c0b5b831ce05b4a18baa36a729
- portable: Forterm-Avalonia-0.3.0-alpha.12-windows-x64-portable.exe；125,961,267字节；SHA256 7c3e1c558cb38e5e65ee8a9e2220fd3cfd5cfeeb4244dce83ee8b6ed85cdaffd

ZIP 52,407,462字节，展开121,965,419字节。更新说明、SHA256SUMS与WINDOWS-ACCEPTANCE均在release，全部现场项保持未勾选。不推送、发布、占前台、生产上传、读真实秘密或重放票据。
