// Package testserver is used only by tests and the explicitly named native-smoke binary.
package testserver

import (
	"bytes"
	"crypto/ed25519"
	"crypto/rand"
	"fmt"
	"github.com/pkg/sftp"
	"golang.org/x/crypto/ssh"
	"io"
	"net"
	"sync"
	"sync/atomic"
	"time"
)

type Server struct {
	DenyNewShell                     atomic.Bool
	RealPathStatus                   atomic.Uint32
	RealPathDelay                    atomic.Int64
	RealPath                         atomic.Int64
	AllowForward                     bool
	AuthorizedKey                    []byte
	Forward                          atomic.Int64
	Output                           []byte
	WriteDelay, ReadDelay, StatDelay atomic.Int64
	ShellDelay                       atomic.Int64
	handlers                         sftp.Handlers
	OpenModes                        []uint32
	AfterTempChmod                   func()
	Port                             int
	Fingerprint                      string
	Auth                             atomic.Int64
	SFTP                             atomic.Int64
	Keepalive                        atomic.Int64
	Exec                             atomic.Int64
	DenyExec, StallExec              bool
	mu                               sync.Mutex
	Input                            []byte
	ExecCommands                     []string
	Sizes                            [][2]uint32
	listener                         net.Listener
	clients                          []net.Conn
	DenyShell, DenySFTP              bool
}

func New() (*Server, error) { return NewWithConfig(nil) }
func NewWithConfig(configure func(*ssh.ServerConfig)) (*Server, error) {
	_, key, e := ed25519.GenerateKey(rand.Reader)
	if e != nil {
		return nil, e
	}
	signer, e := ssh.NewSignerFromKey(key)
	if e != nil {
		return nil, e
	}
	s := &Server{Fingerprint: ssh.FingerprintSHA256(signer.PublicKey())}
	conf := &ssh.ServerConfig{PasswordCallback: func(meta ssh.ConnMetadata, password []byte) (*ssh.Permissions, error) {
		s.Auth.Add(1)
		if meta.User() != "native-user" || string(password) != "native-test-password" {
			return nil, io.EOF
		}
		return nil, nil
	}}
	conf.PublicKeyCallback = func(meta ssh.ConnMetadata, key ssh.PublicKey) (*ssh.Permissions, error) {
		s.mu.Lock()
		accepted := meta.User() == "native-user" && bytes.Equal(key.Marshal(), s.AuthorizedKey)
		s.mu.Unlock()
		if !accepted {
			return nil, io.EOF
		}
		s.Auth.Add(1)
		return nil, nil
	}
	if configure != nil {
		configure(conf)
	}
	conf.AddHostKey(signer)
	s.listener, e = net.Listen("tcp", "127.0.0.1:0")
	if e != nil {
		return nil, e
	}
	s.Port = s.listener.Addr().(*net.TCPAddr).Port
	handlers := sftp.InMemHandler()
	s.handlers = handlers
	handlers.FileList = directoryLister{next: handlers.FileList, server: s}
	handlers.FilePut = delayFileWriter{server: s, next: handlers.FilePut}
	handlers.FileGet = delayFileReader{server: s, next: handlers.FileGet}
	handlers.FileCmd = fileCommands{next: handlers.FileCmd, server: s}
	go func() {
		for {
			conn, e := s.listener.Accept()
			if e != nil {
				return
			}
			s.mu.Lock()
			s.clients = append(s.clients, conn)
			s.mu.Unlock()
			go func() {
				_, channels, requests, e := ssh.NewServerConn(conn, conf)
				if e != nil {
					conn.Close()
					return
				}
				go func() {
					for r := range requests {
						if r.Type == "keepalive@openssh.com" {
							s.Keepalive.Add(1)
						}
						r.Reply(true, nil)
					}
				}()
				for incoming := range channels {
					if incoming.ChannelType() == "direct-tcpip" && s.AllowForward {
						go s.forward(incoming)
						continue
					}
					if incoming.ChannelType() != "session" {
						incoming.Reject(ssh.UnknownChannelType, "unsupported")
						continue
					}
					ch, reqs, e := incoming.Accept()
					if e != nil {
						continue
					}
					go s.session(ch, reqs, handlers)
				}
			}()
		}
	}()
	return s, nil
}
func (s *Server) session(ch ssh.Channel, requests <-chan *ssh.Request, handlers sftp.Handlers) {
	var size = [2]uint32{24, 80}
	var process io.ReadWriteCloser
	for r := range requests {
		switch r.Type {
		case "pty-req":
			var p struct {
				Term                      string
				Cols, Rows, Width, Height uint32
				Modes                     string
			}
			if ssh.Unmarshal(r.Payload, &p) == nil {
				size = [2]uint32{p.Rows, p.Cols}
				s.mu.Lock()
				s.Sizes = append(s.Sizes, size)
				s.mu.Unlock()
			}
			r.Reply(true, nil)
		case "window-change":
			var p struct{ Cols, Rows, Width, Height uint32 }
			if ssh.Unmarshal(r.Payload, &p) == nil {
				size = [2]uint32{p.Rows, p.Cols}
				s.mu.Lock()
				s.Sizes = append(s.Sizes, size)
				s.mu.Unlock()
				if process != nil {
					resize(process, size[0], size[1])
				}
			}
			if r.WantReply {
				r.Reply(true, nil)
			}
		case "shell":
			if delay := time.Duration(s.ShellDelay.Load()); delay > 0 {
				time.Sleep(delay)
			}
			if s.DenyShell || s.DenyNewShell.Load() {
				r.Reply(false, nil)
				continue
			}
			p, e := shell(size[0], size[1])
			if e != nil {
				r.Reply(false, nil)
				continue
			}
			process = p
			r.Reply(true, nil)
			go func() {
				io.Copy(shellOutput{Writer: ch, server: s}, p)
				ch.SendRequest("exit-status", false, ssh.Marshal(struct{ Status uint32 }{0}))
				ch.Close()
				p.Close()
			}()
			go func() {
				buf := make([]byte, 4096)
				for {
					n, e := ch.Read(buf)
					if n > 0 {
						s.mu.Lock()
						if len(s.Input) < 65536 {
							s.Input = append(s.Input, buf[:n]...)
						}
						s.mu.Unlock()
						p.Write(buf[:n])
					}
					if e != nil {
						p.Close()
						return
					}
				}
			}()
		case "exec":
			s.Exec.Add(1)
			var command struct{ Command string }
			_ = ssh.Unmarshal(r.Payload, &command)
			s.mu.Lock()
			s.ExecCommands = append(s.ExecCommands, command.Command)
			s.mu.Unlock()
			if s.DenyExec {
				r.Reply(false, nil)
				continue
			}
			r.Reply(true, nil)
			if s.StallExec {
				continue
			}
			k := s.Exec.Load()
			io.WriteString(ch, fmt.Sprintf("0.12 0.20 0.30 1/10 42\nCPU 累计计数（USER_HZ）\ncpu %d 10 %d %d 2 0 0 0\n内存（KiB）\nMemTotal: 8192000 kB\nMemFree: 1000000 kB\nMemAvailable: 4000000 kB\n磁盘\nFilesystem Size Used Avail Use%% Mounted on\n/dev/test 10G 1G 9G 10%% /\n网络累计字节 / 数据包\nlo: %d 1 0 0 0 0 0 0 %d 1 0 0 0 0 0 0\n", 100+5*k, 40+5*k, 900+90*k, 100+4096*k, 100+2048*k))
			ch.SendRequest("exit-status", false, ssh.Marshal(struct{ Status uint32 }{0}))
			ch.Close()
			return
		case "subsystem":
			var p struct{ Name string }
			if ssh.Unmarshal(r.Payload, &p) != nil || p.Name != "sftp" || s.DenySFTP {
				r.Reply(false, nil)
				continue
			}
			s.SFTP.Add(1)
			r.Reply(true, nil)
			server := sftp.NewRequestServer(&channelObserver{Channel: ch, server: s}, handlers)
			_ = server.Serve()
			server.Close()
			return
		default:
			r.Reply(false, nil)
		}
	}
	ch.Close()
	if process != nil {
		process.Close()
	}
}
func (s *Server) Snapshot() ([]byte, [][2]uint32) {
	s.mu.Lock()
	defer s.mu.Unlock()
	return append([]byte(nil), s.Input...), append([][2]uint32(nil), s.Sizes...)
}
func (s *Server) Close() {
	s.listener.Close()
	s.mu.Lock()
	defer s.mu.Unlock()
	for _, c := range s.clients {
		c.Close()
	}
}

type shellOutput struct {
	io.Writer
	server *Server
}

func (w shellOutput) Write(p []byte) (int, error) {
	w.server.mu.Lock()
	if len(w.server.Output) < 1<<20 {
		w.server.Output = append(w.server.Output, p...)
	}
	w.server.mu.Unlock()
	return w.Writer.Write(p)
}
func (s *Server) SnapshotOutput() []byte {
	s.mu.Lock()
	defer s.mu.Unlock()
	return append([]byte(nil), s.Output...)
}
