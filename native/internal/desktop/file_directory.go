package desktop

import (
	"context"
	"errors"
	"fmt"
	"forterm/native/internal/remotefs"
	"github.com/pkg/sftp"
	"path"
	"strings"
)

func absoluteDirectory(p string) bool {
	return strings.HasPrefix(p, "/") && path.Clean(p) == p && len(p) <= 8192 && !strings.ContainsAny(p, "\x00\r\n\\")
}
func readDirectory(ctx context.Context, fs remotefs.Connection, target string) (string, []remotefs.Entry, string, error) {
	resolved := target
	notice := ""
	if op, ok := fs.(remotefs.Operations); ok {
		p, e := op.RealPath(ctx, target)
		if e != nil {
			var status *sftp.StatusError
			// A known unsupported REALPATH may be bypassed only for an explicitly
			// entered absolute path, and only after LSTAT proves it is a directory.
			if absoluteDirectory(target) && errors.As(e, &status) && status.Code == 8 {
				info, err := fs.Stat(ctx, target)
				if err != nil {
					return "", nil, "", fmt.Errorf("服务器不支持REALPATH；确认绝对目录失败（LSTAT），未上传或切换目录：%w", err)
				}
				if info == nil || !info.IsDir() {
					return "", nil, "", errors.New("输入的绝对路径不是已确认目录（LSTAT），未上传或切换目录")
				}
				notice = "服务器不支持规范路径解析，已读取你指定的绝对目录"
			} else {
				return "", nil, "", fmt.Errorf("确认远程目录失败（REALPATH）：%w", e)
			}
		} else {
			resolved = p
		}
	}
	if !absoluteDirectory(resolved) {
		return "", nil, "", errors.New("服务器未返回安全绝对目录；请明确输入绝对目录后刷新")
	}
	entries, e := fs.List(ctx, resolved)
	if e != nil {
		return "", nil, "", fmt.Errorf("读取远程目录失败（LIST）：%w", e)
	}
	return resolved, entries, notice, nil
}

// readExactDirectory is used only after a separate explicit UI confirmation.
// It never tries a relative path or writes anything to the remote machine.
func readExactDirectory(ctx context.Context, fs remotefs.Connection, target string) (string, []remotefs.Entry, string, error) {
	if !absoluteDirectory(target) {
		return "", nil, "", errors.New("请明确输入安全的绝对目录；不会推断工作目录")
	}
	info, e := fs.Stat(ctx, target)
	if e != nil {
		return "", nil, "", fmt.Errorf("直接读取前确认目录失败（LSTAT）：%w", e)
	}
	if info == nil || !info.IsDir() {
		return "", nil, "", errors.New("直接读取的目标不是普通目录（LSTAT），已停止")
	}
	entries, e := fs.List(ctx, target)
	if e != nil {
		return "", nil, "", fmt.Errorf("直接读取目录失败（LIST）：%w", e)
	}
	return target, entries, "已按你确认的绝对路径读取；未执行REALPATH", nil
}
