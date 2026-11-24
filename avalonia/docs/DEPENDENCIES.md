# 依赖和运行边界（调查中）

- SDK：Microsoft .NET10.0.403，官方dotnet-install.sh安装在/Users/xianyu/.cache/forterm-dotnet，未修改PATH/profile/系统安全。工程global.json固定SDK。官方来源：https://learn.microsoft.com/en-us/dotnet/core/install/macos 。正常首次SDK曾自动创建开发HTTPS证书，未执行trust；本应用不用ASP.NET服务器。
- Avalonia.Desktop/Themes.Fluent12.0.4，MIT，原生Win32/Cocoa窗口与Skia渲染，不含Chromium/WebView。https://github.com/AvaloniaUI/Avalonia
- Iciclecreek.Avalonia.Terminal4.1.2，MIT；XTerm.NET3.0.0，MIT；Porta.Pty2.2.3，MIT。官方仓库https://github.com/tomlm/Iciclecreek.Avalonia.Terminal 、https://github.com/tomlm/XTerm.NET 、https://github.com/tomlm/Porta.Pty 。实际支持声明不等于本应用已在Windows实测。Porta.Pty含Unix native fork shim与Windows ConPTY实现，包内conpty/OpenConsole如出现需逐项审native依赖与再分发许可。
- SSH.NET2026.0.0源码，MIT，tag commit7b2fd3dbf2c86a80a7b06cea020aa5f821c9902e。vendor/SSH.NET保留LICENSE/UPSTREAM.json。上游public SftpClient默认另建transport，不满足一次临时票据，因此做显式源码扩展：SshClient.OpenSharedSftpClient；BaseClient借用Session不重连/不Dispose owner；SftpClient开子系统。其余密码/KI/算法逻辑不改。补丁需回环“认证1/关闭子通道/拒通道不影响另一项”证明，不能只看API。
- BouncyCastle.Cryptography2.7.0 MIT、Microsoft.Extensions.Logging.Abstractions8.0.3 MIT；传递SkiaSharp/HarfBuzz等实际包和许可证将从锁文件/包内nuspec输出，不凭库名猜。
- 测试：xunit.v3、Avalonia.Headless.XUnit、独立venv Paramiko4.0.0，后者仅夹具，不进入发行应用。

发布策略：win-x64/osx-arm64 .NET self-contained目录，先不Trim/AOT/单文件，以便核验全native链。self-contained只包含.NET runtime，并不表示无VC++或OS依赖。对每个PE objdump imports并递归区分同目录/Windows系统/未解析；对Mac dylib用otool-L。VC runtime app-local只在明确许可与来源可证明时携带；不得从用户System32复制不明DLL。干净Windows运行仍是完成“无需用户安装VC/.NET”的必要现场门槛。若现有包含未解决非系统native依赖，会如实列出并不称满足。

实际静态扫描：递归232个PE（含ConPTY包的x64/arm64 OpenConsole），import均可归为随包或Windows系统/API set，未见vcruntime/msvcp导入。mscoree是托管PE传统stub，实际self-contained host使用同包hostfxr/coreclr；不据此要求装.NET Framework。动态LoadLibrary/PInvoke与干净Windows运行仍待。Mac18个入口/原生库load commands已存证；runtime-check在无有效DOTNET_ROOT条件下实运行加载Skia/HarfBuzz，零窗口，不等于GUI首帧。

licenses/收录实际包内许可/notice及官方上游MIT文本，PACKAGES.json含传递依赖声明。两平台.NET runtime LICENSE与THIRD-PARTY-NOTICES同样携带。Windows.Console.ConPTY1.24.260710001包声明MIT，源码Microsoft Terminal，最低Win10 1809；应用目标Win10/11 x64与Mac14+，实际系统运行核验待。

alpha.3新增系统动态调用：Windows advapi32 CredReadW/CredWriteW/CredDeleteW/CredFree、kernel32文件句柄身份；Mac Security.framework SecItem API、CoreFoundation与libSystem lstat，不新增外装C++运行库。Mac加载14个Keychain常量/函数符号，没有访问凭据；真实读写/锁定/授权仍待。非交互查询使用公开但已deprecated的kSecUseAuthenticationUIFail；不允许交互时明确失败，不明文回退。官方依据：https://developer.apple.com/documentation/security/ksecuseauthenticationuifail 、https://developer.apple.com/documentation/security/1401659-secitemadd 、https://learn.microsoft.com/en-us/windows/win32/api/wincred/nf-wincred-credwritew 。

alpha.2实测：Win ZIP78,298,558B，展开225,436,164B；Mac124,119,666B。两大调试符号libSkiaSharp.pdb84,033,536B和libHarfBuzzSharp.pdb20,918,272B，alpha.3正常包排除PDB，单独归档symbols，不移除DLL或许可证。新体积以交付SIZE报告为准。

alpha.4无新增运行时NuGet依赖。已有agent仅使用现成Unix SSH_AUTH_SOCK或Windows openssh-ssh-agent命名管道，不安装/启动agent；自实现有界标准帧，仅请求公钥与签名，签名交SSH.NET验证。规范来源：https://github.com/openssh/openssh-portable/blob/master/PROTOCOL.agent 与 https://datatracker.ietf.org/doc/html/rfc9987 。跳板内部relay仅loopback，随机用户名/密码、单目标、成功后停止监听；复用父SSH direct-tcpip，不执行交互终端命令。restricted-destination/session-bind与证书/硬件agent类型暂不宣称支持。


alpha.5 新增 Avalonia.AvaloniaEdit 12.0.0，MIT，NuGet 所声明源码提交 86fdebec4cff7affb0e14c7885df71d28edce777，原生文本控件（无 WebView）。锁文件与 licenses/AvaloniaEdit-LICENSE 保存版本及实际上游许可。来源：https://github.com/AvaloniaUI/AvaloniaEdit/ 。SSH.NET 额外补丁限制 exec stdout/stderr 合计缓存并明确拒绝 exec 请求；仅供独立固定只读 Linux 监控，不往交互终端输入命令。

alpha.5 补全静态扫描：GNU x64 objdump 不识别 ARM64 OpenConsole，且工具文本未完整列出延迟加载表。新增按 PE/COFF 数据目录解析常规/延迟导入，并用 GNU/Apple LLVM 交叉核对工具可见项目。实际额外识别 Skia 的 d3d12.dll 及 OpenConsole 的 icu.dll 延迟项，均非 VC 运行库。联合 icu.dll 是 Windows10 1903+ 系统组件，不能据 ConPTY 的1809声明就保证本应用1809可用；旧版Win10仍需专门现场核验，不从用户系统复制DLL。来源：https://learn.microsoft.com/en-us/windows/win32/debug/pe-format 、https://learn.microsoft.com/en-us/windows/win32/intl/international-components-for-unicode--icu- 。旧静态报告有工具覆盖边界，alpha.5新报告以239个PE双表解析为准，仍不是动态或干净系统运行证明。

alpha.7 开发新增 FluentFTP55.1.0（MIT，纯托管，无额外依赖），NuGet来源提交 f051d6425342bc99617d933af3580f718c381f64，保留 licenses/FluentFTP-LICENSE。SelfConnectMode.Never、SslSessionLength=0，禁用自动重连、会话轮换与高层自动重试；目录使用有界LIST和显式226/250完成回执，绕开上游CloseAsync吞超时的路径。路径已经在入口解码一次，关闭SanitizeUrlEncoding以保留文件名中字面%20，其他控制字符/遍历拒绝仍保留。来源：https://www.nuget.org/packages/FluentFTP/55.1.0 、https://github.com/robinrodricks/FluentFTP/tree/f051d6425342bc99617d933af3580f718c381f64 。pyftpdlib2.2.0仅自有127.0.0.1测试夹具，不进发行包。
