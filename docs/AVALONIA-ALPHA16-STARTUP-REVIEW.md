# alpha.16 启动修复验证（2026-10-31）

冻结alpha.15真实Mac独立窗口点击本地终端显示空引用，同时后续出现提示符。经理合成窗口原图/身份/日志按原字节保存在 `avalonia/evidence/independent-native-startup-alpha15-20261031/`；该次PROMPT的字面\n和工具输入不作为有效多行缩放证据。

独立软件宿主直接引用冻结alpha.15 App及控件DLL，正确实际换行夹具预写到自有ZDOTDIR，走正常MainWindow按钮，捕获首次异常 `TerminalView.Pty.cs:319`（PtyOptions读取未初始化的_terminal.Cols/Rows）。完整结果/截图在 `avalonia/evidence/frozen-alpha15-startup-repro/`，直接runner日志 `alpha15-frozen-startup-repro-direct-runner.log`。两DLL SHA见result.json并对应冻结载荷；没有原生窗口、用户.zshrc或生产连接。

早先独立宿主日志保留：观察器未判空、未驱动布局、未复制native库，以及VS适配器JSON失败均属宿主问题，不算产品新失败。复制同冻包dylib与匹配osx PDB、直接运行xunit后复现闭合。未改冻结包。

alpha.16等待OnInitialized、并发单飞、工作区关闭自动Loaded启动、关闭前取消/生成后归属检查、读取令牌局部捕获；真实启动错误保留。此前alpha.15测试先布局新页再启动，缺失真实按钮时序，本次明确覆盖。

首轮6项5通过1失败：Unix无效exe实际创建子进程并退出2，测试误期待SpawnAsync抛异常。原日志alpha16-startup-targeted.log保留；改为严格确认退出2/真实启动错误可见，无效exe未恢复成功。修后6项全过，日志alpha16-startup-revised-targeted.log。正常按钮3独立场景均真实多行、仅一次PID、无控件NRE、无错误文字，大→小→大提示严格一份。另并发默认自动启动、初始化前关闭不spawn、无效exe可见。异常诊断分支尚不称在Mac native spawn上强制触发成功。

同时发现alpha.15控件FORTERM.patch为空，原hash记录正确但diff遗漏。本版从固定原commit下载源码，逐个核对原SHA后重新生成4文件完整diff；旧冻结记录保持，不以本次补件反称旧diff完整。XTerm.NET未变，不重复无变化上游2312模型测试。

修后完整工程227项全部通过（1分13秒），日志alpha16-final-validation.log；已实际查看正常启动及四区合成图。两平台构建与冻结进行中。无新版本原生GUI/Windows现场成功声明。不占前台，不操作既有用户终端/配置/旧包，不向其他聊天发消息。

最终三包与冻结后精确DLL正常按钮软件复验已完成，详AVALONIA-ALPHA16-DELIVERY.md；实际nativeGUI/Windows仍待。
