# Forterm Native 0.2.1

独立 Go + Fyne 原生桌面工作区，不含 Chromium、Electron、Node、WebView2。Fyne自行绘制界面，并非 Windows 系统控件库。Electron 0.1.7、历史原型和各自配置均保留。

## 功能

- 同会话 SSH 终端、SFTP、原生在线代码编辑和 Linux 资源监控；独立FTP可作为明确关联的文件来源。
- 显式多文件来源标签，目录/筛选/排序/滚动独立；被引用的文件协议会话隐藏顶部重复标签，最后引用解除恢复。草稿始终绑定原来源，不按IP自动合并或重新授权。
- 原生 VT：ANSI/备用屏幕、UTF-8/CJK/grapheme/emoji、10k历史、前后搜索、选择/复制、bracketed paste、完整多行预览确认。双终端左右/上下分屏，输入对应真实会话。
- 流式普通文件上传下载，无原型1MiB限制；每连接串行队列、批量/追加/进度/取消/停滞/元数据超时。失败或取消停止其余排队，不自动重传；取消下载保留原本地目标，上传中断可能保留部分远程文件。
- 文件新建目录、改名、边界规划递归删除、权限/特殊位保留、右键复制路径；删除2000项/32层/两分钟限制，不跟随链接或盲目删除并发新子项。
- 代码编辑器：行号、语法高亮、缩进线、当前行、撤销/重做、语言/缩进/空白设置；UTF-8 BOM/LF/CRLF/尾空格保留，混合换行明确转换前只读；普通文本1MiB限制，双次远程变化检查、0600初始临时创建、原子替换、首存排他、保存确认/dirty关闭保护。FTP安全在线编辑仍明确不可用，与0.1.7边界一致。
- 默认深色/同步浅色、导航/文件/监控收起、拖动及键盘分隔、独立首选/字号/主题持久化。
- 连接分组/导入导出、持久主机指纹、密码/加密私钥/认证代理/交互认证/SSH跳板；明确保存的密码和TOTP key仅存OS凭据服务，不在配置JSON或导出中。临时票据仅内存、不保存、不自动重连。
- 默认15秒可设置SSH协议心跳；只对已打开SFTP做只读探测，不向交互终端输入命令或按键。监控手动或主动开启自动刷新，有超时、防叠加、隐藏暂停，指标缺失不伪造数据。

## Windows 使用

安装版安装到独立的 Forterm Native 目录，不覆盖 Electron Forterm。四入口ZIP需完整解压，四个EXE保持同目录：

- `forterm-native.exe`：原生主程序
- `xshell.exe`：SSH兼容入口
- `xftp.exe`：SFTP/FTP兼容入口
- `forterm-diagnose.exe`：脱敏参数诊断，不发起连接

```powershell
.\xshell.exe -newtab "会话名称" -url "ssh://username:password@host:22"
.\xftp.exe -url "sftp://username:password@host:22"
.\forterm-diagnose.exe -url "ssh://username:password@host:22"
```

示例为格式，必须使用自己的新凭据。URL仅解码一次；不经shell转发，三秒HMAC内存去重。新原生版与历史原型使用独立实例目录。旧 safeStorage 密文不会尝试解密；导入旧无秘密连接时重新分配ID，不复用旧凭据。

## 构建和验证

Go 1.26、平台C/图形工具链；依赖版本锁定go.mod/go.sum，原生VT/SFTP/FTP的本地修订保留于third_party及各LICENSE/PATCHES说明。Windows构建用Clang前端与MinGW-w64头文件、库和PE链接器：

```sh
go test ./internal/...
go test -race ./internal/...
go build -o forterm-native ./cmd/forterm
CC="$PWD/scripts/clang-mingw.sh" ./scripts/build-windows.sh
```

明确命名的 `cmd/*-smoke` 是本机合成测试入口，不编入主程序、不使用真实票据、不访问生产SSO/审批。证据与状态见MIGRATION.md、VALIDATION.md和docs/PROGRESS.md。

## 真实边界

本机为macOS arm64：原生窗口、回环协议和系统钥匙串已实际运行；Windows x64程序/安装包可交叉构建，但ConPTY、SID命名管道、剪贴板/IME/DPI、Windows凭据、安装卸载仍需要Windows实机验收。未签名，未推送或发布。

堡垒机厂商门户登录/资产API仍等待脱敏契约；这是0.1.7也未交付的独立能力，账户/TOTP本地管理不代表已经接通门户。不猜测厂商或端点，不重放历史秘密。
