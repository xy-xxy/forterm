# C# + Avalonia 0.3.0-alpha.1 首个可运行检查点

2026-10-30，已按用户明确授权独立重写，目录avalonia/，没有嵌套git/Go后台，也没有改动旧Electron/native源码、配置、发行包或用户运行窗口。此为不完整纵向原型，不是全部迁移完成。

## 技术方案与已完成证据

- 官方.NET10.0.403 SDK安装到用户独立缓存，不修改PATH/系统安全。Avalonia12.0.4 + Iciclecreek.Terminal4.1.2/XTerm.NET3.0.0；实际VT控件不是TextBox/网页终端。MIT依赖/版本锁、许可证与notice已归档，见avalonia/docs/DEPENDENCIES.md、licenses/PACKAGES.json。
- SSH.NET2026.0.0 MIT源码固定tag commit，最小显式补丁开同一Session的SFTP，借用客户端不重连/不关闭owner。app通道租约管理原页和第二PTY。无反射内部字段，也不偷偷用Go。
- 9项C#本工程测试通过8.8321s（avalonia/evidence/tests-checkpoint-final.log）：一次TCP/认证，两个PTY与SFTP、1MiB+13流传输逐字节一致、独占目标拒绝覆盖、关闭SFTP后shell仍可输入、原租约释放后第二PTY存活、拒shell/SFTP不影响另一通道；Mac owned UnixPTY实际stty93×27。
- 实际Avalonia headless键分发到真实回环SSH收到Tab09/CtrlC03/文本；鼠标拖选、Copy/Paste和Entry焦点隔离、窗口resize到SSH window-change。ANSI颜色/alternate/中文双宽/组合e+重音/ZWJ模型断言通过，终端有实际软件栅格图，不以漂亮静态图替终端。IME/物理输入与复杂TUI另待。
- 连接编辑有150dp左导航/右卡片、同排地址端口/用户密码、固定左右底栏、34dp按钮6dp圆角；窄窗上下排/动作换行，深浅主题和末项定位的软件图已查看。连接库280dp树+虚拟ListBox真实列模板/搜索；元数据单独原子JSON目录，密码只有内存，不读旧库。仅首原型密码认证，每次指纹明确确认。
- 工作区实际终端与SFTP目录/刷新/目录进入/上传下载/取消，使用Avalonia原生StorageProvider。上传随机临时名完成后标准无覆盖rename；下载安全临时提交等尚待，失败/取消不得称全部完成。系统picker只是实现和编译，未实际点击。

## 运行依赖调查

.NET self-contained osx-arm64/win-x64均构建成功，携带.NET runtime；未采用AOT/Trim/单文件来掩盖依赖。递归232个PE（含ConPTY/OpenConsole子目录），static regular/delay import归为随包或Windows系统/API set，未发现vcruntime/msvcp导入。Skia/HarfBuzz/ANGLE/ConPTY链已记录；未从System32拷DLL。报告avalonia/evidence/native-imports.json。动态LoadLibrary/PInvoke、系统版本和干净Windows实跑仍需验证，不称“绝无系统依赖”。

Mac随包native库load commands已记录。打包后完整app在无有效DOTNET_ROOT条件下运行--runtime-check，成功加载Skia/HarfBuzz，零窗口（avalonia/evidence/packaged-mac-runtime.json）。这证明随包runtime基础加载，不能代替真实GUI首帧/选择器/系统DPI。

## 冻结产物与身份

- 完整Mac原型：release/Forterm-Avalonia-0.3.0-alpha.1-macos-arm64.app；arm64/adhoc签名通过，未启动GUI。执行SHA `aee64c1cd008dfd146c009a502c9c5ad1d7145c3fec6c20736394ed739828794`。
- Windows self-contained原型ZIP：release/Forterm-Avalonia-0.3.0-alpha.1-prototype-windows-x64.zip，约74MiB，SHA `d866fb625a2615029f7462045678f29b7d360e121666026d03379dca05135a89`；主EXE `65422e63b314b0982a9d49ff1d4055c1f6882f5f8e1d2beb1bc0e89cd9ace417`。ZIP CRC通过，尚未Windows运行。它目前只有正常主入口，不冒称四入口最终包。
- 417项source/锁文件/许可证/工具源码清单与tar逐项相符。检查点checkpoints/avalonia-0.3.0-alpha.1-20261030/；manifest SHA `ee5ff02f49b671095a0e3166e1bfbe00f62158f416171f3e67e00776f46912d3`；tar SHA `db162f3259b7502906883e0422a93cdeb44fdc505ecd3e130a93719c6b3b9408`。
- release/IDENTITY-Avalonia-0.3.0-alpha.1.json、SHA256SUMS-Avalonia-0.3.0-alpha.1.txt。8张软件PNG在avalonia/evidence/ui-alpha1/，headless不冒称原生截图。
- 旧native dev11可执行SHA仍是4cdb2f372d3ca31cca6a2ba69bdb331a7ac337a164382d502032a838c368fc03，未修改/未强关。

## 下一阶段及主要风险

完整矩阵avalonia/docs/MIGRATION.md逐项列出迁移缺口：四入口/URL大小写一次解码/内存去重票据、多轮KI/key/agent/跳板、vault/hostkey持久、完整心跳策略、独立同会话UI、文件懒树/多源/安全覆盖复制权限sudo、FTP、远程文本编辑与冲突、真实监控四区、完整传输队列/主题布局持久化等仍待；不能引用Go单测充当C#通过。

下一优先迁移外部入口和认证边界，然后安全文件/编辑/监控能力；继续本地可做项。首个候选可供经理协调实际Mac GUI核验，当前不抢前台。C#alpha1独立bundle ID cn.forterm.avalonia.alpha1、独立新元数据目录，尚未单实例IPC，不会转交旧Go窗口。Windows实跑、物理150%/IME、原生picker和完整新安装器/portable均未交付；生产门户契约缺口、旧dev3上传现场失败保持未闭合。未连接192.168.2.2/生产、未读旧票据、未sudo/推送/发布。

证据目录补记：初版测试的ui-alpha1目录名为硬编码，后续alpha2测试曾复用此路径；因此当前该目录不能单独作为alpha1精确版本图证。alpha1冻结源码/包/摘要不变，早期聊天中的原始图片是当时输出；alpha2已改用程序集InformationalVersion命名新图目录，并保留复用前发现时副本ui-alpha2-before-versioned-path。后续按新目录与截图SHA记录版本，不将复用图冒充冻结alpha1图。
