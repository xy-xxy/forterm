# Forterm Avalonia 0.3.0-alpha.15 终端缩放修订候选（2026-10-31）

本轮处理真人反馈“窗口从大缩小时终端重复错行/右提示混正文”。用户原窗口仅由经理获准只读观察，本聊天只看本地现场图，不读取用户.zshrc、终端流或票据，不向其终端输入、清屏、关闭或占用前台。现场before/smaller窗口尺寸未改变，不能称那次拖角已新复现。用户画面不复制进入发布或合成证据，也不转录内容。

## 自有复现与修订

正常MainWindow配真实自建zsh -f -i，自有ZDOTDIR/HISTFILE、彩色中文/Powerline长路径多行左提示和右提示。大→小→大使同一左提示从1份变2～3份，缓冲区也重复，排除仅绘图几何；修前合成证据在avalonia/evidence/alpha15-resize-failure-multiline。

固定原版本MIT源码XTerm.NET3.0.0(commit453ed957…)与控件4.1.2(commit0851f519…)：vendor中含完整许可、原文件SHA、变更清单和统一diff。不是盲目升级版本。

- 活动PTY采用稳定物理原行网格，避免主动重排与shell重绘互相干扰。历史不再自动重新换行；缩小暂时裁切的单元/属性保留，未被后续覆盖/擦除时，恢复宽度可显示。清右/整行/显示范围处理旧尾，擦左不删未擦右尾，避免旧字复活。
- 同一布局事务按最终Bounds成对提交模型和原PTY，多个中间cols/rows不各自触发重绘；不只延后后端、让模型先跑。真实PTY事务断言预提交无resize，最终仅一次，stty实际回读一致。
- 新绑定和异步spawn检查已经变化的布局，隐藏/零面积忽略过渡尺寸；SSH忽略非正值，真实1×1不再擅自变2×2。
- 保存光标的相对/绝对行混用归一，缩放按逻辑内容/行锚点定位，覆盖scrollback和pending-wrap。
- 字体独立调查：旧实选Menlo无Powerline，本机已有Meslo/JetBrainsMono/Hack Nerd；新属性默认和theme setter均优先已有等宽Nerd，实际MesloLGS NF且样本字形可用。未安装字体；不保证无覆盖字体的机器显示所有私用字形。

没有resize清屏、按键/命令注入或修改用户主题。完整定位过程与策略边界见docs/AVALONIA-ALPHA15-RESIZE-REVIEW.md。

## 首次整组失败与修后证据

首轮完整216项中215通过，四区切换的多行提示line56仍从1变2，不能按早期定向绿交付。原失败逐字节保留avalonia/evidence/alpha15-first-full-failure.log，SHA256 d81c56b01427d5c2c7c379b01f68c989df93f7172e1f9535ac86e1b3a12c9bd8。

随后加入最终布局模型/PTY成对提交、真实单事务断言及固定5个独立提示场景/两种切换顺序。每场景严格初始1份、随后仍1份，没有放宽计数、重复直到绿或用clear掩盖。修后完整单次221项57秒通过：alpha15-revised-full-validation.log；alpha15-final-validation.log为同结果副本供冻结，旧失败未删除。

上游固定源码模型2312通过、1项原有SKIP。最初1个Unicode全码位fixture因上游通配未拷贝失败，补原始fixture后通过；最终全组通过日志alpha15-upstream-xterm-final-source.log，不算成工程221项。

确认内容包括：

- 正常MainWindow真实owned zsh的大→小→大、快速连续缩放、四区/三种中央分隔；末请求与网格一致。
- 真实stty写入自有文件回读，UI输入触发仅子进程输出的随机标记，缩放后实际鼠标选中复制完整标记。真实本地Launch启动中布局改变/字体zoom也通过。
- 两个本地PTY隐藏/切页：缩放第二页不变更隐藏第一页，两进程存活且输入/输出标记不串。
- 自有SSH真实WindowChange、alternate有界全屏帧/独立随机ACK、文件通道保留、认证1；resize期间终端输入为空。
- 裁切文字/宽字符放大恢复、擦除不复活尾字、擦左边界、保存光标/滚动历史与待换行、无效/一格SSH尺寸等模型和通道测试。

部分新增测试早期失败来自物理行搜索换行标记、选择点过贴分隔线误触拖动、两行视口测试帧写超出后滚走头部，均保留诊断并修正测试依据，未修改生产代码凑断言。截图等正常绘制调度后捕获，不靠终端Refresh/clear。

135张当前软件图/尺寸/SHA在avalonia/evidence/ui-0.3.0-alpha.15/SCREENSHOTS.json。固定case0～4的owned-zsh-resize、four-panels、owned-local-launch-resize-zoom、owned-two-pty-returned-first、owned-ssh-resize-alternate-ack以及尺寸/协议/font探测JSON可复核，主要图已实际查看。旧迭代owned图保留alpha15-before-layout-coalescing-*，不当新版最终证明。所有这些均软件窗口+真实owned PTY/合成服务，不是用户真实窗口或Windows现场通过。

## 冻结身份与交付

- sourceFiles: 665
- sourceManifestSHA256: 18f47c09d264540d3c94bcc0234b7b8abd1cc8a0cf06270a79b8288336428a0a
- sourceArchiveSHA256: d9f7c9f3b0481ef1d6795f03b41aa7b9abfe1bd32856b0bd71a07a8dfe86fd5c
- checkpoint: /Users/xianyu/IdeaProjects/forterm/checkpoints/avalonia-0.3.0-alpha.15-20261031
- windowsMainExeSHA256: 57e34f3e073752687ce238b80924ff2a3111e0330bec8487d36c8944210bdbd9
- macExecutableSHA256: ba8d6bee1572461a4d1f5d0a3a5f86fd9f86c8b5321580ab06d422b35a25f4c0
- ZIP: Forterm-Avalonia-0.3.0-alpha.15-windows-x64.zip；52,414,669字节；SHA256 c5a89672fca879cd3d14eeeba292e283327b3f939219f55f40dde363c250c4c3
- installer: Forterm-Avalonia-0.3.0-alpha.15-windows-x64-setup.exe；51,209,084字节；SHA256 46aab22db5fa8e631d64dfd5cc394aecce4c98a808fa50fe749b73b173b2b074
- portable: Forterm-Avalonia-0.3.0-alpha.15-windows-x64-portable.exe；125,969,459字节；SHA256 0bbc9c3d1776e6fe9a9e8d163267d3d364453804ad947293d29eaf4276bf52dc

四AMD64入口/ZIP CRC与293载荷核对；安装器解包293文件一致，C#便携精确内嵌原ZIP与管理模块，175项bundle/管理运行库摘要核对，同源码Mac校验器只解压/哈希最终ZIP，零窗口/零子进程。240PE常规+延迟导入/18Mac原生依赖无未解析/VC外部导入；外壳另查双表。正常无PDB，symbols另存；Win10 icu1903+边界保留。静态核验不代表干净Windows执行。

Mac正常包adhoc深签名、无窗口Skia/HarfBuzz/合成xftp诊断通过；包内明确读取到两个resize-grid-v15补丁标识，PAYLOADS新增模型/控件摘要防止误装旧NuGet DLL。665源码与冻结清单一致。IDENTITY中的installerPortableGenerated=false属于初次冻结，后续真实三包状态以DELIVERY/VERIFICATION-windows为准。

## 主机打包工具恢复（不改冻结应用）

原打包工具缓存缺失，恢复经官方校验的同版本NSIS与7-Zip后，旧x86编译器在当前ARM机器返回Bad CPU type。没有安装Rosetta/系统组件，也没有改运行时来绕过。根据原electron-builder打包配方，独立构建官方NSIS3.04源的ARM64主机编译器，8192字符串/advanced logging配置匹配原Windows资源，C/C++编译器源码未改，仅旧构建脚本适配Python3/SCons4.8.1；原x86编译器保留。

原冻结tooling/package-windows.py未编辑，补充scripts/package-avalonia-alpha15-native-nsis.py只替换主机compiler路径执行；NSIS脚本、Windows stubs/plugins、应用与原ZIP均不改。实际编译器v3.04 SHA a9bd745e834f36d3cc71f09c0eaabc01fdd35ca650e165385e312b11b6adb9e4；构建参数/来源/源码SHA/补丁与单独恢复点见BUILD-TOOLS记录与checkpoints/avalonia-0.3.0-alpha.15-native-packaging-20261031。licenses/PACKAGES中f409…是冻结的旧主机预设摘要，实际本次主机链以BUILD-TOOLS为准，不冒称仍用旧x86文件。原缓存缺失、架构失败日志/中间目录全部保留，未为工具变化重复功能全量。

## 待复核

本版是缩放修订候选。用户当前alpha.14与旧会话/源包保留，未用真实提示配置复验，不保证已损坏的旧历史可凭截图恢复；新版本原生窗口大→小→大仍由经理协调。Windows/ConPTY/物理DPI/IME、真实堡垒机通道、系统picker/vault/生产上传等待现场，门户契约仍缺。未占屏、读用户终端流或配置、真实agent/vault/票据、生产上传、SSO/审批重放、推送/发布或向其他聊天发信。
