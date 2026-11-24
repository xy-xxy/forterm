# dev.8 首绘字体初始化顺序修复 · 离线审查

## 当前结果

经理独立复核已完成：ZIP CRC/四AMD64入口、414冻结源码及tar、Mac预览摘要一致，最小diff已审，隔离冻结源码的冷启动一致性race通过4.135s。原始结果 `native/evidence/ui-0.2.2-dev.8/independent-manager/verification.json`；未执行dev8原生首帧或Windows。已有用户解锁请求未答，传包与具体首帧复验继续由经理协调，不重复询问或轮询。

dev.7经理已按用户许可实际检查Mac预览PID92827并正常退出：深浅连接管理、合成静态四区、导航实拖约40逻辑px、小窗更多/刷新及收起导航有7张原始图。文件及摘要已归档 `native/evidence/ui-0.2.2-dev.7/independent-native-mac/`，原记录不修改。这不包含Windows运行、真实服务器/上传、真实150%显示缩放或本轮键盘分隔/重启持久化。

首次native-dark.jpg中名称末位0/8可见裁剪，native-light.jpg完整，故外观不能整体验收。dev.8修复的是已复现的初始化缺陷，仍待后续原生首帧复验。

## 定位及最小改动

Fyne的原生驱动在runGL启动时才注册清字体缓存的设置监听；此前先构造表格，再在initChrome中设置所选主题。无窗口驱动按“监听尚未存在”的生命周期建模：旧默认字体测量85.359375px，切到当前PingFang SC后隐式测量仍为旧值，而明确当前字体需要87.515625px，短2.15625px。此前未测量过的新名称0020也能重现，表明不只是单字符串旧metrics。

差异与原图末位裁剪相符，列内空余充足；不把其他GPU问题当已排除。修复仅把主题选择前置到newApp创建Window和首批控件之前，连接管理构造器同样保障该顺序，initChrome不再迟设或重复应用同主题。没有扩大列宽、补空格、定时重绘、强制切换主题或更改系统字体/终端字宽。

修后0000/0004/0008/0012/0016/0020的隐式与明确字体测量完全一致，主题只选择一次。初始明暗偏好、字体策略、虚拟化和实际数据语义沿用dev.7。

## 新身份与证据

- Windows完整应用四入口审查ZIP：`release/Forterm-Native-0.2.2-dev.8-review-windows-x64.zip`；SHA256 `4bbb1f505d416243b753e21188014d246397a2f96ceeb8c4f0151fb9309eb438`。
- 主PE SHA256 `0a958b5c96432feccd37e21a127f6783ecccbdf1209292435324fd48f523803f`。
- Mac arm64离线预览：`native/evidence/ui-0.2.2-dev.8/native/Forterm-Connection-Review-dev8.app`；可执行SHA256 `fd112d047decf054943e76cc644e871f015332dd4b490337a6975c18b6db1d51`，adhoc签名通过，未启动。
- 414条源码与当前文件及冻结tar逐项匹配；检查点 `checkpoints/native-0.2.2-dev.8-review-20261028/`，包含源码/差异/日志/预览IDENTITY。
- manifest SHA256 `60e09710d16153baf8045bc3fdb59252127466fe9802b53a8c2f700bbef91786`；源码tar SHA256 `0aee9e0a380eeae867b4b948cb7346129c55f2083c834dcb29e40cad4deeb009`。
- 相对dev7，5条源文件变化：app版本/早期主题，appearance初始化保障，chrome移除迟设，管理页构造保障，preview app ID；新增font_startup_probe_test.go，其余408条不变。dev7 ZIP和Mac可执行摘要复核保持原值，原图未覆盖。

修前/修后JSON：`native/evidence/ui-0.2.2-dev.8/offline-font-probe-fresh-name/`。日志位于 `native/evidence/performance-0.2.2/`：

- `dev8-before-runGL-fresh-name.log`：独立冷进程重现旧缺陷（含从未预热名称）。
- `dev8-fixed-runGL-fresh-name.log`：修后冷启动测量一致/只选择一次主题，race通过3.444s。
- `dev8-startup-ui-race.log`：相关主题/虚拟表格/系统sans/键盘导航/更多/折叠搜索/预览guard定向race通过22.332s。
- `dev8-vet.log`：通过。没有无变化全量测试或再次抢前台。

## 后续最小原生复验

由经理在获得合适前台时间后协调；本轮未提出新问题或启动dev8。用此独立dev8 app，核对标题 `Forterm UI Review · 0.2.2-dev.8 · PID <实际PID>`。须先在新进程冷启动的深色页保留首张图，尚未切主题、重排、缩放或刷新前检查名称0/8；再切浅色/回深色比较。不得用预热后的完整画面替代首帧。

仍为1000合成配置/拒绝凭据服务/无真实shell或网络的预览；实际终端只是内存输出。无需连接或上传。可用 `FORTERM_UI_PREVIEW_EVIDENCE` 指向本项目证据目录以绑定PID/版本/字体族，原始截图和结果另存dev8目录，不覆盖dev7。

## Windows与上传状态

用户授权复制dev7到 `192.168.2.2 D:\Tools\Forterm-Native-dev7-test`，经理已创建目录，但粘贴未成功后Mac锁屏，目标仍空；解锁问题已一次提出、pending。未成功复制、未启动新Windows EXE。程序员不操作经理独占的远程UI、不重复催问，也不擅自把dev8替换到该目标。

上传现场用户已明确以后再测。当前dev8不是Windows首绘/生产上传已通过的正式版本；本轮不生产上传、终端命令、重登或票据重放，也不混入前台视觉复验。
