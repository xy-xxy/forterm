# Forterm Avalonia 0.3.0-alpha.33 工作区工具条与文件浏览交付（2026-11-10）

本轮继续按 XTerminal 的操作层级整理 Forterm：连接库行提供连接、编辑和更多动作，页签与工作区工具条保持紧凑，顶部导航和分区采用圆角岛屿表面，工作区左侧按条件切换为文件浏览，没有文件通道时显示可操作的空状态。连接库、新建、本地终端、验证器、导航、工作区和快捷键现在使用紧凑的32dp纯文本模式栏；当前区段以绿色半粗文字显示，其他区段使用中性灰色，悬停/按下有轻量背景，键盘焦点仍清晰可见。去掉大绿色胶囊，避免与 XTerminal 的密集顶栏节奏冲突，也不改变已有入口行为。

## 验证

VisualPolishUiTests 7项定向通过，包含顶部模式栏初始激活、点击切换激活态和软件截图；完整239项通过，日志 `avalonia/evidence/alpha33-final-validation.log`。截图索引位于 `avalonia/evidence/ui-0.3.0-alpha.33/SCREENSHOTS.json`，重点图是 `top-navigation-xterminal-modebar.png`、`workspace-file-pane-replaces-connection-nav.png` 和 `polished-library-empty.png`。

XTerminal仅按用户提供的截图和本机只读界面作视觉参考，未操作其终端内容、未使用生产票据。

## 交付

源码676文件，manifest SHA `f5996aa26b4055cfad45ee8c3587c965c09f5e1a0e1aaa6b7eb76885c7d7fef7`，归档 SHA `eaf22558798f679c23c788845d91616816e12a6b109aa65eab5c0ab84c6e8313`，恢复点 `/Users/xianyu/IdeaProjects/forterm/checkpoints/avalonia-0.3.0-alpha.33-20261111`。

| 文件 | 大小 | SHA256 |
| --- | ---: | --- |
| Windows四入口 ZIP | 52431912 | 577099aa4540f8219c966768ed5f206979cbbb6b712db9752a72212f12f1a444 |
| Windows安装包 | 51238937 | ab1c28d122c11058174b84fcf7617f14b8a7f044f309616238dca62ef23eb8be |
| Windows便携版 | 125985843 | 5226e8ce2f34c97801c47d43e78400e194707afd59d0d45dd19f480ab5b7ec1c |
| Mac arm64候选 apphost | 125654662 | bd0d07d090ef4db91ef0ba79d95761a30c3becca4beab4622895a01eb47ffa2f |

293载荷、ZIP CRC、安装器解包、便携引导、bundle模块、PE双表和Mac依赖证据齐全。正常包不含PDB，symbols独立。Windows/Mac原生GUI、ConPTY、物理DPI、IME、原生picker、系统vault、堡垒机现场和生产上传仍待验证；旧包、旧会话和用户配置未操作。
