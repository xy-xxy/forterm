> 0.2.1经理独立验包与真实CUA复验通过（2026-10-27）：132项ZIP/安装器、四AMD64入口、portable精确ZIP、241源码摘要均匹配；实际1050×650@150%多文件/展开25队列/末项详情/粘贴撤销/放大1450×950终端真实执行/缩回均通过。原始证据已保存在 `native/evidence/independent-0.2.1/`，详见 `docs/INDEPENDENT-VERIFICATION-Native-0.2.1.md`。仅追加验证记录，运行时和三包SHA不变；Windows实机与完整IME仍未验。无新增确定本地缺陷，后续等待现场/接口资料/新反馈。

> 输入补测已更新（2026-10-27）：独立原生对照窗口中，标准Fyne Entry的a/b/c后空格实际提交ABC，x/y/z后回车实际提交xyz；自绘代码编辑器d/e/f后空格提交“得分”，x/y/z后回车提交xyz。证据 `native/evidence/input-probe-0.2.1/result.json`、`committed-state.json`、`committed-standard-and-code.jpg`。此前普通字母未立即显示属于输入法组合等待，不能继续表述为字符丢失。候选/预编辑显示和提交回车顺序尚未完整验证，Windows键盘/IME仍未实机运行。此补测未改生产运行时或最终主PE。

# Forterm Native 0.2.1 验证与交付身份

2026-10-27，macOS arm64。本轮仅修正0.2.0独立验收发现的小高度布局阻断，不扩展功能。保留原生0.2.0和Electron0.1.7旧包；0.2.0仍有已确认的小窗口缺陷，不推荐使用。

## 最终Windows产物

- `release/Forterm-Native-0.2.1-x64-setup.exe`：11.8MiB。
- `release/Forterm-Native-0.2.1-windows-x64.zip`：16.76MiB，四入口完整目录。
- `release/Forterm-Native-0.2.1-x64-portable.exe`：19.18MiB，精确内嵌上述ZIP。
- SHA：`release/SHA256SUMS-Native-0.2.1.txt`；身份与核验：`release/VERIFICATION-Native-0.2.1.json`。
- 最终主PE SHA256 `3c7ae45123c07c33d17ee38ddaeee0bf76389c7273f4eecc508f1668132d2b9b`。Windows x64的主程序与三个入口经PE检查；ZIP CRC及解包文件一致。NSIS无警告，7zz完整性测试通过，实际解出132个应用文件与准备目录逐字节一致。便携EXE为PE x64且精确内嵌新ZIP；无Chromium/WebView/Node运行依赖。主程序依赖不含测试fixture。

## 本轮通过的本机验证

- `native/evidence/release-0.2.1/native-final-race.log`：完整 `go test -race ./internal/...` 通过（其中原已通过包可由Go缓存复用）；`vet.log`为空成功。
- `native/evidence/small-layout-0.2.1-directed.log`、`small-layout-0.2.1-race.log`：desktop/codeedit/workspace定向/race通过，新增短高度列表/队列在实际滚动列内、列表最小100dp、队列150dp、没有负尺寸或上下覆盖的回归。
- `native/evidence/small-0.2.1/native-gui-smoke.json`：真实原生合成回环 passed。三向编辑停靠同一草稿/光标/撤销历史；精确143字节shell输入/非回显执行/真实ANSI、同transport四能力/认证1/SFTP1、25流式队列、保存确认与取消、dirty关闭取消、监控隐藏暂停与恢复、协议心跳无注入、多来源同名草稿/顶部重复标签隐藏。
- `native/evidence/small-0.2.1/actual-cua-result.json`：实际CUA、全窗口截图读取确认1050×650物理像素，FYNE_SCALE=1.5（Mac）；不是780×620逻辑截图冒充。原图 `small-initial-cua.jpg`，文件和代码正文均可见。
- 实际选择来源A的27文件/25完成队列；轮滚外列抵达展开控件，展开后无进度条向上覆盖；轮滚内部队列至batch-24并实际打开最后项详情。`small-expanded-queue-cua.jpg`、`small-queue-last-cua.jpg`。
- 返回路径工具，实际轮滚文件列表至最后sample.go/index26并选择。`small-file-last-selected-cua.jpg` 与活布局selectedFileIndex=26。
- 鼠标点代码正文、Down/End键盘定位、实际剪贴板粘贴small-window-qa、工具栏撤销。`small-editor-input-cua.jpg`、`editor-input-state.json`、`editor-undo-state.json`，保留来源B原始X草稿。该测试证明这些输入路径，不代表所有IME路径均已验。
- 切来源B再回A，选择/展开队列/草稿保持；实际拖动原生窗口角到1453×954物理像素，再缩回1050×650，正文仍可达。`regular-expanded-queue-cua.jpg`、`regular-state.json`。
- `native/evidence/regular-0.2.1/native-gui-smoke.json`：常规浅色、1150×760逻辑尺寸组合 passed；截图已查看。原始Canvas Capture含Retina backing像素，不能拿其尺寸当全窗口物理尺寸。

## 保留失败与未验证范围

经理原0.2.0失败图/JSON已复制到`native/evidence/failures/small-0.2.0-manager/`。修复前固定Border头尾把正文减为零/负高度；本次文件区使用有可见滚动条的列，文件列表保留真实滚动视口、队列独立滚动，编辑区按短高度分配可用空间并在文档内滚动保留工具/代码/状态，未程序裁切根容器伪造可达。

Mac字符补测已形成更明确结果：独立原生对照窗口中，标准Fyne Entry的a/b/c后空格实际提交ABC、x/y/z后回车提交xyz；自绘代码编辑器d/e/f后空格提交“得分”、x/y/z后回车提交xyz。`native/evidence/input-probe-0.2.1/result.json`、`committed-state.json`、`committed-standard-and-code.jpg`。此前字符未立即显示为输入法组合等待，不能表述为字符丢失；候选/预编辑显示及提交回车顺序未完整验证，Windows键盘/IME仍待实机。生产运行时未改，最终主PE保持。

其他既有原生能力证据索引仍见NATIVE-FINAL-VALIDATION-0.2.0.md（其中0.2.0包的交付推荐已被本轮撤回）。未穷举全部文件drop/批选与缩放交叉组合、长时间多窗口压力。

Windows10/11 x64实际安装卸载、ConPTY、SID管道、系统凭据、中文IME/剪贴板、系统DPI及生产堡垒机权限尚未运行。现场清单 `release/WINDOWS-ACCEPTANCE-Native-0.2.1.md`；交叉构建和静态解包不是Windows运行通过。未知厂商门户仍待脱敏契约，不猜端点、不使用历史票据/生产SSO审批。未推送、上传或发布。
