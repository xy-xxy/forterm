# Avalonia alpha.3 安全传输、队列与凭据检查点（2026-10-30）

在冻结alpha.2基础上继续实现，不覆盖旧包/源码检查点，不启动可见GUI或触碰生产。完整C#迁移仍在进行，本阶段可运行原型不是最终全部交付。

## 传输与来源生命周期

- 上传与下载都先创建同目录随机临时文件；远程EXCL创建在SFTP OPEN原子带0600，Mac本地临时0600。完整流式SHA、复读与源/目标元数据和内容复核通过才提交；不在系统保存对话框返回后直接OpenWrite截断旧文件。
- SourceId/原目录/实际文件版本在等待选择器与确认前绑定。改路径栏、切换来源、原视图关闭、取消选择器不会改投新目标。父目录逐段raw LSTAT拒绝链接；本地用Mac dev/inode或Win文件句柄volume/index身份，父目录替换、链接/特殊文件拒绝。Windows本机磁盘规则与PInvoke仅编译，实机待。
- 原子替换需明确确认与服务端advertised posix-rename；无能力就拒绝覆盖，没有删除旧文件再改名的退路。权限/owner/group不能保留则拒绝覆盖。恢复远程权限使用已打开临时句柄FSETSTAT，避免按被替换的路径chmod；本地成功替换保留原Unix权限。
- 新raw OPEN/RENAME/REMOVE扩展避免客户端REALPATH把最终链接解析为其他目标。临时上传句柄属性与路径属性也核对；本地临时清理核对创建时文件ID，归属变化不删替换项。
- 元数据8秒边界，数据无进展30秒，单任务30分钟默认上限；元数据超时不被视为不存在。提交前取消保持旧目标。进入不可撤销提交点后等待独立回执；回执丢失标“需核对”，只隔离原SFTP通道，不自动重试、不影响其他SSH通道。不把提交后取消说成已回滚。
- 标准SFTP v3不提供inode/CAS，本阶段是路径类型/UID/GID/mode/时间长度/完整内容的阶段性检查，不宣称可以排除恶意并发换目录或最后检查至rename间的所有竞争；原子版本条件提交需要服务端额外能力。

## 串行队列与UI

- 128等待/512历史的单写队列，绑定原source关闭token；等待任务能立即取消，运行任务等临时清理结束，提交中禁撤销。完成/失败/取消/需核对有明确文字与详情；需核对项不被“清理完成”丢弃。
- 两行紧凑行、真实字节/总量与细进度、30dp详情/取消，折叠队列与小高度76dp行视口；进度刷新限约10Hz并合并UI通知。完成后释放执行闭包，避免历史保留整个VT/来源对象。
- 目录加载使用8秒有界异步列表、2万项边界与epoch；路径输入变化先清旧列表，晚到回复不覆盖新路径。会话标签显式关闭取消原来源任务，上传完成仅在原来源/原目录仍当前时刷新。
- 终端4MiB积压与读取异常有明确独立提示，关闭该PTY仍保留SFTP/其他会话；既有反压隔离回环继续通过。

## 系统凭据、指纹和私钥UI

- 真正OS API后端：Win Credential Manager（Advapi32 CredRead/Write/Delete）与macOS Keychain（Security SecItem，CF字典）。不调用携密命令行，不保存明文，不自建加密钥匙。未勾系统保存的新密码完全不访问vault，临时URL连接不写库。
- 显式保存密码与私钥口令分开；取消保存清除旧值，保留旧值需确实存在；失败留在编辑窗口保留输入。元数据写入失败回滚OS凭据，回滚未确认明确报错。保存期间外部修改/删除不被旧表单静默覆盖。
- 普通主机指纹持久，初次/变化明确核对、拒绝不改旧值；相同已保存指纹不反复问；测试连接不写信任或凭据。数据默认独立AvaloniaAlpha3，不自动读取旧版密码。
- 私钥与独立口令卡片、选择文件、保存策略已接；文件2MiB边界，解析在后台，追加问题与私钥口令不自动使用登录密码/临时票据。连续KBI再切password方法仍需新输入，不能发送空值或重放最初票据。
- 真实保管库读写/锁定/授权未实操：事务和UI测试使用假vault，Mac只加载14个系统常量/函数符号，没有访问任何凭据。kSecUseAuthenticationUIFail为公开但deprecated接口，无法非交互访问时失败，不明文回退；实际Mac/Win验证仍待。

## 验证与证据

57项最终通过16.9900s：avalonia/evidence/alpha3-final-validation.log。包含原协议/入口/认证/VT套件与真实owned SFTP流、取消保旧/原子覆盖保权限/提交回执丢失只停文件通道、目录/来源绑定、元数据超时、原生picker服务的控制器异步模型、队列串行/关闭来源、fake vault回滚和输入保留、known-host变化等。所有SSH/SFTP只127.0.0.1临时夹具；没有生产上传。

12张版本绑定软件图在avalonia/evidence/ui-0.3.0-alpha.3/，已查看小窗队列、私钥末项、深浅/保存失败等，SCREENSHOTS.json有尺寸/SHA。不是原生窗口/物理150%或真实picker点击；旧dev3现场“选完不动”和Win性能反馈不能据此结案。

## 冻结产物与实际体积

- 正常Mac：release/Forterm-Avalonia-0.3.0-alpha.3-macos-arm64.app，123,916,693B（118.18MiB）；arm64/adhoc通过，正常GUI未启动；apphost SHA `7e5c19177bd9da02ca5dbddd98097e8e50b8aad2d9680fb4f0d92e313c709d60`。
- Windows四AMD64入口正常原型ZIP：release/Forterm-Avalonia-0.3.0-alpha.3-prototype-windows-x64.zip，51,770,085B（49.37MiB），展开120,283,461B（114.71MiB），285文件、CRC通过；SHA `f97d5044861461c6d18513afa3c09b2793651dbd9261605bd5f4a77c59fc97fb`。实际Forterm.dll SHA `886139acb6d4ba2166b3c550187e69e8f69ce34473748965b4a11f9ce68c36e4`，Core SHA `a331617219069eb77b22c5c7cdfa16216c65ae2ac57ccdc24df3669a566da88f`，apphost不能单独识别托管版本。
- 正常Win ZIP/Mac app均断言没有PDB；所有DLL与许可证保持。symbols独立27,093,320B（25.84MiB），SHA `30d17f0bfbec3005005d970389d79177c1e83a658054005116db3f27c697190f`。旧alpha2完整包不变。
- Win展开分布：.NET runtime及其他81,599,600B、图形/终端30,518,624B、SSH/crypto5,705,104B、应用896,000B、许可证/元数据1,564,133B；最大文件CoreLib16,033,616B、Skia11,628,896B、Xml7,788,328B、ANGLE5,394,096B、Bouncy5,155,728B。完整排行和alpha2对比见release/SIZE-Avalonia-0.3.0-alpha.3.json。
- 443源码/锁文件/许可/工具项与tar/manifest匹配。检查点checkpoints/avalonia-0.3.0-alpha.3-20261030/；manifest SHA `15c722a25e190e502d0f7c52b5723f7d7aa63a60d32dc080cbb888737f904b3e`；tar SHA `2565c15f2f61964ce7ed6d9dfd3eec4d974f55d0dfa2d5ff8e360552ea6db777`。IDENTITY/PAYLOADS/SHA256SUMS-Avalonia-0.3.0-alpha.3在release。

打包app--runtime-check在无有效DOTNET_ROOT下加载Skia/HarfBuzz和Keychain相关符号，零可见窗口、CredentialsAccessed=false；Mac实际无窗口诊断参数转交通过。237 PE static imports无vcruntime/msvcp或未解析项；新增Advapi32/Kernel32/Security/CF/libSystem动态调用单列，不把静态扫描当干净Windows实测。源码SDK/许可证详DEPENDENCIES.md。

## 后续

下一阶段继续agent/跳板、可配置心跳与会话复用UI，再做在线编辑、真实监控、文件树多来源/复制移动权限/FTP和矩阵剩余项。实际Mac GUI/系统picker、Windows/物理DPI/IME、真实vault读写、生产堡垒机以及最终安装器/portable都未完成。经理只读核包和图审，程序员不抢前台、不向其他聊天发消息、不推送发布。生产门户契约缺口保持。


## 后续截图归属补记（alpha.5 开发时发现）

三个测试曾硬编码 alpha.3 图片路径，alpha.4 全套运行时覆盖了 transfer-queue-small.png、system-save-failure-keeps-input.png、key-page-small.png。当前这三图不能作为冻结 alpha.3 的原图；原摘要与当前摘要保留在 avalonia/evidence/alpha3-screenshot-provenance-audit-alpha5.json，其余图匹配。旧发行包与源码归档未修改。所有测试写图路径现统一取实际程序集版本，避免后续再覆盖旧版。
