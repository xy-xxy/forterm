# 0.2.2-dev.8 首绘字体初始化顺序修复候选

dev.7经理已按用户许可实际运行Mac离线预览PID92827并正常退出，确认深浅管理页、静态四区、导航实拖约40逻辑px、小窗固定更多/刷新和导航收起。原始7图/identity/result及摘要保留在evidence/ui-0.2.2-dev.7/independent-native-mac。只属于Mac真实UI+合成静态内容，不是Windows、实际150%显示缩放、服务器/上传或本轮键盘/重启持久化通过。

发现首次native-dark.jpg的部分名称末位0/8被裁，切浅色及重排后完整，外观尚未全部验收。dev.8仅修初始化顺序：选择持久化主题在newApp创建窗口/首批控件前完成，连接管理构造器也保障该顺序；initChrome只校验同主题，不再迟设/重复应用。没有增加列宽、补空格、强制切换主题或改字体文件/终端栅格。

离线定位证据：原生Fyne字体缓存重置监听在runGL启动时才注册；dev7此前在表格控件创建后、runGL前才SetAppearance。无窗口驱动按“监听尚未注册”的生命周期建模，复现旧默认字体缓存测量85.359375px，而当前PingFang SC需87.515625px，短2.15625px；未预热过的新名称0020也短同样宽度，说明不只是单个字符串的旧metrics。这个差异与原图选择性末位裁剪相符，不把其他GPU因素当已排除。

修后同组名称的隐式/显式字体测量一致，且所选主题仅应用一次。证据offline-font-probe-fresh-name内修前/修后JSON；dev8-before-runGL-fresh-name.log、dev8-fixed-runGL-fresh-name.log；修后race3.444s、相关UI定向race22.332s、vet通过。未重跑无变化全量，没有再次占前台。

原生dev8首帧仍待后续协调复验，不能把离线probe当实际GPU首绘通过。当前新Windows x64四入口审查ZIP及Mac arm64离线预览使用dev8身份，不覆盖dev7源码/包/原图。UI样式、系统CJK sans策略、受限预览、文件传输和认证业务均沿用dev7；只更改主题初始化/身份和定向测试。

Windowsdev7文件复制已获授权但仍未成功：经理已建D:\Tools\Forterm-Native-dev7-test目录，粘贴受阻后Mac锁屏，解锁请求pending。未复制、未启动Windows新EXE；该协调由经理负责，程序员不触屏、不催问或擅自改传dev8。用户说上传以后再测，仍不混入生产上传/终端命令/重登/票据重放。
