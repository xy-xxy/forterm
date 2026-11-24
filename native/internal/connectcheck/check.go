// Package connectcheck performs one explicit, bounded connection check. It
// never opens a shell/exec stream, writes remote files, or persists credentials.
package connectcheck

import (
	"context"
	"errors"
	"fmt"
	"forterm/native/internal/ftpconn"
	"forterm/native/internal/launch"
	"forterm/native/internal/sshconn"
)

type Result struct {
	Success, Authenticated bool
	Stage, Message, Files  string
}

func Run(ctx context.Context, c launch.Connection, callbacks sshconn.Callbacks, progress func(string)) Result {
	emit := func(s string) {
		if progress != nil {
			progress(s)
		}
	}
	if c.Protocol == "ftp" {
		conn, err := ftpconn.DialWithStatus(ctx, c, func(stage string) {
			switch stage {
			case "network":
				emit("正在连接FTP网络…")
			case "network_ready":
				emit("FTP网络已连接，正在认证…")
			case "authenticated":
				emit("FTP已认证，读取当前目录…")
			}
		})
		if err != nil {
			if ctx.Err() != nil {
				return stopped(ctx)
			}
			var failure *ftpconn.ConnectError
			stage := "input"
			if errors.As(err, &failure) {
				stage = failure.Stage
			}
			return Result{Stage: stage, Message: err.Error(), Files: "未验证"}
		}
		defer conn.Close()
		return Result{Success: true, Authenticated: true, Stage: "complete", Message: "FTP登录及当前目录读取通过；FTP不提供SSH主机指纹验证", Files: "控制通道可用；数据通道、上传和写入未测试"}
	}
	callbacks.KeepAlive = &sshconn.KeepAlive{Enabled: false}
	prior := callbacks.Progress
	callbacks.Progress = func(event sshconn.ConnectEvent) {
		if prior != nil {
			prior(event)
		}
		prefix := "目标"
		if event.Total > 1 {
			prefix = fmt.Sprintf("第%d/%d跳", event.Hop, event.Total)
		}
		labels := map[sshconn.ConnectStage]string{sshconn.StageNetwork: "连接网络", sshconn.StageNetworkReady: "网络已连接", sshconn.StageFingerprint: "等待确认主机指纹", sshconn.StageTrusted: "主机指纹已确认", sshconn.StageAuthentication: "正在认证", sshconn.StageAuthenticated: "SSH认证通过"}
		if label := labels[event.Stage]; label != "" {
			emit(prefix + " · " + label)
		}
	}
	conn, err := sshconn.Dial(ctx, c, callbacks)
	if err != nil {
		if ctx.Err() != nil {
			return stopped(ctx)
		}
		var failure *sshconn.ConnectError
		stage := "input"
		if errors.As(err, &failure) {
			stage = string(failure.Stage)
		}
		return Result{Stage: stage, Message: err.Error(), Files: "未验证"}
	}
	defer conn.Close()
	emit("SSH认证通过，测试SFTP子系统通道…")
	if err := conn.ProbeSFTP(ctx); err != nil {
		if ctx.Err() != nil {
			return Result{Authenticated: true, Stage: "files", Message: "SSH认证通过，文件通道探测超时或已取消", Files: "未确认；未启动终端"}
		}
		return Result{Success: c.Protocol != "sftp", Authenticated: true, Stage: "files", Message: "SSH认证通过；SFTP子系统不可用，其他已授权能力不受此次测试影响", Files: err.Error()}
	}
	return Result{Success: true, Authenticated: true, Stage: "complete", Message: "网络、SSH主机指纹及认证通过", Files: "SFTP子系统通道可用；目录/上传/编辑/exec和shell未测试"}
}
func stopped(ctx context.Context) Result {
	if errors.Is(ctx.Err(), context.DeadlineExceeded) {
		return Result{Stage: "timeout", Message: "测试超时，连接已关闭，未自动重试", Files: "未确认"}
	}
	return Result{Stage: "cancelled", Message: "测试已取消，未保存或自动重试", Files: "未确认"}
}
