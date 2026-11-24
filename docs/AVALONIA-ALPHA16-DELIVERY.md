# Forterm Avalonia 0.3.0-alpha.16 本地终端启动修复交付（2026-10-31）

正常点击“本地终端”会在新模板模型初始化前读取行列数，alpha.15真实窗口先显示空引用，随后Loaded自动启动又出现提示。冻结alpha.15精确DLL在同按钮路径捕获首次NRE `TerminalView.Pty.cs:319`。此前测试先布局新页后启动，遗漏这一时序。alpha.16等待初始化、工作区由明确入口启动、默认控件并发单飞；关闭前取消等待，异步spawn回来核对归属，reader捕获本次token。保留真实错误/非零退出，Release仅记录类型/调用栈而非argv/环境/消息。

## 验证

- 工程完整227项全部通过，1分13秒，日志 `avalonia/evidence/alpha16-final-validation.log`；其中新增6项覆盖正常按钮3独立真实多行夹具/一次启动/缩放严格一份、并发、初始化前关闭、无效exe失败可见。首轮测试误期待Unix exe失败抛异常、实际退出2，原失败保留，修正的是平台依据。
- 冻结后用精确alpha.16包内DLL再次走正常按钮，提示符出现、0首次控件NRE、无错误文字；DLL SHA匹配PAYLOADS。宿主源码/fixture/result/截图在 `avalonia/evidence/frozen-alpha16-startup-check`，反例在frozen-alpha15-startup-repro；两者无native窗口，不冒称nativeGUI通过。
- 668源码逐项与checkpoint一致，138软件图SHA/尺寸有索引，正常启动和四区图已查看；293载荷在ZIP/installer/portable一致，原ZIP/管理模块与bundle运行库摘要核对，同源码Mac便携校验器0窗口0子进程。240PE常规/延迟和18Mac依赖无未解析/外部VC；outer引导双表另核。正常无PDB，symbols另存。
- Mac adhoc深签名、无窗口Skia/HarfBuzz和合成xftp诊断通过，无凭据API消费/生产网络。控件补丁launch-ready-v16，模型resize-grid-v15；模型未改，不重复上游2312通过1原有skip。旧控件diff为空，本版按原commit逐项原SHA补齐完整4文件diff，旧冻结记录不改。
- 本版源内packager改用已验证native ARM64 NSIS3.04，实际编译器/7zip摘要、版本和原源码/构建脚本适配写BUILD-TOOLS与checkpoint；无需Rosetta/系统安装。

## 交付身份

源清单SHA256 `e68eb30e60a9e2a4ea026bbd0bcefe7f8bf1cec090c394403fbf6fbf8cbf6e1b`；源码归档 `545d11630ddc37618dacf284160ceead3c8d70ba3edfe79a96f0f896aff7269a`；恢复点 `/Users/xianyu/IdeaProjects/forterm/checkpoints/avalonia-0.3.0-alpha.16-20261031`。

| 文件 | 字节 | SHA256 |
| --- | ---: | --- |
| Forterm-Avalonia-0.3.0-alpha.16-windows-x64.zip（四入口） | 52416598 | f07dd6697b449670169534e0cfc0341df6ffea43bc92be446b5e7d5f2be86b64 |
| Forterm-Avalonia-0.3.0-alpha.16-windows-x64-setup.exe | 51212231 | b493f7bfe0d202a8af5bf9e801fe999cc240ce937edf07ac52066a068b9241e3 |
| Forterm-Avalonia-0.3.0-alpha.16-windows-x64-portable.exe | 125969459 | a97a9cf1abbb4d5dc1bb121c68a3fc70f22221cd086cd5cb5236dd5b2584523c |

Mac `/Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.16-macos-arm64.app`；apphost SHA `27eaa7b55753c9f91c3fae052f612cc811dde582af9d6b95277ee8a8056dccb1`。PAYLOADS、DELIVERY、VERIFICATION、BUILD-TOOLS、SIZE、OUTER-PE、symbols、SHA256SUMS（15条）、更新说明和WINDOWS-ACCEPTANCE位于release相同版本名。IDENTITY的installerPortableGenerated=false为初冻历史状态，最终三包生成状态以DELIVERY/VERIFICATION为准。

## 待现场

新版本实际Mac窗口正常启动和正确多行缩放仍待经理协调；Windows干净机四入口/安装卸载/ConPTY/IME/物理DPI、原生picker/系统vault、真实堡垒机权限降级/上传和监控对照仍待。门户契约未给，不实现猜测接口。此次没有占前台、读取真实.zshrc/用户终端、操作旧实例/配置/票据/agent/vault、生产SSO审批重放、跨聊天发信或推送发布。旧alpha.15及更早版本与用户会话保持。

## 冻结后独立复核补充

2026-10-31经理获本次用户许可的独立实际Mac合成多行启动/缩放/收窄与一次粘贴有限通过，原图身份已归档，详INDEPENDENT-NATIVE-RESIZE-Avalonia-0.3.0-alpha.16.md。此为冻结后补充，不倒改release的历史待复核字段。用户原主题/Windows/ConPTY/IME/picker/vault/生产仍待。
