# Forterm Avalonia 0.3.0-alpha.19 下拉弹出与动画交付（2026-11-10）

本轮参考 XTerminal 的深色菜单层级和操作节奏，统一 Forterm 所有 ComboBox。弹出层使用深色圆角表面、边框和内边距，列表项保持34dp行高，选中项使用绿色强调，悬停颜色柔和过渡。展开时弹出层以150ms CubicEaseOut做透明度和6dp上浮动画，键盘导航、轻量关闭和自动化树继续使用Avalonia原生ComboBox行为。

主题覆盖偏好、连接协议、认证方式、跳板、账户算法/位数、文件来源和工作区排列选择器。没有把文件树、表格或正文菜单全局套用下拉样式。

## 验证

新增下拉动效定向测试2项通过，日志 `avalonia/evidence/alpha19-dropdown-motion-targeted.log`。测试打开实际Popup并断言动画Transitions、末状态、34dp ComboBoxItem和弹出截图。完整231项测试通过，1分03秒，日志 `avalonia/evidence/alpha19-final-validation.log`。

当前软件截图195张，重点图是 `polished-combo-popup.png`、`polished-workspace-local.png`。alpha19截图索引含尺寸和SHA。XTerminal只做只读视觉参考，未操作其终端内容。

## 交付

源码672文件，manifest SHA `ba27b3d6716a8267c03368498c24b77e5dbf3a9eb497b4de7bb34650c4671995`，归档 SHA `ad88e14c640feec3080887d5f8e9a5e8925439438f5dc46f335f40ffc7defa2b`，恢复点 `/Users/xianyu/IdeaProjects/forterm/checkpoints/avalonia-0.3.0-alpha.19-20261031`。

| 文件 | 大小 | SHA256 |
| --- | ---: | --- |
| Windows四入口 ZIP | 52419994 | 8a5569286dd526f4d769daa770474757a84e843ebd23b2d823416bd0f5b5fa02 |
| Windows安装包 | 51214423 | fdcae227bdd45ef3fbc076aaacc2b31d2983dca007c9c5e26f457ac1b1344b78 |
| Windows便携版 | 125973555 | 3d6389c989b0b7e6155241e16be0ad062f7df3d23df1113b6399eb86eb2e5c1a |
| Mac arm64候选 apphost | 125607558 | 4ad3ce0938a0fe2df6cfdea35706f55236fea0598ef64502d4b10c65d3f49310 |

293载荷、ZIP CRC、安装器解包、便携引导、bundle模块、PE双表和Mac依赖证据齐全。正常包不含PDB，symbols独立。alpha19使用已记录的native ARM64 NSIS 3.04工具链。

真实Mac窗口、Windows干净机、ConPTY、物理DPI、IME、原生picker、系统vault、堡垒机现场和生产上传仍待验证。旧包、旧会话和用户配置未操作。
