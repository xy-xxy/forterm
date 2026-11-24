# Forterm Avalonia 0.3.0-alpha.17 按钮双向居中交付（2026-10-31）

按用户“文字靠上，左右也要居中”的最新要求，通用Button水平和垂直ContentAlignment均明确Center；连接分类按钮的额外Left覆盖同步改Center。原Fluent ContentPresenter实测会绑定两属性，因此修复的是实际父级内容布局，并非只设TextBlock.VerticalAlignment。标准按钮模板和hover/pressed/disabled/键盘focus保持。

普通34dp、图标30dp、队列26dp及既有局部小按钮尺寸、对称padding保留，无逐按钮负margin或全局扩控件。图文组合整体居中并统一6dp间距，原Content/自动化名称保持；⌄因字体基线低而改成内置14dp矢量箭头，无新图库或字体安装。树、表格正文和菜单列表项未居中。

## 同尺寸修前后

修前直接引用冻结alpha.16精确DLL；修后为alpha.17实际代码。均软件Skia渲染，非用户前台截图。

| 场景 | 修前 | 修后 |
| --- | --- | --- |
| 深色矩阵100%，13/14/16中英文 | [修前](/Users/xianyu/IdeaProjects/forterm/avalonia/evidence/button-alignment-before-alpha16/matrix-Dark-1.00.png) | [修后](/Users/xianyu/IdeaProjects/forterm/avalonia/evidence/ui-0.3.0-alpha.17/matrix-Dark-1.00.png) |
| 浅色矩阵150%，13/14/16中英文 | [修前](/Users/xianyu/IdeaProjects/forterm/avalonia/evidence/button-alignment-before-alpha16/matrix-Light-1.50.png) | [修后](/Users/xianyu/IdeaProjects/forterm/avalonia/evidence/ui-0.3.0-alpha.17/matrix-Light-1.50.png) |
| 浅色连接页125%，分类及底栏 | [修前](/Users/xianyu/IdeaProjects/forterm/avalonia/evidence/button-alignment-before-alpha16/connection-Light-1.25.png) | [修后](/Users/xianyu/IdeaProjects/forterm/avalonia/evidence/ui-0.3.0-alpha.17/connection-Light-1.25.png) |

完整修前18张和当前101张截图分别有来源/尺寸/SHA；另有125%/150%、两主题、确认键盘focus/pressed/hover/26dp队列等图。Button与ContentPresenter双属性及child真实中心几何逐项断言。126组前景墨迹测量最大全局中心偏移水平23→1.667dp、垂直8.4→1dp，残差包含字形bearing/抗锯齿，不保证所有系统字体墨迹精确零偏差。实际主要图已查看。

## 验证与保留记录

20项相关UI回归全部通过，16秒，`avalonia/evidence/alpha17-final-validation.log`。覆盖按钮矩阵/真实队列鼠标取消/Space按下释放及确认、连接底栏/导航/顶部工具栏、正常四区、FTP文件动作、复制移动/小队列、布局。未重跑无变化旧227全量/上游模型/终端缩放。alpha.16的完整227项及经理独立Mac合成多行启动/缩放有限通过仍属该版历史基线，不冒称本版全量或原生通过。

第一轮26dp等于actual bounds断言在125%像素舍入为26.4dp失败，原日志保留；修正检查声明26dp与<=1物理像素舍入，未扩大控件。首轮部分Light主/连接图被主窗口偏好重置成Dark，旧图和通过日志保留alpha17-before-theme-isolation-*；最终每窗明确theme并断言ActualThemeVariant，重新生成真实浅色修前后。详release的VALIDATION。

冻结后精确Mac DLL再做双主题/三比例样式/正常主窗/连接窗软件复核，18图、双属性/child中心/ActualTheme断言通过，日志alpha17-frozen-button-check.log，DLL SHA对应PAYLOADS。此为包内软件复验，未打开native前台窗口。

## 冻结交付

源码670文件；manifest SHA `05b2d5babf9d4392676931b7853809bfb817a9f4b8db8e014c91d86f66496f11`；source archive SHA `a0cb961f86d7b01d66a11218a9fcce1fd874c97218fa4aba14cab2fe9b8b4515`。恢复点 `/Users/xianyu/IdeaProjects/forterm/checkpoints/avalonia-0.3.0-alpha.17-20261031`。

- [Mac arm64完整候选](/Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.17-macos-arm64.app)；apphost SHA `3ba67b420f09cb5078a699076591b9dca3f3bf9f36e811e95faa9b094fee6123`。独立默认配置realm AvaloniaAlpha17，未自动打开。
- [Windows四入口 ZIP](/Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.17-windows-x64.zip)；52418142字节；SHA `27a059edde091e72e76187984ebd37c523f529dbd0801b048dfd3bcc8f04900c`。
- [Windows安装包](/Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.17-windows-x64-setup.exe)；51214042字节；SHA `654dc96c43b3ea51419fc22751c2d89505b227bc347354f5516c05417379f571`。
- [Windows便携版](/Users/xianyu/IdeaProjects/forterm/release/Forterm-Avalonia-0.3.0-alpha.17-windows-x64-portable.exe)；125969459字节；SHA `adad8d59f041c895f52f3a31fde00fd4dc563addb3ceaf76edde7278b0ff3925`。
- [15条校验索引](/Users/xianyu/IdeaProjects/forterm/release/SHA256SUMS-Avalonia-0.3.0-alpha.17.txt)；更新说明、现场清单、DELIVERY/PAYLOADS/SIZE/VERIFICATION/BUILD-TOOLS/OUTER-PE与symbols同目录。

293载荷在三包一致，ZIP CRC/NSIS解包/便携原ZIP与管理模块/bundle运行时逐项SHA和同源码Mac解压器零窗口零子进程核对；240PE常规与延迟/18Mac依赖无未解析/外部VC，outer双表另核。Mac adhoc深签名、无窗口Skia/HarfBuzz及合成xftp诊断通过，未消费系统凭据或生产票据。IDENTITY的installerPortableGenerated=false属于初冻历史，最终状态见DELIVERY/VERIFICATION。

本版实际Mac窗口像素复核仍由经理协调，未占屏/关闭既有窗口；Windows物理DPI/ConPTY/IME/picker/vault/真实堡垒机/生产上传仍待。旧alpha.16及更早包和用户会话/配置保持；无跨聊天发信/推送发布。不为等待制造新改动或重复无变化测试。
