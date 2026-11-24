# Forterm Avalonia 0.3.0-alpha.30 工作区工具条与文件浏览交付（2026-11-10）

本轮继续按 XTerminal 的操作层级整理 Forterm：连接库行提供连接、编辑和更多动作，页签与工作区工具条保持紧凑，顶部导航和分区采用圆角岛屿表面，工作区左侧按条件切换为文件浏览，没有文件通道时显示可操作的空状态。连接库、新建、本地终端、验证器、导航、工作区和快捷键现在使用紧凑的32dp纯文本模式栏；当前区段以绿色半粗文字显示，其他区段使用中性灰色，悬停/按下有轻量背景，键盘焦点仍清晰可见。去掉大绿色胶囊，避免与 XTerminal 的密集顶栏节奏冲突，也不改变已有入口行为。

## 验证

VisualPolishUiTests 6项定向通过，包含顶部模式栏初始激活、点击切换激活态和软件截图；完整235项通过，日志 `avalonia/evidence/alpha30-final-validation.log`。截图索引位于 `avalonia/evidence/ui-0.3.0-alpha.30/SCREENSHOTS.json`，重点图是 `top-navigation-xterminal-modebar.png`、`workspace-file-pane-replaces-connection-nav.png` 和 `polished-library-empty.png`。

XTerminal仅按用户提供的截图和本机只读界面作视觉参考，未操作其终端内容、未使用生产票据。

## 交付

源码672文件，manifest SHA `fb3e8b929de33899d622ce6f654ae9a4c1e49d8c7127ec468184321ad04a173d`，归档 SHA `f448b1572ea383bba05f7d6cbf15b9f388dd4bff4e89153c64acd8d6a281b769`，恢复点 `/Users/xianyu/IdeaProjects/forterm/checkpoints/avalonia-0.3.0-alpha.30-20261110`。

| 文件 | 大小 | SHA256 |
| --- | ---: | --- |
| Windows四入口 ZIP | 52424591 | bd4b8a9e61a3c916dee20bef3f44eb7c3667fa8bcb7ac3a11d7ac002ccb9ec17 |
| Windows安装包 | 51221891 | 9ab394a03d5a6b9e74719c28537d804b22822d62b73bf999c941ee52f05ec321 |
| Windows便携版 | 125977651 | 38cb5f15ef90773951639addcf5abba87087f8f8cf0b69e7a64de9490b85835c |
| Mac arm64候选 apphost | 125629574 | f8023a77a91a81c3fb88dee4b5417671e9db78fa243bb82a4135e24d1314ba1f |

293载荷、ZIP CRC、安装器解包、便携引导、bundle模块、PE双表和Mac依赖证据齐全。正常包不含PDB，symbols独立。Windows/Mac原生GUI、ConPTY、物理DPI、IME、原生picker、系统vault、堡垒机现场和生产上传仍待验证；旧包、旧会话和用户配置未操作。
