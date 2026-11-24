# Forterm Avalonia 0.3.0-alpha.18 界面打磨交付（2026-11-10）

本轮按截图标出的四块区域重新整理界面层级。左侧连接导航使用紧凑的图标、名称和数量行，连接列表补充协议徽标。终端、文件、在线编辑和资源监控变成统一高度的能力芯片，选中、悬停、禁用和键盘状态保持一致。文件来源栏加入“文件来源”标签并把下拉、关联和解除操作合成一组工具条。编辑器排列选择器与能力工具条对齐，并补充排列图标。

原有工作区快捷键、D1-D4切换、文件来源选择、文件协议会话隐藏规则和文件正文左对齐继续使用原逻辑。没有把树、表格或文件列表文字全局改为居中。

## 验证

相关界面定向2项通过，日志 `avalonia/evidence/alpha18-visual-polish-targeted.log`。连接库空态、工作区本地态、8个既有连接/布局场景的定向UI回归通过，结果见 `avalonia/evidence/alpha18-ui-targeted.log`。完整测试第二次运行231项全部通过，1分24秒，见 `avalonia/evidence/alpha18-final-validation.log`。

第一次完整运行有一个本地启动场景短暂观察到提示出现两份。该场景单独重跑通过，第二次完整运行也全部通过。这个过程和日志均保留，作为测试调度抖动记录。

新旧截图在 `avalonia/evidence/ui-0.3.0-alpha.18/` 与 `avalonia/evidence/button-alignment-before-alpha16-theme-unisolated/`。当前索引包含194张软件渲染图，新增 `polished-library-empty.png` 与 `polished-workspace-local.png`。这些是软件渲染核对，不代表用户原生窗口或Windows现场。

## 交付身份

源码671文件，manifest SHA `9aad20f5181a3727da5c9f1d561f7a55e8f2ca382e847a786a8049bedec490da`，归档 SHA `e3e759e88987020c66ce1102aaf14118ff1d60a91b87c9b0af4c49ed6f9bbf66`，恢复点 `/Users/xianyu/IdeaProjects/forterm/checkpoints/avalonia-0.3.0-alpha.18-20261031`。

| 文件 | 大小 | SHA256 |
| --- | ---: | --- |
| [Windows四入口 ZIP](/Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.18-windows-x64.zip) | 52419054 | 90807284deabe66745cd07596c633eed0e0be19c8ee2cc5c6ae32f66dd227824 |
| [Windows安装包](/Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.18-windows-x64-setup.exe) | 51214301 | d582be3721678b0aebf7696956823fa0113baba5c109a0755c297d683d723ee6 |
| [Windows便携版](/Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.18-windows-x64-portable.exe) | 125973555 | 30817fa3e97457581542d630bcf8c29397bc962d600c427ebe2e6ef2977e2dab |
| [Mac arm64候选](/Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.18-macos-arm64.app) | 125603462 | apphost 245ea2a9a8a24c1eb2287021065149b68d527a66d1945077e9bb9983c0ead460 |

293个载荷在ZIP、安装器和便携版之间一致，安装器与便携引导的静态导入、PE双表、Mac依赖、ZIP CRC、运行时模块和无窗口诊断均已记录。正常包不含PDB，symbols独立。alpha.18使用已记录的本机NSIS 3.04 ARM64工具链。

## 待现场

Mac真实窗口、Windows干净机、ConPTY、物理DPI、IME、系统文件选择器、系统凭据库、真实堡垒机通道权限降级和生产上传仍待现场验证。旧包、旧会话和用户配置未操作，未使用真实票据、SSO或审批接口。
