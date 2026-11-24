# Forterm Avalonia 0.3.0-alpha.29 工作区工具条与文件浏览交付（2026-11-10）

本轮继续按 XTerminal 的操作层级整理 Forterm：连接库行提供连接、编辑和更多动作，页签与工作区工具条保持紧凑，顶部导航和分区采用圆角岛屿表面，工作区左侧按条件切换为文件浏览，没有文件通道时显示可操作的空状态。连接库、新建、本地终端、验证器、导航、工作区和快捷键现在使用紧凑的32dp纯文本模式栏；当前区段以绿色半粗文字显示，其他区段使用中性灰色，悬停/按下有轻量背景，键盘焦点仍清晰可见。去掉大绿色胶囊，避免与 XTerminal 的密集顶栏节奏冲突，也不改变已有入口行为。

## 验证

VisualPolishUiTests 5项定向通过，包含顶部模式栏初始激活、点击切换激活态和软件截图；完整234项通过，日志 `avalonia/evidence/alpha29-final-validation.log`。截图索引位于 `avalonia/evidence/ui-0.3.0-alpha.29/SCREENSHOTS.json`，重点图是 `top-navigation-xterminal-modebar.png`、`workspace-file-pane-replaces-connection-nav.png` 和 `polished-library-empty.png`。

XTerminal仅按用户提供的截图和本机只读界面作视觉参考，未操作其终端内容、未使用生产票据。

## 交付

源码672文件，manifest SHA `302ba2a42468b6f36c41fe5a9f0c475e9226280dab7cf80f2bba8e78428dcb32`，归档 SHA `f88341672df85c9041d542329f8c528e63438816e7331cc3570eedb20dbd085b`，恢复点 `/Users/xianyu/IdeaProjects/forterm/checkpoints/avalonia-0.3.0-alpha.29-20261110`。

| 文件 | 大小 | SHA256 |
| --- | ---: | --- |
| Windows四入口 ZIP | 52423612 | b93c2f302d2c866a8208a8523550c6f6ebaa82f13d464a323a3ec569c07cccb1 |
| Windows安装包 | 51218541 | 76073b3d45cbbf47d59422c055d7aaffcdbcd8ba8a0d1d140a5517cdef6cff33 |
| Windows便携版 | 125977651 | 371003127a2e6386432e1dc1ec7f58613e5dbb8444a18af30c6841c4bef2d27a |
| Mac arm64候选 apphost | 125627014 | d9eefeac7d5f9ee6e5c54b66532d30895a5aee0df45d6dbfc92cf3d3abfadaf4 |

293载荷、ZIP CRC、安装器解包、便携引导、bundle模块、PE双表和Mac依赖证据齐全。正常包不含PDB，symbols独立。Windows/Mac原生GUI、ConPTY、物理DPI、IME、原生picker、系统vault、堡垒机现场和生产上传仍待验证；旧包、旧会话和用户配置未操作。
