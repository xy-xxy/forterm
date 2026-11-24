package ftpconn

import (
	"context"
	"fmt"
	"os"
	"strings"
	"time"
)

func (c *Connection) RealPath(ctx context.Context, remote string) (string, error) {
	return c.resolve(remote)
}
func (c *Connection) ChmodSupported() bool { return c.chmodSupported }
func (c *Connection) Mutate(parent context.Context, kind, first, second string, mode os.FileMode) error {
	ctx, cancel := context.WithTimeout(parent, 15*time.Second)
	defer cancel()
	a, e := c.resolve(first)
	if e != nil {
		return e
	}
	b := ""
	if second != "" {
		b, e = c.resolve(second)
		if e != nil {
			return e
		}
	}
	return c.run(ctx, func() error {
		switch kind {
		case "mkdir":
			return c.client.MakeDir(a)
		case "rename":
			return c.client.Rename(a, b)
		case "remove":
			return c.client.Delete(a)
		case "rmdir":
			return c.client.RemoveDir(a)
		case "chmod":
			if !c.chmodSupported {
				return localFailure{"FTP服务器未确认SITE CHMOD能力"}
			}
			raw := uint32(mode.Perm())
			if mode&os.ModeSetuid != 0 {
				raw |= 04000
			}
			if mode&os.ModeSetgid != 0 {
				raw |= 02000
			}
			if mode&os.ModeSticky != 0 {
				raw |= 01000
			}
			code, _, err := c.client.Command(fmt.Sprintf("SITE CHMOD %o %s", raw, a))
			if err != nil {
				return err
			}
			if code >= 400 {
				return localFailure{"FTP权限修改被拒绝，未按请求应用"}
			}
			return nil
		default:
			return localFailure{"不支持的FTP文件操作"}
		}
	})
}
func helpSupportsChmod(message string) bool {
	for _, word := range strings.Fields(strings.ToUpper(message)) {
		if strings.Trim(word, " ,;:()[]") == "CHMOD" {
			return true
		}
	}
	return false
}
