Forterm Studio

适用：Windows 10 / 11（x64）。不需要另外安装 .NET。

一、四个程序
  Forterm.exe            主程序，双击打开
  xshell.exe             给控制器打开 SSH 终端
  xftp.exe               给控制器打开文件传输（SFTP；ftp:// 打开 FTP）
  forterm-diagnose.exe   只检查启动参数，不联网

  这些程序要放在同一个文件夹里，不能单独拷走 xshell.exe 或 xftp.exe。

二、接入控制器（SSO 或堡垒机客户端）
  在控制器里把 Xshell、Xftp 的程序位置改成本文件夹里的 xshell.exe、xftp.exe，参数不用改。
  完整路径可以在 Forterm 的“系统配置 → 控制器接入”里复制。
  点了没反应时，看那一页的“最近的唤醒”：没有记录，多半是控制器没有启动这两个程序。

三、先自己试一下（PowerShell，在本文件夹里）
  .\xshell.exe --diagnose -url "ssh://用户名:密码@主机:22"
  echo $LASTEXITCODE
  只检查参数，不联网。0 为正常；2 为参数不被接受，或缺少 Forterm.exe。
  用户名、密码里的特殊字符写成百分号编码，例如 @ 写 %40，: 写 %3A，/ 写 %2F，# 写 %23。

四、数据
  设置和连接在 %LOCALAPPDATA%\Forterm\Studio，密码只在 Windows 凭据管理器里。
  删除或升级程序不会动它们。控制器的临时票据只在内存里使用，不保存，断线后不会自动重连。

程序没有数字签名，第一次运行时 Windows 可能提示，选“更多信息 → 仍要运行”。
