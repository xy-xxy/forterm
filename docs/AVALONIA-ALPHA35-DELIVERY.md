# Forterm Avalonia 0.3.0-alpha.35 本机目录树交付

本版在本机文件工作区加入懒加载目录树。打开本地终端后，左侧目录树从用户主目录开始；展开节点只读取当前目录的子目录，收起分支会丢弃节点，点击目录树节点会同步刷新右侧文件列表。原有本机路径、刷新、返回上级、文件大小和本地来源隔离保持不变，远程 SFTP/FTP 失败时仍保留已有文件会话关联入口。

合成目录测试覆盖目录树根节点、展开子目录、列表联动和本地终端集成；完整回归 241 项通过。alpha.35 已在 Mac 原生窗口启动并验证：本机来源为 `/Users/xianyu`，列表 87 项，目录树可展开。

交付文件：

- [Windows x64 四入口 ZIP](/Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.35-windows-x64.zip) — SHA256 `03923e1d248eabef49aec2014119066da08e774afa810a11ac89c5bc8f7e15b0`
- [Windows x64 安装包](/Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.35-windows-x64-setup.exe) — SHA256 `95346799d4ea4694aca5d0d4b651d2ba4371646cd9e0a4b889d25bf41d76ae32`
- [Windows x64 便携版](/Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.35-windows-x64-portable.exe) — SHA256 `c96fbec25a0383716ccb6ab29f3343beb9ab02e8e448db00c5d90591463f2f10`
- [symbols](/Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.35-symbols.zip) — SHA256 `9822c604c14f1d28e64f3d99e3813de0a013f3551e1317afae5d38b2f5f1f5e2`
- [Mac arm64 应用候选](/Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.35-macos-arm64.app) — 主程序 SHA256 `b6aa3f808d62fbe54cd42b6bbda3dcd5761d1e9d59294a55052d8efaedcd8425`
- [SHA256 清单](/Users/xianyu/IdeaProjects/forterm/release/SHA256SUMS-Avalonia-0.3.0-alpha.35.txt)

ZIP 已核对 293 个载荷文件、四入口名称和 CRC；安装包/便携版逐文件匹配同一 ZIP，便携版内嵌该 ZIP。Windows 仍待干净实机运行，Mac 本次已完成窗口级启动和目录树展开观察。
