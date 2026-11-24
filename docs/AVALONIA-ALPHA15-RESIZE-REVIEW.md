更新：alpha.15修后完整221项/57秒与三包已交付，原整组失败保留；最终身份和主机工具链恢复说明见AVALONIA-ALPHA15-DELIVERY.md，下文为定位过程。

更新（2026-10-31）：首轮完整216中1失败已保留；增加最终布局模型/PTY成对提交及固定5场景稳定性与真实PTY事务断言后，完整221项57秒通过，见alpha15-revised-full-validation.log。当前构建中，尚未原生现场验收。下文为定位记录。

# alpha.15 终端缩放复现与修订验证

用户报告大窗缩小时终端问题。经理只读现场后确认alpha.14本地第二页彩色左右提示重复/断裂/交错；现场before/smaller图窗口未改变，不能把后图当新缩放复现。用户画面仅用于本地查看，不复制到发布证据/源码、不转录内容、不外传。本聊天未读取用户.zshrc/终端字节、未占前台/输入命令/清屏/关闭其会话。

## 独立复现

正常MainWindow内运行自建zsh -f -i，ZDOTDIR/HISTFILE均为自有临时目录；使用自写彩色中文长路径、多行左提示、右提示与Powerline样本。单行未重复，多行大→小→大使同一标记从1变2～3份，缓冲区也重复，排除仅画框/像素层问题。修前合成证据在avalonia/evidence/alpha15-resize-failure-multiline；不含用户提示/配置。

## 修订策略与边界

活动PTY采用稳定物理网格，不主动reflow已经输出的行；shell/全屏程序按真实新尺寸重绘。历史保留原行边界，缩小时右侧裁切，恢复宽度后显示保留的单元与颜色/宽字符。擦除到右/全行会处理旧尾，擦除到左保留未擦右尾，防止旧字复活。没有在resize中发送clear、命令或按键，也没有让用户改主题掩盖缺陷。此策略是行为选择，不能称历史仍会自动重新换行。

同原NuGet版本/提交的MIT源码固定在vendor/XTerm.NET（3.0.0、453ed957…）与vendor/Iciclecreek.Terminal（4.1.2、0851f519…）。原档SHA、修改文件和统一diff随vendor保留。保存光标从屏幕相对行与reflow绝对行混用改为一致绝对锚点；resize跟随逻辑内容/行，覆盖scrollback与pending-wrap。

PTY新绑定和异步spawn后检查最新网格；隐藏/零面积不发临时一格，SSH忽略非正尺寸并接受真实1×1，不再最少2格。更新顺序仍串行且绑定原连接，不新增拖后模型的异步resize队列。

字体单独实测：旧主字体为Menlo，无Powerline样本；本机已有MesloLGS NF、JetBrainsMono/Hack Nerd。原控件theme setter覆盖了属性默认，所以两处都调整为优先已有等宽Nerd。新探测实际MesloLGS NF且Powerline字形可用。未安装字体、未读用户字体配置；不保证没有覆盖字体的机器可显示所有私用符号。

## 定向证据

- 多行提示/右提示/中文/Powerline，普通和快速连续缩放，三种中央分隔与编辑/监控同时展开：无新提示残留，实际连接最后请求匹配网格。
- 真实PTY stty size写入自有文件并回读，与最终rows/cols一致；通过UI输入触发仅子进程输出的随机标记，确认不是输入回显；缩放后实际鼠标选中并复制出完整标记。
- 真正本地LaunchProcess启动时改变布局/随后font zoom；stty与非回显输出正确。
- 两个真实本地PTY同正常MainWindow：缩放第二页不更改隐藏第一页，切回后输入/标记不串页，两个进程持续存活。
- 自有SSH服务真实收到WindowChange；alternate全屏程序按新尺寸重绘并返回独立随机ACK，服务端尺寸/模型一致，文件通道和认证1保留，resize期间输入记录为空。
- 非正SSH尺寸忽略，1×1端到端不擅自变2×2，SFTP仍可用。
- 原行尾/中文宽字符恢复、擦除左右范围、保存光标/历史/待换行模型用例。

12项定向均通过，其后另补四区/三种分隔与选中复制验证通过。上游固定源码测试2312通过、1项原有SKIP。首轮1项因原工程fixture通配未拷贝失败，补原始fixture后通过；之后最终全组2312通过。非回显长标记自然换行，早期测试按物理行找整串导致假失败，已按IsWrapped拼逻辑行；选择起点过贴分隔线误触拖动，改从字符中心验证；SSH旧测试帧在两行视口写超出导致头部滚出，改为有界真正全屏帧，未改生产代码来凑断言。截图等待正常调度绘制后捕获，无终端Refresh/清屏补救。

关键图和数据均在avalonia/evidence/ui-0.3.0-alpha.15：owned-zsh-resize-*-*.png、owned-zsh-resize-four-panels-*.png、owned-local-launch-resize-zoom.png、owned-two-pty-returned-first.png、owned-ssh-resize-alternate-ack.png、owned-local-final-size.json、owned-ssh-resize-protocol.json、owned-font-probe.json。均为软件窗口+真实owned PTY/合成服务，不是用户实际窗口或Windows现场通过。

当前准备工程完整回归与固定源码最终上游复核，尚未alpha.15交付。alpha.14源包与用户会话不改，真实新版本GUI缩放仍需经理协调。参考：zsh维护者2019-07-15的重排/多行提示光标讨论 https://www.zsh.org/mla/workers/2019/msg00561.html 。该资料辅助定位，结论以自有复现/协议回读为准，不把问题直接归咎用户主题。
