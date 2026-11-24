package sshconn

import (
	"context"
	"crypto/rand"
	_ "embed"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"forterm/native/internal/fileops"
	"io"
	"strings"
	"sync"
	"sync/atomic"
	"unicode/utf8"
)

//go:embed privilege_helper.py.txt
var permissionHelper string

const sudoPrompt = "[FORTERM-SUDO-PASSWORD]"

type privilegePayload struct {
	Action   string                 `json:"action"`
	Path     string                 `json:"path"`
	Mode     uint32                 `json:"mode"`
	Expected *fileops.PrivilegeStat `json:"expected,omitempty"`
}

func shellLiteral(value string) string { return "'" + strings.ReplaceAll(value, "'", "'\\''") + "'" }
func permissionScript(item fileops.PermissionItem, action string, expected *fileops.PrivilegeStat) (string, error) {
	if _, e := fileops.SafePath(item.Path); e != nil {
		return "", e
	}
	if !utf8.ValidString(item.Path) || item.Mode > 07777 {
		return "", errors.New("权限目标或模式无效")
	}
	payload, e := json.Marshal(privilegePayload{action, item.Path, item.Mode, expected})
	if e != nil {
		return "", e
	}
	return "LC_ALL=C /usr/bin/python3 -c " + shellLiteral(permissionHelper) + " " + shellLiteral(string(payload)), nil
}
func (c *Connection) InspectPrivilege(ctx context.Context, item fileops.PermissionItem) (fileops.PrivilegeStat, string, error) {
	script, e := permissionScript(item, "inspect", nil)
	if e != nil {
		return fileops.PrivilegeStat{}, "", e
	}
	output, stderr, e := c.permissionExec(ctx, script, nil, "")
	if e != nil {
		return fileops.PrivilegeStat{}, "", errors.New("当前SSH无法用安全只读助手核对路径；需要已安装Python3及独立exec能力，不会自动安装或降级")
	}
	var snapshot fileops.PrivilegeStat
	if json.Unmarshal([]byte(strings.TrimSpace(output)), &snapshot) != nil {
		return snapshot, "", errors.New("安全路径核对返回无效")
	}
	if snapshot.Size != item.Before.Size || snapshot.Mode != fileops.RawMode(item.Before.Mode) || snapshot.ModifiedNS/1e9 != item.Before.Modified.Unix() || snapshot.Directory != item.Directory {
		return snapshot, "", errors.New("exec与SFTP路径元数据不一致，拒绝对未知目标提权")
	}
	apply, _ := permissionScript(item, "apply", &snapshot)
	_, stderr, e = c.permissionExec(ctx, "/usr/bin/sudo -n -u root -l -- "+strings.TrimPrefix(apply, "LC_ALL=C "), nil, "")
	if e == nil {
		return snapshot, "sudo策略可用（无密码或已有有效授权）；执行身份固定root", nil
	}
	low := strings.ToLower(stderr)
	if strings.Contains(low, "tty") || strings.Contains(low, "terminal is required") {
		return snapshot, "", errors.New("sudo策略需要TTY；当前独立安全通道不支持，不绕过")
	}
	if strings.Contains(low, "password is required") || strings.Contains(low, "a password is required") {
		return snapshot, "sudo需要认证；只有明确sudo提示时才会收取本次密码", nil
	}
	return snapshot, "", errors.New("sudo不可用或策略未授权此安全权限命令；未执行变更")
}
func (c *Connection) ApplyPrivilege(ctx context.Context, item fileops.PermissionItem, expected fileops.PrivilegeStat, provider func(context.Context) ([]byte, error)) error {
	script, e := permissionScript(item, "apply", &expected)
	if e != nil {
		return e
	}
	nonce := make([]byte, 16)
	if _, e := rand.Read(nonce); e != nil {
		return e
	}
	prompt := "[FORTERM-SUDO-" + hex.EncodeToString(nonce) + "]"
	_, stderr, e := c.permissionExec(ctx, "/usr/bin/sudo -S -p "+shellLiteral(prompt)+" -u root -- "+strings.TrimPrefix(script, "LC_ALL=C "), provider, prompt)
	if e != nil {
		if strings.Contains(strings.ToLower(stderr), "tty") {
			return errors.New("sudo要求TTY，未降级或写入交互终端")
		}
		return errors.New("sudo权限变更未获确认，可能策略拒绝/认证取消/目标变化；请重新读取，未自动重试")
	}
	return nil
}

type permissionWriter struct {
	mu       sync.Mutex
	data     strings.Builder
	prompted bool
	marker   string
	prompt   func()
}

func (w *permissionWriter) Write(data []byte) (int, error) {
	w.mu.Lock()
	if w.data.Len()+len(data) > 16384 {
		w.mu.Unlock()
		return 0, io.ErrShortBuffer
	}
	w.data.Write(data)
	ask := !w.prompted && w.marker != "" && strings.Contains(w.data.String(), w.marker)
	if ask {
		w.prompted = true
	}
	w.mu.Unlock()
	if ask && w.prompt != nil {
		go w.prompt()
	}
	return len(data), nil
}
func (w *permissionWriter) text() string { w.mu.Lock(); defer w.mu.Unlock(); return w.data.String() }
func (c *Connection) permissionExec(ctx context.Context, command string, provider func(context.Context) ([]byte, error), marker string) (string, string, error) {
	if !c.privilegePending.CompareAndSwap(false, true) {
		return "", "", errors.New("上个权限查询尚未释放，未叠加")
	}
	type result struct {
		out, errText string
		err          error
	}
	answer := make(chan result)
	go func() {
		defer c.privilegePending.Store(false)
		session, e := c.client.NewSession()
		if e != nil {
			select {
			case answer <- result{err: e}:
			case <-ctx.Done():
			}
			return
		}
		defer session.Close()
		stop := context.AfterFunc(ctx, func() { session.Close() })
		defer stop()
		in, e := session.StdinPipe()
		if e != nil {
			select {
			case answer <- result{err: e}:
			case <-ctx.Done():
			}
			return
		}
		defer in.Close()
		stdout, stderr := &permissionWriter{}, &permissionWriter{marker: marker}
		var sent atomic.Bool
		stderr.prompt = func() {
			if provider == nil || !sent.CompareAndSwap(false, true) {
				in.Close()
				return
			}
			password, e := provider(ctx)
			if e == nil && !strings.ContainsAny(string(password), "\r\n\x00") && len(password) <= 8192 && ctx.Err() == nil {
				line := make([]byte, len(password)+1)
				copy(line, password)
				line[len(password)] = '\n'
				_, e = in.Write(line)
				clear(line)
			}
			clear(password)
			in.Close()
			if e != nil {
				session.Close()
			}
		}
		session.Stdout = stdout
		session.Stderr = stderr
		e = session.Run(command)
		value := result{stdout.text(), stderr.text(), e}
		select {
		case answer <- value:
		case <-ctx.Done():
		}
	}()
	select {
	case r := <-answer:
		return r.out, r.errText, r.err
	case <-ctx.Done():
		return "", "", fmt.Errorf("权限exec取消或超时：%w", ctx.Err())
	}
}
