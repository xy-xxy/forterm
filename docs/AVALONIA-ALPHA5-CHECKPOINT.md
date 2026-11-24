# Avalonia 0.3.0-alpha.5 检查点（2026-10-30）

C# 重写的四区、安全在线编辑和同 SSH 真实监控阶段原型，完整迁移继续。旧版本和用户窗口保留。

## 已实现与验证

- 终端、文件、在线编辑、资源监控同一工作区同时显示，分别展开收起，三列及监控区可拖动分隔；SFTP入口可按需打开同认证终端。
- 原生 AvaloniaEdit 多草稿（最多12），UTF-8/BOM、LF/CRLF/混合换行、撤销、Ctrl/⌘S；打开/保存1MiB边界，拒绝二进制/链接。内存快照共用串行队列，明确确认、私有临时项、完整SHA与远端版本复核、保权限原子替换/新建排他。来源关闭只读保留草稿，不自动改投；关闭有未保存提示。SFTP元数据与哈希复核不是服务端CAS。
- 当前SSH独立exec固定只读Linux命令，实际 /proc 逐核增量、内存、根磁盘和网络速率/最多60采样趋势。默认手动、3秒可选自动（3–600），隐藏/切页取消并暂停、恢复重置首样；单飞、8秒可见截止、合计1MiB输出边界、exec拒绝/超时保其他能力。没有安装agent或往PTY输入监控命令。
- 95项本工程全量通过（47秒），日志 avalonia/evidence/alpha5-final-validation.log。覆盖同认证四能力并行及exec权限降级、远端同大小同mtime冲突、保存提交点取消不误报、排队取消保草稿、自动刷新与隐藏暂停。21张软件图及尺寸/SHA在 avalonia/evidence/ui-0.3.0-alpha.5/SCREENSHOTS.json；1150×760、768×506、700×433和深浅主题。小监控视口可滚动，不宣称全部指标在小高度下同时无需滚动。

## 冻结身份

- sourceFiles: 466
- sourceManifestSHA256: 56184adef3b84a8370805070b41e7e7a3172f7a9d419a3586abdafaeeaed2a9d
- sourceArchiveSHA256: c2f77c7e0bb9931512562d2855503634d408d6d42f65e10e545eb204968f3e53
- checkpoint: /Users/xianyu/IdeaProjects/forterm/checkpoints/avalonia-0.3.0-alpha.5-20261030
- macApp: /Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.5-macos-arm64.app
- macExecutableSHA256: 9244b2adf9bb0561a04543297754b79c55bd7040dfbbad3f85ee2257b2659cfa
- windowsPrototypeZIP: /Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.5-prototype-windows-x64.zip
- windowsZIPSHA256: 2b545871fe5757e84cbd3ae0b9c499fb5213255cdee5dc3c3206f265c044316e
- windowsMainExeSHA256: 92ca30518cb13cd1a7d0f6e4c992e7bd2790383c1666e8429e348fd9e0774f11
- symbolsArchive: /Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.5-symbols.zip
- symbolsSHA256: e46a9119a32dc51b154d67a57446ca6be9a844ffa87014ec8af5c7b0bd46b487

正常Win ZIP 52,054,969字节（约49.64MiB），展开121,053,370字节；Mac124,685,512字节。正常包无PDB，符号另存。主EXE apphost不足以识别构建，托管payload摘要见 release/PAYLOADS-Avalonia-0.3.0-alpha.5.json，体积见相邻SIZE文件。签名、ZIP CRC、四AMD64入口与466源码归档一致性检查通过。打包后的Mac只执行无窗口runtime-check/合成xftp诊断，未创建原生窗口或读真实凭据。

## 依赖扫描的实际边界

239个PE均解析常规和延迟导入，并用GNU/Apple LLVM交叉检查可见条目；18个Mac原生load commands。未发现vcruntime/msvcp或未解析项。补扫识别Skia的d3d12.dll和OpenConsole的icu.dll延迟项；联合icu.dll是Windows10 1903+系统组件，1809及更老系统不能据此声明兼容。旧alpha报告的工具文本覆盖不足已补记。依据 https://learn.microsoft.com/en-us/windows/win32/debug/pe-format 和 https://learn.microsoft.com/en-us/windows/win32/intl/international-components-for-unicode--icu- 。静态检查仍不是干净Windows/动态加载证明。

## 下一步与待验

继续alpha.6文件懒树、多来源明确关联及只隐藏被选中文件协议的顶部会话，保留原认证/源绑定/草稿和传输；随后FTP、复制移动、权限sudo及完整迁移清单。当前没有最终安装器/portable，原生GUI/物理输入/IME/150%、系统picker、真实vault、Windows和生产现场仍待经理协调；不抢前台、不重跑历史Go PID45327。

构建补记：alpha.6复建发现Core/vendor旧锁文件可能只剩最后发布的win-x64 RID，alpha.5的严格locked restore流程需要先对目标RID重新生成锁文件。alpha.6已统一所有工程双平台RuntimeIdentifiers，包选择未变，解决该发布脚本缺口；alpha.5原始包/源码归档不改。
