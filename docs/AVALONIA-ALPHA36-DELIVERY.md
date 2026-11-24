# Forterm Avalonia 0.3.0-alpha.36 XTerminal 风格本机文件树交付

本版按参考图把本机文件区改成整栏目录树：目录和文件都作为树节点显示，目录按需展开，收起释放子节点；树从用户主目录开始，占满文件区高度，终端和资源监控保持并列。路径、刷新、返回上级、本机来源隔离和远程失败关联入口保持不变。

合成目录回归覆盖目录展开、文件叶节点、本地终端集成和列表兼容；完整回归 241 项通过。alpha.36 已在 Mac 原生窗口启动并验证：`/Users/xianyu` 树展开后可见目录和 `.bash_history`、`.profile` 等文件叶节点，终端/文件树/监控同时显示。

交付文件：

- [Windows x64 四入口 ZIP](/Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.36-windows-x64.zip) — SHA256 `99f35ce5c5e44016e38e0a10ebfcb0d21f51d63dbae27232e467d1b20aacced8`
- [Windows x64 安装包](/Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.36-windows-x64-setup.exe) — SHA256 `5294da67fddccce37f2ef7c2f0ee6608525f88e0f683d59c5e12957f214ca875`
- [Windows x64 便携版](/Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.36-windows-x64-portable.exe) — SHA256 `0331e43e2790dbce7e72d214fb6d87f5a2735cf5b4257f378127d1188cfc8b5c`
- [symbols](/Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.36-symbols.zip) — SHA256 `84ae8041240208d172efeea29b6d91fae651cab203b4fab71a86bc4b48a8fc9c`
- [Mac arm64 应用候选](/Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.36-macos-arm64.app) — 主程序 SHA256 `1c0042f3ef55519f8bb7b9c4b98dc9c6e9434aa78a61d9916abdac183627ec7e`
- [SHA256 清单](/Users/xianyu/IdeaProjects/forterm/release/SHA256SUMS-Avalonia-0.3.0-alpha.36.txt)

ZIP 已核对 293 个载荷文件、四入口名称和 CRC；安装包/便携版逐文件匹配同一 ZIP，便携版内嵌该 ZIP。Windows 仍待干净实机运行，Mac 本次已完成窗口级启动和目录树展开观察。
