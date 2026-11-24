# Avalonia 0.3.0-alpha.4 检查点（2026-10-30）

这是 C# 迁移中的阶段原型，尚非完整交付。旧 Electron、Go 和 alpha.1–3 包保持。

实现已有本机 agent 签名、保存跳板链与认证复用、可靠全局应答保活与独立 SFTP 探测、同认证独立终端新页、主题字号导航和心跳偏好。仅使用自有合成 agent/SSH 服务测试，不访问用户 agent 或生产认证。

75 项本工程测试通过（48.4418 秒），日志 `avalonia/evidence/alpha4-final-validation.log`。软件渲染图在 `avalonia/evidence/ui-0.3.0-alpha.4/`。Mac self-contained 无窗口运行时检查通过，未访问系统凭据。237 PE 静态导入检查通过，无未解析项或 vcruntime/msvcp；此结果不代表干净 Windows 已运行。

## 冻结身份

- sourceFiles: `456`
- sourceManifestSHA256: `b71f8043ba84f8a4b48921d68762f16362f2675efa0a3c870a743cc5bd90ed82`
- sourceArchiveSHA256: `4d0ff0ea1fce9a32fe97708c2c454d04f607e274d9ae91e4c5ff37313df72b2e`
- checkpoint: `/Users/xianyu/IdeaProjects/forterm/checkpoints/avalonia-0.3.0-alpha.4-20261030`
- macApp: `/Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.4-macos-arm64.app`
- macExecutableSHA256: `047b335a4e87bcbdea7c35eaf8d052abe5931c53096d72d1f92ba20594242134`
- windowsPrototypeZIP: `/Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.4-prototype-windows-x64.zip`
- windowsZIPSHA256: `c2d14e985f134a4c79c4a5f047991820933069c0da0211dbe195be5653fffa88`
- windowsMainExeSHA256: `ef9a78b46e808505e22e66341a35958c647047c4ff4b37cebfa59ba86bad1b71`
- symbolsArchive: `/Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.4-symbols.zip`
- symbolsSHA256: `2c2223f631200f1e20c2d46c5dbf5f2edbce6f6a188ddb8f8a1caed7b95e434b`

正常 Windows ZIP 51,793,723 字节，展开 120,340,300 字节；Mac 123,972,501 字节。正常包无 PDB，符号单独归档，运行库和许可保留。四入口 AMD64/ZIP CRC 已检查。apphost 不足以标识版本，托管 payload 摘要见 `release/PAYLOADS-Avalonia-0.3.0-alpha.4.json`；体积见相邻 SIZE JSON。

## 尚未完成

在线安全编辑、真实同 SSH exec 逐核监控、四区组合与三向分隔、懒树多来源、FTP、复制移动和权限 sudo 等仍待迁移。原生 GUI、物理输入/IME、系统文件选择器、真实凭据读写及 Windows 实机待协调；安装器和便携版未生成。历史 Go PID45327 不属于 C# 验收。

下一工作版本 alpha.5，先实现真实 exec 指标采集的有界输出/取消/权限隔离及解析，再接入按需监控与安全编辑。冻结 alpha.4 包和源码归档不再修改。

静态扫描后续补记：alpha.5 发现 GNU x64 objdump 无法覆盖附带ARM64 OpenConsole，且旧工具文本漏列部分延迟加载项。旧“无未解析项”只限当时工具可见项，不能当完整双表扫描。alpha.5 新增 PE/COFF 常规/延迟导入解析和 LLVM 交叉检查，识别 d3d12/icu 系统延迟项，其中联合icu.dll为Windows10 1903+；旧Windows10尤其1809尚无兼容结论。未改alpha.4原包/源码归档。
