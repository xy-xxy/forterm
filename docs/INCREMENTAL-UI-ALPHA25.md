# Forterm Avalonia 0.3.0-alpha.25 工作区工具条与文件浏览交付（2026-11-10）

本轮继续按 XTerminal 的操作层级整理 Forterm：连接库行提供连接、编辑和更多动作，页签与工作区工具条保持紧凑，工作区左侧按条件切换为文件浏览，没有文件通道时显示可操作的空状态。连接库、新建、本地终端、验证器、导航、工作区和快捷键现在使用紧凑的32dp纯文本模式栏；当前区段以绿色半粗文字显示，其他区段使用中性灰色，悬停/按下有轻量背景，键盘焦点仍清晰可见。去掉大绿色胶囊，避免与 XTerminal 的密集顶栏节奏冲突，也不改变已有入口行为。

## 验证

VisualPolishUiTests 5项定向通过，包含顶部模式栏初始激活、点击切换激活态和软件截图；完整234项通过，日志 `avalonia/evidence/alpha25-final-validation.log`。截图索引位于 `avalonia/evidence/ui-0.3.0-alpha.25/SCREENSHOTS.json`，重点图是 `top-navigation-xterminal-modebar.png`、`workspace-file-pane-replaces-connection-nav.png` 和 `polished-library-empty.png`。

XTerminal仅按用户提供的截图和本机只读界面作视觉参考，未操作其终端内容、未使用生产票据。

## 交付

源码672文件，manifest SHA `293da746dbf964638cce4af1f3bc449cb993dc817271b331b956e40244246729`，归档 SHA `2ef000d86f2979ba2a903b54cbe1df69df1270f17ba786f87d86a478c133fee7`，恢复点 `/Users/xianyu/IdeaProjects/forterm/checkpoints/avalonia-0.3.0-alpha.25-20261110`。

| 文件 | 大小 | SHA256 |
| --- | ---: | --- |
| Windows四入口 ZIP | 52422314 | c68795214c903d0a5fede675475412778fbb31447f4cabbdedaafba7a8bd3484 |
| Windows安装包 | 51216474 | f5626b3c5a6ac94b3e9f65cde72bdf99cdb05152c822cbc0f8864d2597a7eb49 |
| Windows便携版 | 125973555 | 75b0e71f73f42d408c4b18ee7f1a56c97650b8836b76ba7ff0d999e124da683f |
| Mac arm64候选 apphost | 125619846 | 63589c0f7aa6e70c740619b31616f705b25955193eea55374a350654a8ca570a |

293载荷、ZIP CRC、安装器解包、便携引导、bundle模块、PE双表和Mac依赖证据齐全。正常包不含PDB，symbols独立。Windows/Mac原生GUI、ConPTY、物理DPI、IME、原生picker、系统vault、堡垒机现场和生产上传仍待验证；旧包、旧会话和用户配置未操作。
