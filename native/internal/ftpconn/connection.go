// Package ftpconn implements an independent native FTP control connection. It
// never silently upgrades/downgrades to SSH or replays login credentials.
package ftpconn

import (
	"context"
	"errors"
	"forterm/native/internal/launch"
	"forterm/native/internal/remotefs"
	ftp "github.com/jlaffaye/ftp"
	"io"
	"math"
	"net"
	"net/textproto"
	"os"
	"path"
	"strings"
	"sync"
	"sync/atomic"
	"time"
)

type Connection struct {
	chmodSupported bool
	client         *ftp.ServerConn
	root           string
	lane           chan struct{}
	live           context.Context
	cancel         context.CancelFunc
	closed         atomic.Bool
	mu             sync.Mutex
	sockets        map[*socket]bool
}
type socket struct {
	net.Conn
	owner *Connection
	once  sync.Once
}

func (s *socket) Close() error {
	var e error
	s.once.Do(func() { e = s.Conn.Close(); s.owner.mu.Lock(); delete(s.owner.sockets, s); s.owner.mu.Unlock() })
	return e
}
func Dial(ctx context.Context, c launch.Connection) (*Connection, error) {
	return DialWithStatus(ctx, c, nil)
}

func DialWithStatus(ctx context.Context, c launch.Connection, status func(string)) (*Connection, error) {
	emit := func(stage string) {
		if status != nil {
			status(stage)
		}
	}
	phase := "network"
	emit(phase)
	if c.Protocol != "ftp" || !c.HasPassword || c.KeyPath != "" || strings.ContainsAny(c.Username+c.Password, "\x00\r\n") {
		return nil, errors.New("FTP需要独立用户名与密码，不支持私钥或含控制字符的认证")
	}
	live, cancel := context.WithCancel(context.Background())
	connection := &Connection{live: live, cancel: cancel, lane: make(chan struct{}, 1), sockets: map[*socket]bool{}}
	stop := context.AfterFunc(ctx, connection.Close)
	defer stop()
	client, err := ftp.Dial(c.Address(), ftp.DialWithDialFunc(func(network, address string) (net.Conn, error) {
		raw, e := (&net.Dialer{Timeout: 10 * time.Second}).DialContext(live, network, address)
		if e != nil {
			return nil, e
		}
		wrapped := &socket{Conn: raw, owner: connection}
		connection.mu.Lock()
		if connection.closed.Load() {
			connection.mu.Unlock()
			raw.Close()
			return nil, io.ErrClosedPipe
		}
		connection.sockets[wrapped] = true
		connection.mu.Unlock()
		return wrapped, nil
	}), ftp.DialWithShutTimeout(10*time.Second))
	if err == nil {
		emit("network_ready")
		phase = "authentication"
		emit(phase)
		connection.client = client
		err = client.Login(c.Username, c.Password)
	}
	c.Password = ""
	if err == nil {
		emit("authenticated")
		phase = "directory"
		connection.root, err = client.CurrentDir()
		if err == nil {
			code, message, helpErr := client.Command("HELP SITE")
			connection.chmodSupported = helpErr == nil && code < 400 && helpSupportsChmod(message)
		}
	}
	if err != nil || ctx.Err() != nil {
		connection.Close()
		return nil, &ConnectError{Stage: phase}
	}
	emit("directory_ready")
	return connection, nil
}
func (c *Connection) Closed() bool { return c.closed.Load() }
func (c *Connection) Close() {
	if c.closed.Swap(true) {
		return
	}
	c.cancel()
	c.mu.Lock()
	sockets := make([]*socket, 0, len(c.sockets))
	for s := range c.sockets {
		sockets = append(sockets, s)
	}
	c.mu.Unlock()
	for _, s := range sockets {
		_ = s.Close()
	}
}
func (c *Connection) resolve(remote string) (string, error) {
	if strings.ContainsAny(remote, "\x00\r\n") {
		return "", errors.New("FTP路径不能包含控制字符")
	}
	if path.IsAbs(remote) {
		return path.Clean(remote), nil
	}
	return path.Join(c.root, remote), nil
}
func (c *Connection) run(ctx context.Context, fn func() error) error {
	select {
	case c.lane <- struct{}{}:
		defer func() { <-c.lane }()
	case <-ctx.Done():
		return errors.New("FTP文件请求在等待队列时取消或超时")
	}
	if c.Closed() {
		return errors.New("FTP连接已关闭，请明确重新建立连接；不会重用临时凭据")
	}
	closed := make(chan struct{})
	stop := context.AfterFunc(ctx, func() { c.Close(); close(closed) })
	defer func() {
		if !stop() {
			<-closed
		}
	}()
	err := fn()
	if ctx.Err() != nil {
		return errors.New("FTP请求取消或超时，连接已关闭；旧列表保留，请重新授权连接")
	}
	if err == nil {
		return nil
	}
	var local localFailure
	if errors.As(err, &local) {
		return local
	}
	var protocol *textproto.Error
	if errors.As(err, &protocol) {
		return errors.New("FTP目标不可用、权限不足或服务器不支持此操作")
	}
	if errors.Is(err, os.ErrNotExist) {
		return os.ErrNotExist
	}
	c.Close()
	return errors.New("FTP文件连接中断，未自动重新登录或重传")
}
func convert(entry *ftp.Entry) remotefs.Entry {
	mode := os.FileMode(0)
	if entry.Type == ftp.EntryTypeFolder {
		mode = os.ModeDir
	} else if entry.Type == ftp.EntryTypeLink {
		mode = os.ModeSymlink
	}
	size := int64(entry.Size)
	if entry.Size > math.MaxInt64 {
		size = -1
	}
	if entry.Mode != nil {
		raw := *entry.Mode
		mode |= os.FileMode(raw & 0777)
		if raw&04000 != 0 {
			mode |= os.ModeSetuid
		}
		if raw&02000 != 0 {
			mode |= os.ModeSetgid
		}
		if raw&01000 != 0 {
			mode |= os.ModeSticky
		}
	}
	return remotefs.Entry{ModeKnown: entry.Mode != nil, Name: entry.Name, Size: size, Directory: entry.Type == ftp.EntryTypeFolder, Mode: mode, Modified: entry.Time}
}
func (c *Connection) List(parent context.Context, remote string) ([]remotefs.Entry, error) {
	ctx, cancel := context.WithTimeout(parent, 15*time.Second)
	defer cancel()
	resolved, e := c.resolve(remote)
	if e != nil {
		return nil, e
	}
	var out []remotefs.Entry
	e = c.run(ctx, func() error {
		list, e := c.client.List(resolved)
		if e != nil {
			return e
		}
		for _, entry := range list {
			if entry.Name != "." && entry.Name != ".." {
				out = append(out, convert(entry))
			}
		}
		return nil
	})
	return out, e
}

type fileInfo struct{ entry remotefs.Entry }

func (f fileInfo) Name() string       { return f.entry.Name }
func (f fileInfo) Size() int64        { return f.entry.Size }
func (f fileInfo) Mode() os.FileMode  { return f.entry.Mode }
func (f fileInfo) ModTime() time.Time { return f.entry.Modified }
func (f fileInfo) IsDir() bool        { return f.entry.Directory }
func (f fileInfo) ModeKnown() bool    { return f.entry.ModeKnown }
func (f fileInfo) Sys() any           { return nil }
func (c *Connection) stat(remote string) (os.FileInfo, error) {
	entry, e := c.client.GetEntry(remote)
	if e == nil {
		return fileInfo{entry: convert(entry)}, nil
	}
	list, e := c.client.List(path.Dir(remote))
	if e != nil {
		return nil, e
	}
	for _, entry := range list {
		if entry.Name == path.Base(remote) {
			return fileInfo{entry: convert(entry)}, nil
		}
	}
	return nil, os.ErrNotExist
}
func (c *Connection) Stat(parent context.Context, remote string) (os.FileInfo, error) {
	ctx, cancel := context.WithTimeout(parent, 10*time.Second)
	defer cancel()
	resolved, e := c.resolve(remote)
	if e != nil {
		return nil, e
	}
	var info os.FileInfo
	e = c.run(ctx, func() error { var e error; info, e = c.stat(resolved); return e })
	return info, e
}

type ConnectError struct{ Stage string }

func (e *ConnectError) Error() string {
	switch e.Stage {
	case "network":
		return "FTP网络连接失败或超时"
	case "authentication":
		return "FTP认证未完成，请检查独立文件连接信息"
	default:
		return "FTP认证已完成，但当前目录读取失败"
	}
}
