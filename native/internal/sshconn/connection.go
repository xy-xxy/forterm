package sshconn

import (
	"context"
	"errors"
	"fmt"
	"forterm/native/internal/launch"
	"forterm/native/internal/remotefs"
	"forterm/native/internal/transfers"
	"github.com/pkg/sftp"
	"golang.org/x/crypto/ssh"
	"golang.org/x/crypto/ssh/agent"
	"io"
	"net"
	"os"
	"path"
	"sync"
	"sync/atomic"
	"time"
)

type Callbacks struct {
	Progress  func(ConnectEvent)
	KeepAlive *KeepAlive
	HostKey   func(context.Context, string, string) (bool, error)
	Challenge func(context.Context, string, []string, []bool) ([]string, error)
}
type Connection struct {
	shellPending     atomic.Bool
	privilegePending atomic.Bool
	leaseMu          sync.Mutex
	references       int
	heartbeat        KeepAlive
	parents          []*ssh.Client
	monitoring       atomic.Bool
	closed           atomic.Bool
	client           *ssh.Client
	cancel           context.CancelFunc
	done             chan struct{}
	fileLane         chan struct{}
	fileMu           sync.Mutex
	file             *sftp.Client
	opening          *fileOpen
	closeOnce        sync.Once
}

func Dial(ctx context.Context, c launch.Connection, cb Callbacks) (*Connection, error) {
	if len(c.Jumps) > 8 {
		return nil, errors.New("跳板链超过8层")
	}
	chain := append([]launch.Connection(nil), c.Jumps...)
	c.Jumps = nil
	chain = append(chain, c)
	clients := []*ssh.Client{}
	var via *ssh.Client
	for index, hop := range chain {
		hopCB := cb
		if cb.Progress != nil {
			hopCB.Progress = func(event ConnectEvent) { event.Hop = index + 1; event.Total = len(chain); cb.Progress(event) }
		}
		client, err := connectClient(ctx, hop, hopCB, via)
		if err != nil {
			for i := len(clients) - 1; i >= 0; i-- {
				clients[i].Close()
			}
			return nil, err
		}
		clients = append(clients, client)
		via = client
	}
	live, cancel := context.WithCancel(context.Background())
	options := DefaultKeepAlive()
	if cb.KeepAlive != nil {
		options = normalizeKeepAlive(*cb.KeepAlive)
	}
	result := &Connection{references: 1, heartbeat: options, client: via, parents: clients[:len(clients)-1], cancel: cancel, done: make(chan struct{}), fileLane: make(chan struct{}, 1)}
	go result.keepAlive(live)
	return result, nil
}
func connectClient(ctx context.Context, c launch.Connection, cb Callbacks, via *ssh.Client) (*ssh.Client, error) {
	if c.Protocol == "ftp" {
		return nil, errors.New("原生原型暂未迁移FTP")
	}
	if cb.HostKey == nil {
		return nil, errors.New("必须确认SSH主机指纹")
	}
	var auth []ssh.AuthMethod
	var passwordAttempted atomic.Bool
	var hostConfirmed atomic.Bool
	var hostRejected atomic.Bool
	if (c.AuthMethod == "" || c.AuthMethod == "password") && (c.HasPassword || c.Password != "") {
		auth = append(auth, ssh.PasswordCallback(func() (string, error) {
			if passwordAttempted.Swap(true) {
				if cb.Challenge == nil {
					return "", errors.New("服务器要求追加认证，未自动重试原密码")
				}
				answers, e := cb.Challenge(ctx, "服务器追加密码认证（首次密码已提交）", []string{"服务器要求的新认证值（仅本轮）"}, []bool{false})
				if e != nil || len(answers) != 1 {
					return "", errors.New("追加认证取消")
				}
				value := answers[0]
				clear(answers)
				return value, nil
			}
			return c.Password, nil
		}))
	}
	if c.AuthMethod == "agent" {
		socket, err := openAgent(ctx)
		if err != nil {
			return nil, errors.New("SSH认证代理不可用")
		}
		defer socket.Close()
		stopAgent := context.AfterFunc(ctx, func() { socket.Close() })
		defer stopAgent()
		auth = append(auth, ssh.PublicKeysCallback(agent.NewClient(socket).Signers))
	}
	if c.KeyPath != "" {
		f, e := os.Open(c.KeyPath)
		if e != nil {
			return nil, errors.New("无法读取私钥")
		}
		b, e := io.ReadAll(io.LimitReader(f, (1<<20)+1))
		f.Close()
		if len(b) > 1<<20 {
			return nil, errors.New("私钥超过1MiB限制")
		}
		if e != nil {
			return nil, errors.New("无法读取私钥")
		}
		signer, e := ssh.ParsePrivateKey(b)
		if _, encrypted := e.(*ssh.PassphraseMissingError); encrypted {
			phrase := c.Passphrase
			if phrase == "" && cb.Challenge != nil {
				answers, err := cb.Challenge(ctx, "解锁私钥", []string{"私钥密码"}, []bool{false})
				if err != nil {
					for i := range b {
						b[i] = 0
					}
					return nil, errors.New("私钥解锁已取消")
				}
				if len(answers) > 0 {
					phrase = answers[0]
				}
			}
			pass := []byte(phrase)
			signer, e = ssh.ParsePrivateKeyWithPassphrase(b, pass)
			for i := range pass {
				pass[i] = 0
			}
			phrase = ""
			c.Passphrase = ""
		}
		for i := range b {
			b[i] = 0
		}
		if e != nil {
			return nil, errors.New("私钥无法解析或解锁失败")
		}

		auth = append(auth, ssh.PublicKeys(signer))
	}
	if cb.Challenge != nil {
		round := 0
		auth = append(auth, ssh.KeyboardInteractive(func(user, instruction string, questions []string, echo []bool) ([]string, error) {
			round++
			if round == 1 && !c.Temporary && !passwordAttempted.Load() && (c.AuthMethod == "" || c.AuthMethod == "password" || c.AuthMethod == "keyboard") && (c.HasPassword || c.Password != "") && singlePasswordQuestion(questions, echo) {
				return []string{c.Password}, nil
			}
			title := fmt.Sprintf("服务器交互认证 · 第%d轮", round)
			if passwordAttempted.Load() {
				title += "（密码已提交，服务器要求补充验证）"
			} else if round > 1 {
				title += "（服务器要求后续验证）"
			}
			if instruction != "" {
				title += "\n" + limitChallengeText(instruction, 256)
			}
			return cb.Challenge(ctx, title, questions, echo)
		}))
	}
	conf := &ssh.ClientConfig{User: c.Username, Auth: auth, HostKeyCallback: func(host string, addr net.Addr, key ssh.PublicKey) error {
		notifyConnect(cb, StageFingerprint)
		ok, e := cb.HostKey(ctx, c.Address(), ssh.FingerprintSHA256(key))
		if e != nil || !ok {
			hostRejected.Store(true)
			return &ConnectError{Stage: StageFingerprint}
		}
		hostConfirmed.Store(true)
		notifyConnect(cb, StageTrusted)
		notifyConnect(cb, StageAuthentication)
		return nil
	}, Timeout: 15 * time.Second}
	var raw net.Conn
	var e error
	notifyConnect(cb, StageNetwork)
	if via != nil {
		raw, e = via.DialContext(ctx, "tcp", c.Address())
	} else {
		raw, e = (&net.Dialer{}).DialContext(ctx, "tcp", c.Address())
	}
	if e != nil {
		return nil, &ConnectError{Stage: StageNetwork}
	}
	notifyConnect(cb, StageNetworkReady)
	if deadline, ok := ctx.Deadline(); ok {
		_ = raw.SetDeadline(deadline)
	}
	stop := context.AfterFunc(ctx, func() { _ = raw.Close() })
	cc, ch, req, e := ssh.NewClientConn(raw, c.Address(), conf)
	stop()
	c.Password = ""
	if e != nil {
		raw.Close()
		if hostRejected.Load() {
			return nil, &ConnectError{Stage: StageFingerprint}
		}
		if hostConfirmed.Load() {
			return nil, &ConnectError{Stage: StageAuthentication, temporary: c.Temporary}
		}
		return nil, &ConnectError{Stage: StageHandshake}
	}
	notifyConnect(cb, StageAuthenticated)
	_ = raw.SetDeadline(time.Time{})
	return ssh.NewClient(cc, ch, req), nil
}

func (c *Connection) keepAlive(ctx context.Context) {
	defer close(c.done)
	if !c.heartbeat.Enabled {
		<-ctx.Done()
		return
	}
	ticker := time.NewTicker(time.Duration(c.heartbeat.IntervalSeconds) * time.Second)
	defer ticker.Stop()
	for {
		select {
		case <-ctx.Done():
			return
		case <-ticker.C:
			answer := make(chan error, 1)
			go func() {
				for _, client := range append(append([]*ssh.Client(nil), c.parents...), c.client) {
					if _, _, err := client.SendRequest("keepalive@openssh.com", true, nil); err != nil {
						answer <- err
						return
					}
				}
				answer <- nil
			}()
			select {
			case <-ctx.Done():
				return
			case err := <-answer:
				if err != nil {
					c.Close()
					return
				}
			case <-time.After(45 * time.Second):
				c.Close()
				return
			}
			if c.heartbeat.SFTPProbe {
				c.probeFiles(ctx)
			}
		}
	}
}
func (c *Connection) Close() {
	c.closeOnce.Do(func() {
		c.closed.Store(true)
		c.cancel()
		_ = c.client.Close()
		for i := len(c.parents) - 1; i >= 0; i-- {
			_ = c.parents[i].Close()
		}
	})
}

type Shell struct {
	Session *ssh.Session
	In      io.WriteCloser
	Out     io.ReadCloser
}

func (c *Connection) Shell(rows, cols int) (*Shell, error) {
	return c.ShellContext(context.Background(), rows, cols)
}
func (c *Connection) shellHandshake(ctx context.Context, rows, cols int) (*Shell, error) {
	s, e := c.client.NewSession()
	if e != nil {
		return nil, errors.New("终端通道未授权；文件连接仍可尝试")
	}
	stop := context.AfterFunc(ctx, func() { s.Close() })
	defer stop()
	if ctx.Err() != nil {
		s.Close()
		return nil, ctx.Err()
	}
	in, e := s.StdinPipe()
	if e != nil {
		s.Close()
		return nil, errors.New("无法创建终端输入")
	}
	out, w := io.Pipe()
	s.Stdout = w
	s.Stderr = w
	if e = s.RequestPty("xterm-256color", rows, cols, ssh.TerminalModes{ssh.ECHO: 1}); e == nil {
		e = s.Shell()
	}
	if e != nil {
		s.Close()
		w.Close()
		out.Close()
		return nil, errors.New("服务器未开放shell；不会关闭其他通道")
	}
	go func() { _ = s.Wait(); w.Close() }()
	return &Shell{s, &shellInput{writer: in}, out}, nil
}
func (s *Shell) Resize(rows, cols int) error {
	if rows < 1 || cols < 2 {
		return nil
	}
	return s.Session.WindowChange(rows, cols)
}
func (s *Shell) Close() { s.Session.Close(); s.In.Close(); s.Out.Close() }

type fileOpen struct {
	done      chan struct{}
	client    *sftp.Client
	err       error
	abandoned bool
}

func (c *Connection) getFile(ctx context.Context) (*sftp.Client, error) {
	if ctx.Err() != nil || c.Closed() {
		return nil, errors.New("文件操作已取消或连接已关闭")
	}
	c.fileMu.Lock()
	if c.file != nil {
		f := c.file
		c.fileMu.Unlock()
		return f, nil
	}
	if c.opening != nil {
		c.fileMu.Unlock()
		return nil, errors.New("SFTP通道仍在等待服务器，未重复开启")
	}
	o := &fileOpen{done: make(chan struct{})}
	c.opening = o
	c.fileMu.Unlock()
	go func() {
		f, e := sftp.NewClient(c.client)
		c.fileMu.Lock()
		if o.abandoned && f != nil {
			_ = f.Close()
			f = nil
		}
		o.client, o.err = f, e
		if !o.abandoned && e == nil {
			c.file = f
		}
		c.opening = nil
		close(o.done)
		c.fileMu.Unlock()
	}()
	select {
	case <-ctx.Done():
		c.fileMu.Lock()
		o.abandoned = true
		if o.client != nil {
			_ = o.client.Close()
			if c.file == o.client {
				c.file = nil
			}
		}
		c.fileMu.Unlock()
		return nil, errors.New("SFTP开启超时或取消；未关闭SSH")
	case <-o.done:
		if o.err != nil {
			return nil, errors.New("SFTP通道未授权，终端仍可使用")
		}
		if ctx.Err() != nil {
			return nil, errors.New("文件操作已取消")
		}
		return o.client, nil
	}
}
func (c *Connection) fileCall(ctx context.Context, fn func(*sftp.Client) error) error {
	select {
	case c.fileLane <- struct{}{}:
		defer func() { <-c.fileLane }()
	case <-ctx.Done():
		return fmt.Errorf("等待文件队列取消或超时，未打开文件流：%w", ctx.Err())
	}
	f, e := c.getFile(ctx)
	if e != nil {
		return e
	}
	closed := make(chan struct{})
	stop := context.AfterFunc(ctx, func() {
		c.fileMu.Lock()
		if c.file == f {
			c.file = nil
		}
		c.fileMu.Unlock()
		_ = f.Close()
		close(closed)
	})
	defer func() {
		if !stop() {
			<-closed
		}
	}()
	e = fn(f)
	if ctx.Err() != nil {
		return fmt.Errorf("文件操作取消或超时，文件通道已关闭；SSH仍保留：%w", ctx.Err())
	}
	return e
}

type Entry = remotefs.Entry

func (c *Connection) List(ctx context.Context, p string) ([]Entry, error) {
	var entries []Entry
	e := c.fileCall(ctx, func(f *sftp.Client) error {
		list, e := f.ReadDir(p)
		if e != nil {
			return fmt.Errorf("目录读取失败（READDIR）：%w", e)
		}
		for _, v := range list {
			entries = append(entries, Entry{ModeKnown: true, Name: v.Name(), Size: v.Size(), Directory: v.IsDir(), Mode: v.Mode(), Modified: v.ModTime()})
		}
		return nil
	})
	return entries, e
}
func (c *Connection) Upload(ctx context.Context, local, remote string, overwrite bool) error {
	return c.RunTransfer(ctx, transfers.Spec{Local: local, Remote: remote, Overwrite: overwrite, Direction: "upload"}, nil)
}
func (c *Connection) Download(ctx context.Context, remote, local string, overwrite bool) error {
	return c.RunTransfer(ctx, transfers.Spec{Local: local, Remote: remote, Overwrite: overwrite, Direction: "download"}, nil)
}
func RemoteJoin(dir, name string) string { return path.Join(dir, name) }

func (c *Connection) Closed() bool { return c.closed.Load() }

// Retain/Release are UI owners of an authenticated transport, including jumps.
// Close remains a force-close for network failure and isolated check clients.
func (c *Connection) Retain() bool {
	c.leaseMu.Lock()
	defer c.leaseMu.Unlock()
	if c.Closed() || c.references <= 0 {
		return false
	}
	c.references++
	return true
}
func (c *Connection) Release() {
	c.leaseMu.Lock()
	if c.references <= 0 {
		c.leaseMu.Unlock()
		return
	}
	c.references--
	last := c.references == 0
	c.leaseMu.Unlock()
	if last {
		c.Close()
	}
}

func (c *Connection) ShellContext(parent context.Context, rows, cols int) (*Shell, error) {
	if c.Closed() {
		return nil, errors.New("SSH已断开，未重新登录")
	}
	if !c.shellPending.CompareAndSwap(false, true) {
		return nil, errors.New("同SSH有未完成终端通道请求，未叠加")
	}
	ctx, cancel := context.WithTimeout(parent, 15*time.Second)
	defer cancel()
	type answer struct {
		shell *Shell
		err   error
	}
	result := make(chan answer)
	go func() {
		defer c.shellPending.Store(false)
		shell, e := c.shellHandshake(ctx, rows, cols)
		select {
		case result <- answer{shell, e}:
		case <-ctx.Done():
			if shell != nil {
				shell.Close()
			}
		}
	}()
	select {
	case value := <-result:
		if ctx.Err() != nil {
			if value.shell != nil {
				value.shell.Close()
			}
			return nil, ctx.Err()
		}
		return value.shell, value.err
	case <-ctx.Done():
		return nil, errors.New("创建终端通道超时或取消；原终端保留，未重登")
	}
}

type shellInput struct {
	mu     sync.Mutex
	writer io.WriteCloser
	closed bool
}

func (s *shellInput) Write(data []byte) (int, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.closed {
		return 0, io.ErrClosedPipe
	}
	return s.writer.Write(data)
}
func (s *shellInput) Close() error {
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.closed {
		return nil
	}
	s.closed = true
	return s.writer.Close()
}
