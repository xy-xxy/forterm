# Forterm Avalonia 0.3.0-alpha.31 工作区工具条与文件浏览交付（2026-11-10）

本轮继续按 XTerminal 的操作层级整理 Forterm：连接库行提供连接、编辑和更多动作，页签与工作区工具条保持紧凑，顶部导航和分区采用圆角岛屿表面，工作区左侧按条件切换为文件浏览，没有文件通道时显示可操作的空状态。连接库、新建、本地终端、验证器、导航、工作区和快捷键现在使用紧凑的32dp纯文本模式栏；当前区段以绿色半粗文字显示，其他区段使用中性灰色，悬停/按下有轻量背景，键盘焦点仍清晰可见。去掉大绿色胶囊，避免与 XTerminal 的密集顶栏节奏冲突，也不改变已有入口行为。

## 验证

VisualPolishUiTests 6项定向通过，包含顶部模式栏初始激活、点击切换激活态和软件截图；完整236项通过，日志 `avalonia/evidence/alpha31-final-validation.log`。截图索引位于 `avalonia/evidence/ui-0.3.0-alpha.31/SCREENSHOTS.json`，重点图是 `top-navigation-xterminal-modebar.png`、`workspace-file-pane-replaces-connection-nav.png` 和 `polished-library-empty.png`。

XTerminal仅按用户提供的截图和本机只读界面作视觉参考，未操作其终端内容、未使用生产票据。

## 交付

源码674文件，manifest SHA `086a87ea9d92acfc05f43c4e48e335e50cf4489bd8cb146965cd062f25230090`，归档 SHA `bca9be81b5c4d7e8d785084c9db8ffef50fcceaab506c9173a80f28e37dfc26a`，恢复点 `/Users/xianyu/IdeaProjects/forterm/checkpoints/avalonia-0.3.0-alpha.31-20261110`。

| 文件 | 大小 | SHA256 |
| --- | ---: | --- |
| Windows四入口 ZIP | 52426946 | dc46562af1f83f14fcd6c2323411f625264d50e58f7c71d4a08a5e8c41d4cdc2 |
| Windows安装包 | 51228604 | 6eeb72b300d827738ab367816a55f5543ad3328768f2474d380905759877b247 |
| Windows便携版 | 125981747 | 62333af5b6ea9d4e6bf50c6dd3d30b19af3d28b7604ab9212e9b5724cf72d8aa |
| Mac arm64候选 apphost | 125635206 | 5083896ef093b61418b3094bcd4b34c2c752e15ca539d7e0eb5615b08726e564 |

293载荷、ZIP CRC、安装器解包、便携引导、bundle模块、PE双表和Mac依赖证据齐全。正常包不含PDB，symbols独立。Windows/Mac原生GUI、ConPTY、物理DPI、IME、原生picker、系统vault、堡垒机现场和生产上传仍待验证；旧包、旧会话和用户配置未操作。
