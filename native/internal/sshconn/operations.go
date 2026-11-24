package sshconn

import (
	"context"
	"errors"
	"github.com/pkg/sftp"
	"os"
	"time"
)

func (c *Connection) RealPath(parent context.Context, remote string) (string, error) {
	ctx, cancel := context.WithTimeout(parent, 10*time.Second)
	defer cancel()
	var resolved string
	e := c.fileCall(ctx, func(f *sftp.Client) error { var e error; resolved, e = f.RealPath(remote); return e })
	return resolved, e
}
func (c *Connection) ChmodSupported() bool { return true }
func (c *Connection) Mutate(parent context.Context, kind, first, second string, mode os.FileMode) error {
	ctx, cancel := context.WithTimeout(parent, 15*time.Second)
	defer cancel()
	return c.fileCall(ctx, func(f *sftp.Client) error {
		switch kind {
		case "mkdir":
			return f.Mkdir(first)
		case "rename":
			return f.Rename(first, second)
		case "replace":
			if _, ok := f.HasExtension("posix-rename@openssh.com"); !ok {
				return errors.New("服务器不支持安全覆盖重命名")
			}
			return f.PosixRename(first, second)
		case "remove":
			return f.Remove(first)
		case "rmdir":
			return f.RemoveDirectory(first)
		case "chmod":
			return f.Chmod(first, mode)
		default:
			return errors.New("不支持的文件操作")
		}
	})
}

func (c *Connection) SupportsSafeReplace() bool {
	c.fileMu.Lock()
	defer c.fileMu.Unlock()
	if c.file == nil {
		return false
	}
	_, ok := c.file.HasExtension("posix-rename@openssh.com")
	return ok
}
