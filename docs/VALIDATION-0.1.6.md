# Forterm 0.1.6 阶段版验证

2026-10-26。本版交付“关联文件标签隐藏”的实际界面改动，并包含待适配的门户接入基础。门户登录/资产接口尚未接通，不以框架或合成测试替代厂商联调。没有访问生产门户、旧凭据、审批或SSO接口。

## 最终0.1.6构建上的检查

| 检查 | 结果 / 证据（test-results/） |
| --- | --- |
| TypeScript / 构建 | typecheck-0.1.6.log、build-0.1.6.log通过；renderer体积提示不影响构建 |
| 真实关联文件标签 | linked-tabs-0.1.6.log：SFTP/FTP明确关联后隐藏，切换/解除/最后所属工作区关闭恢复，共享源仍有关联则保持隐藏；实际隐藏源编辑/保存、dirty切换保护、快捷键/关闭其他跳过隐藏源、外部聚焦转所属工作区；初始3次SSH认证，无新增认证、pageerror=0 |
| 生产未适配界面 | bastion-access-ui-0.1.6.log：登录禁用、无假资产、受限IPC/无任意HTTP代理、0门户请求，测试提供器不在主程序bundle；150%原生完整窗口中的登录控件可见 |
| 原入口UI smoke | ui-0.1.6.log：真实本地PTY、保存连接、诊断脱敏、Xftp路由、新窗口、共享会话移除 |
| 11进程实际去重 | burst-0.1.6.log：11诊断进程→1报告/计数11/0socket；11SSH进程→1会话/1socket；不同合成票据独立 |
| Windows启动器 | launchers-0.1.6.log：Go x64四入口中的3个转交入口构建/复制；主入口为Electron |
| 包与校验 | verification-0.1.6.log / VERIFICATION-0.1.6.json：ZIP CRC、四x64 PE、ZIP与win-unpacked四入口/asar一致、包内版本0.1.6、6个dist与asar逐字节一致、Windows ConPTY及三包SHA256 |

关联标签与待适配界面的0.1.6原生截图在screenshots-0.1.6/，已逐张查看。合成资产名/端口及内容仅用于测试。截图中的关联SFTP源已隐藏，未关联的备用FTP标签仍可见，底层活动连接数保持。

## 已有定向证据的复用边界

此前0.1.6-dev.1接入基础的21项定向测试（14服务+4账户+3TOTP）通过；覆盖HTTP登录/MFA/过期、分页/账号协议、审批三分支、重复点击、一次性兑换未知不重放、晚到结果/注销/配置变化与SSH生命周期、真实通道降级及公开投影/错误不含秘密。本阶段未改这些被测源文件，逐文件摘要核对见FOUNDATION-EVIDENCE-0.1.6.json，未把旧测试伪称为本轮重跑。

关联标签的开发快照也有经理独立复核：/tmp/forterm-manager-linked-tabs.json，renderer index--ae0S7lf.js SHA256 cb2683190c88ebbc11e3bcbdb9a95916bec94df5d4a8a29823f953fda674d5f6。经理确认同代理IP不自动隐藏、隐藏源实际编辑、共享/解除/最后所属工作区关闭后恢复，3次初始认证无额外重登，pageerror0。该结果对应开发快照，不冒充最终三包独立验收。

0.1.5的P0上传、BOM及无变化的全量旧协议没有重复运行；其已验证行为与真实限制继续参见VALIDATION-0.1.5.md。0.1.5包的SHA256在本轮核对保持不变。

## 交付与未完成项

新包为Forterm-0.1.6-x64.zip（四入口）、Forterm-0.1.6-x64-setup.exe、Forterm-0.1.6-x64-portable.exe，配套SHA256SUMS-0.1.6.txt和验证JSON。未签名、未上传/推送/发布；旧版包均保留，源码仍处于既有未跟踪状态，另有checkpoints/本地源码恢复点。

宿主为macOS，执行的是本机真实Electron与合成回环HTTP/SSH/SFTP/FTP，Windows包为交叉构建与静态核验，**不是Windows原生或生产堡垒机验收**。待验证项目在WINDOWS-ACCEPTANCE-0.1.6.md。最终包仍供经理独立复核。

门户具体提供器为空；目前不能直接登录堡垒机/查看真实授权资产。仍缺厂商/版本匹配的登录/MFA/资产账号/审批及sid之后最终连接来源的契约，参见BASTION-CONTRACT-TEMPLATE.md。没有猜华为CBH、重放旧Cookie/票据或创建假登录。FTP仍无安全在线编辑/排他新建，证书/SDK/隔离网页登录未适配；详情见BASTION-ACCESS-DESIGN.md及README。
