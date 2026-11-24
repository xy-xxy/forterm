# Forterm Studio 0.4.0-alpha.1 交付说明（2026-11-22）

阶段 1：新工程骨架与堡垒机唤醒。计划见 `docs/STUDIO-REBUILD-PLAN.md`。alpha.36（`Forterm.Desktop`）及更早版本未改动、未重新打包。

## 内容

- 新工程 `avalonia/src/Forterm.Studio`（程序集名 `Forterm`），复用 `Forterm.Core`；AXAML + MVVM；暗夜/明亮两套配色取自参考截图。
- 主窗口：标题栏会话页签、左侧窄栏、文件/终端/监控三栏（各自页签、收起、最大化、拖宽）、底部状态行。
- 唤醒：`xshell` + ssh:// → 终端工作区（终端 + 文件树同一 SSH 连接）；`xftp` 或 sftp:// → 文件工作区，可按需在同一连接上打开终端；`xftp` + ftp:// → FTP 文件工作区。`-newtab` 作为页签名；`-newwin` 开新窗口；未知参数显示提示、不连接。
- 入口未带密码时弹出“本次密码”对话框；交互认证（动态验证码）多轮对话框；首次/变化主机指纹确认对话框（默认拒绝）。
- **入口等待修复**：`Forterm.Launcher`（xshell/xftp）恢复 0.1.1 行为，等待主程序并转交退出码；此前 C# 入口启动后立即返回 0。
- 主程序若无法把启动转交给已运行实例，返回 4 并提示，不再另起一个主实例。
- 独立数据目录 `LocalAppData/Forterm/Studio`（`FORTERM_STUDIO_DATA` 可覆盖），单实例管道按此目录命名，与 alpha.36 并行运行互不抢占唤醒。
- 本地终端（标题栏按钮或首页按钮）。

## 验证

`Forterm.Studio.Tests` 17 项通过（macOS arm64，`TMPDIR=$(getconf DARWIN_USER_TEMP_DIR)`）：

- 路由：xshell/xftp/ftp/裸 URL/大小写/标题清理/未知参数拒绝。
- 入口链：3 秒内相同启动只开一个会话（标签名变化不影响）；二次实例经管道转交参数；真实 xftp 入口等待主程序 1 秒并返回其退出码 7；主程序缺失时入口返回 2。
- 端到端（Python 回环 SSH/FTP 夹具，仅 127.0.0.1）：xshell 唤醒后终端+文件同连接、主机指纹确认一次；xftp 唤醒后文件可用、按需开终端；无密码时只询问一次且标记为临时；拒绝指纹不连接；FTP 根目录列出；非法参数只显示提示。
- 打包产物（Mac）：`Forterm --runtime-check`；`xshell --diagnose …` / `xftp --diagnose …` 经入口转交主程序并输出脱敏报告；非法参数退出码 2 经入口转交；`forterm-diagnose` 正常。

软件渲染截图：`avalonia/evidence/studio-0.4.0-alpha.1/`（xshell 工作区、xftp 文件工作区、参数拒绝提示、首页深浅色）。

## 产物

| 文件 | SHA256 |
|---|---|
| `release/Forterm-Studio-0.4.0-alpha.1-windows-x64.zip`（51MB，解压后 244 个文件，含四入口与 licenses） | `7b198a615193bf86d48b18bacec09fc213a0fbc65717d5bf20ccfccfea7269bb` |

Mac 目录版：`avalonia/artifacts/studio-0.4.0-alpha.1-osx-arm64/`（未签名、未打成 .app）。

## 未验证 / 限制

- **Windows 实机未运行**：ConPTY、命名管道转交、真实控制器唤醒、DPI/输入法均待现场。
- Mac 原生窗口未打开（只做了软件渲染与无窗口的打包链路检查）。
- 未签名。
- 首页是占位，连接库在阶段 2；监控栏是占位，阶段 4 接入。
- 文件树只读浏览（展开/刷新）；右键菜单、上传下载、编辑在阶段 3/5。
- SFTP 未提供当前目录（REALPATH 被拒）时只显示原因，明确确认绝对目录的流程在阶段 3 接入。
- `forterm-diagnose.exe` 仍是控制台报告（与 alpha.36 相同），没有图形报告窗口和导出按钮。
- 文件树展开箭头样式未生效。
