# Forterm Avalonia 0.3.0-alpha.13 布局修订交付（2026-10-30）

本版修正经理从alpha.12截图指出的既有要求缺口。alpha.12确实是源码中的终端左/文件中/监控底、CPU逐核横条；不是夹具换位。旧“本地收敛/仅待外部”表述撤回。alpha.12三包、537源检查点与证据保持，本版独立新身份。

## 实际修订

- 正常MainWindow：最左连接导航、左文件区/懒树、中央终端/在线编辑、右侧监控。编辑器三向停靠只在中央变换；控件、原PTY、草稿与SSH认证不重建。文件协议来源顶部标签隐藏规则保留。
- 监控为右侧窄栏，整栏内部滚动；CPU逐核为两列环形仪表、固定视口虚拟列表。256核为128行数据，实际控件数量有界；cpu255可达，刷新后保持末核位置。总CPU/内存/根分区环、真实速率趋势/来源/自动刷新单飞超时与隐藏暂停保留。
- 700×433逻辑尺寸（约1050×650@150%内容面积）：四区边界与顺序有断言，编辑正文有实际TextView高度断言，监控最末说明可滚动到完整可见。中央正文保留最小空间、压缩编辑工具栏/草稿页与状态行；长元数据保留tooltip。逻辑软件图不是物理150%或Windows实机证明。
- 队列紧凑两行，详情/取消为确实26×26dp，完整tooltip；170dp窄栏实际鼠标取消通过。详情正文滚动、关闭按钮固定；空队列不占第二行计数。文件工具栏紧凑化且保留文字/提示。

## 验证和图片

完整单次201项46秒通过：avalonia/evidence/alpha13-final-validation.log。之后只为同一仪表用例增加“刷新后仍在cpu255”的断言，737ms通过：alpha13-core-scroll-refresh.log；生产代码未变，不重复全量，不算202项。

正常MainWindow通过公开入口、指纹确认、文件编辑与用户式展开导航/监控进行软件测试；没有把SessionPane移入新造的布局来拍图。全部使用自有127.0.0.1合成服务器/公开测试凭据，256核计数来自夹具真实exec回复，经实际采样比较显示，不在UI硬填百分比。认证1；权限降级、隐藏暂停及原有能力回归均通过。

61张软件图尺寸/SHA在avalonia/evidence/ui-0.3.0-alpha.13/SCREENSHOTS.json；主要完整深浅、小窗、滚动末项、256核末项及队列图已实际逐张查看。索引：

- normal-main-expanded-dark.png / normal-main-expanded-light.png：正常1150×760完整MainWindow。
- normal-main-expanded-700-logical.png / normal-main-expanded-1050-logical.png：两种小尺寸完整四区。
- normal-main-monitor-bottom-700-logical.png：窄窗监控滚到底，网络/说明完整可达。
- monitor-256-cores-last-180.png / monitor-256-cores-last-after-refresh-180.png：180dp末核及刷新保持。
- queue-narrow-170.png / queue-details-small.png：26dp命中、取消和小详情固定关闭。
- 修前实际MainWindow图单独在avalonia/evidence/alpha13-before-layout，未改作新版通过证明。

## 冻结与三包核验

- sourceFiles: 540
- sourceManifestSHA256: 11f1770e7cb5eb3d53edc9d6bc1fce4d6499de50ab205966efa845eacde2c5f0
- sourceArchiveSHA256: e6fc72ee6bb0b4b884e1b854fe5082a8001cadf066619979887c99b9e67c317d
- checkpoint: /Users/xianyu/IdeaProjects/forterm/checkpoints/avalonia-0.3.0-alpha.13-20261030
- windowsMainExeSHA256: 12be7bba84ced8e3f53f854d348f12359eb4a5b5b70793ce46733ab0e8ce3b8b
- macExecutableSHA256: 8e2578dcfd44cf82bf81bca733a1c37236e3222eee78ba1afeb2aedd8b077520
- ZIP: Forterm-Avalonia-0.3.0-alpha.13-windows-x64.zip；52,409,632字节；SHA256 801abff6fb6c5ff68d7743974f2c9f46d08793e1171f2bd85f1d5e4d7916d788
- installer: Forterm-Avalonia-0.3.0-alpha.13-windows-x64-setup.exe；51,202,944字节；SHA256 161ec71264ecdb40f9dc0e1b87e8ad13305bfba74e99490e4672f889a89a8117
- portable: Forterm-Avalonia-0.3.0-alpha.13-windows-x64-portable.exe；125,961,267字节；SHA256 53b0266e342b62cb2a714aeb94f5c1cbfcce60cde936d9001b813c0c7c67236d

ZIP CRC/四个AMD64入口和293载荷核对；安装器解包293文件与ZIP一致，C#便携内嵌精确原ZIP与托管模块一致。同冻结源码Mac校验器对最终WindowsZIP实际解压/哈希，零窗口/零子进程。实际175项bundle元数据与全部管理运行库摘要核对。alpha.13打包器已集成正确流程，没有重复alpha.12跨架构加载失败，也不再需要外部补充脚本。

主运行时240个PE常规/延迟导入和18Mac原生依赖无未解析/VC外部导入；两种外壳另用同一PE解析器检查常规/延迟表（OUTER-PE记录），C#便携AMD64、NSIS安装引导壳为x86而应用载荷为AMD64。静态检查不能替代干净Windows执行，Win10 icu1903+边界保留。正常包无PDB，符号另存。Mac正常包adhoc深签名与无窗口Skia/HarfBuzz/合成xftp诊断通过，540源码与冻结清单一致。

IDENTITY记录初始冻结阶段installerPortableGenerated=false；后续真实三包状态、路径/SHA以DELIVERY和VERIFICATION-windows为准，未改写冻结历史。

## 待独立复核与现场

本次布局/仪表/队列修订待经理独立复核。原生GUI/系统文件选择器/真实OS vault/Windows性能与IME/物理高DPI/生产堡垒机仍未验证。旧Go现场上传问题未被本次软件图自动结案；门户脱敏契约仍缺，不能内置自动登录或猜接口。更新说明与未勾选现场清单随ZIP及release提供。

没有抢前台、使用用户真实agent/vault/票据、生产上传、终端注入、sudo生产操作、SSO/审批重放、推送或发布。仅本聊天编辑与本地记录，未向其他聊天发送消息。
