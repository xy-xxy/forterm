# Forterm Avalonia 0.3.0-alpha.34 本机文件工作区交付

本次修复了本地终端没有文件列表的问题。打开“本地终端”后，工作区会创建独立的“本机”文件来源，默认从当前用户主目录开始列出文件和文件夹；刷新、进入子目录、返回上级和文件大小展示都在本地来源内完成，不会把本机来源当成 SFTP/FTP 去认证或关联。远程 SFTP/FTP 读取失败时仍显示“关联已打开的文件会话”入口，权限降级规则保持不变。

合成目录 UI 测试覆盖本机来源列举、进入子目录和本地终端自动安装文件工作区；alpha.34 完整回归 241 项通过。软件渲染截图见 `avalonia/evidence/ui-0.3.0-alpha.34/local-terminal-file-workspace-alpha34.png`，尺寸为 900×600 逻辑像素，截图中可见本机文件来源、`local.txt` 和终端/监控并列布局。

交付文件：

- [Windows x64 四入口 ZIP](/Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.34-windows-x64.zip) — SHA256 `6fe4f3decdbaaf83dcc570c3f29a0bc251ee3ee603fb268751c3093bb0abd83c`
- [Windows x64 安装包](/Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.34-windows-x64-setup.exe) — SHA256 `9b2f104edc3b981f19afbb96f9b32974532c0442c40fcba7ecf8b5c5f2315e47`
- [Windows x64 便携版](/Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.34-windows-x64-portable.exe) — SHA256 `89e6df99d3caee601dc0eb2f537b879eb3972a2ef951248902a8de45567a2948`
- [symbols](/Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.34-symbols.zip) — SHA256 `9303f98c2bc91cb6a450bcba805c6dbfd6bbc52c255f888d2891513eefc219fe`
- [Mac arm64 应用候选](/Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.34-macos-arm64.app) — 主程序 SHA256 `2ecaf958d0fa36df90f85cc36f0bda58d47cb4b89ca4b4497178e7f16e17da80`
- [SHA256 清单](/Users/xianyu/IdeaProjects/forterm/release/SHA256SUMS-Avalonia-0.3.0-alpha.34.txt)

ZIP 已核对 293 个载荷文件、四入口名称和 CRC；安装包/便携版逐文件匹配同一 ZIP，便携版内嵌该 ZIP。Windows 仍未在干净实机运行，Mac 原生窗口复核因当前系统锁屏未执行；软件渲染和交叉构建证据不替代现场验证。
