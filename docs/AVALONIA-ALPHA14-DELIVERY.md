# Forterm Avalonia 0.3.0-alpha.14 连接表单与工具栏修订交付（2026-10-30）

本轮根据经理获准实际运行alpha.13的空配置原生窗口发现的问题，完成连接表单与主界面精修。alpha.13冻结包、原运行PID38177与空配置目录保留；未占前台、未向窗口输入或关闭旧会话。末次只读检查PID38177仍运行，原主入口SHA8e2578dc…未变。

## 原生证据的确切范围

经理实际点击新建、滚动0→259、私钥页继续到487、底部字段/取消可达及深浅切换通过。原五图与identity完整像素留档、SHA在avalonia/evidence/independent-native-empty-alpha13-20261030/PROVENANCE.json。没有连接、测试认证、上传或读取用户凭据。该有限alpha.13空窗复核不能称为alpha.14原生GUI、真实四区SSH、Windows、picker或vault通过。

## 本版修复

- 根因是私钥导航回调直接设置authMode.SelectedIndex=1。现在“私钥设置”仅展开/定位此节，保持原认证方式、密码可用性、输入、保存勾选与系统秘密策略。非私钥方式的私钥节为禁用预览，说明应在身份认证下拉框明确选择；选择认证本身仍不写保管库，明确保存才执行已有策略。
- 五种协议/认证组合（SSH密码/私钥/交互/agent、FTP密码）都验证导航无副作用和零vault调用。显式下拉选私钥后才启用私钥字段；文件选择也在忙碌时禁用，标题明确“选择私钥文件”。
- 表单220dp说明列、24dp列间gutter，说明改为完整短句/有意换行；480窄窗上下排与10dp间隔。改善了贴控件与“令”孤行。分节导航为轻量样式。
- 连接库标题独占一行，操作间隔10dp；顶部使用轻量工具栏并突出新建。普通主窗状态为“就绪”，移除表单迁移卡；版本/实现与验证边界在“关于与验证说明”。关于页有滚动正文和固定关闭，不读vault/不访问网络。
- alpha.13的左右工作区、环形虚拟逐核监控、26dp队列与其余能力保留，未更改协议入口或临时票据规则。

## 验证

完整单次204项46秒通过：avalonia/evidence/alpha14-final-validation.log。新增三个软件用例覆盖五种导航组合、显式认证选择、深浅900/760/700/480表单gutter/底部可达、连接库标题/间距及关于说明/无vault调用；原认证失败保留/显式保存和工作区等回归通过。首轮gutter测试把隐藏私钥行也作已布局控件，断言失败日志保留；已改为检查真实显示的行，未为此修改生产布局规则。

82张软件图及尺寸/SHA：avalonia/evidence/ui-0.3.0-alpha.14/SCREENSHOTS.json。已实际查看主要图：key-navigation-preserves-password.png、key-explicit-auth-selection.png、connection-polish-Dark-900-top.png、Dark-700-top.png、Light-480-bottom.png、library-polish-1150.png、library-polish-700.png、about-verification-details.png；它们是本版软件渲染，不是原生启动证明。

## 冻结身份与三包

- sourceFiles: 542
- sourceManifestSHA256: 4d619db639f0e742bb588cbf51b777926904aafee6a1831b9b4e57c4390deab7
- sourceArchiveSHA256: 5ce47fd40648db3bdf661db2f345ef27cda2644b9714b157286f245455d7f3f9
- checkpoint: /Users/xianyu/IdeaProjects/forterm/checkpoints/avalonia-0.3.0-alpha.14-20261030
- windowsMainExeSHA256: 03286ec9f5962cf3155742ce485f847374b4c4c0b90863254dd62d83fb44dc89
- macExecutableSHA256: 2fe5ed4b532a3cf8bba275886074f2172818907f900d502b19005c049fb01596
- ZIP: Forterm-Avalonia-0.3.0-alpha.14-windows-x64.zip；52,411,821字节；SHA256 6b8701d09e2736533fd5c7f09d5161936f5892e07b3b4ebd306551c58464716c
- installer: Forterm-Avalonia-0.3.0-alpha.14-windows-x64-setup.exe；51,204,989字节；SHA256 aa585cf6564c937d71576a0ab3be66f89369dcd697d91153a1458117aad960fd
- portable: Forterm-Avalonia-0.3.0-alpha.14-windows-x64-portable.exe；125,965,363字节；SHA256 3af9ed83e51088aa677d37e2667ff485211e746a641144f0f425eb766d9b6379

ZIP CRC/四AMD64入口与293载荷通过；安装器解包293文件匹配，C#便携内嵌精确原ZIP和托管模块匹配，同源码Mac校验器实际解压/哈希最终ZIP且零窗口/零子进程。175项bundle元数据/管理运行库与来源一致。240PE常规+延迟表/18Mac原生依赖无未解析/VC外部导入；外壳两种另查双表，NSIS引导壳x86、便携和应用载荷AMD64。静态检查不等于干净Windows执行，icu.dll Windows10 1903+边界保留。正常无PDB，符号另存。

Mac正常包adhoc深签名及无窗口Skia/HarfBuzz/xftp合成诊断通过，542源码与冻结清单一致。IDENTITY中的installerPortableGenerated=false是初次冻结阶段；最终真实三包状态以DELIVERY/VERIFICATION-windows为准，不改写冻结历史。

本版待独立复核。原生alpha.14/Windows/IME/物理缩放/系统picker/真实OS vault/生产堡垒机仍未验，旧Go生产上传问题未据空窗UI结案，门户脱敏契约仍缺。更新说明/未勾选现场清单/SHA随包与release提供。本聊天仅代码/软件测试/本地打包与文档，未占屏、生产连接/上传、真实agent/vault访问、重放票据/SSO/审批、推送或发布，也未给其他聊天发消息。
