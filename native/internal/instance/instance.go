package instance

import (
	"encoding/json"
	"errors"
	"github.com/gofrs/flock"
	"io"
	"net"
	"os"
	"path/filepath"
	"time"
)

type Server struct {
	listener net.Listener
	lock     *flock.Flock
	done     chan struct{}
}
type request struct {
	Args []string `json:"args"`
}

func Start(directory string, args []string, accept func([]string)) (*Server, bool, error) {
	if e := os.MkdirAll(directory, 0700); e != nil {
		return nil, false, errors.New("无法创建原生运行目录")
	}
	_ = os.Chmod(directory, 0700)
	lock := flock.New(filepath.Join(directory, "instance.lock"), flock.SetPermissions(0600))
	primary, e := lock.TryLock()
	if e != nil {
		return nil, false, errors.New("无法锁定原生运行目录")
	}
	address, e := endpoint(directory)
	if e != nil {
		lock.Close()
		return nil, false, e
	}
	if !primary {
		defer lock.Close()
		deadline := time.Now().Add(5 * time.Second)
		for time.Now().Before(deadline) {
			conn, e := dial(address)
			if e == nil {
				_ = conn.SetDeadline(time.Now().Add(5 * time.Second))
				e = json.NewEncoder(conn).Encode(request{args})
				var ack bool
				if e == nil {
					e = json.NewDecoder(conn).Decode(&ack)
				}
				conn.Close()
				if e == nil && ack {
					return nil, false, nil
				}
			}
			time.Sleep(50 * time.Millisecond)
		}
		return nil, false, errors.New("现有原生实例没有确认参数转交；没有启动重复实例")
	}
	listener, e := listen(address)
	if e != nil {
		lock.Close()
		return nil, false, errors.New("无法建立原生实例通信")
	}
	s := &Server{listener: listener, lock: lock, done: make(chan struct{})}
	go func() {
		defer close(s.done)
		for {
			conn, e := listener.Accept()
			if e != nil {
				return
			}
			go func(c net.Conn) {
				defer c.Close()
				_ = c.SetDeadline(time.Now().Add(5 * time.Second))
				var r request
				decoder := json.NewDecoder(io.LimitReader(c, 1024*1024))
				decoder.DisallowUnknownFields()
				if decoder.Decode(&r) != nil || len(r.Args) > 128 {
					return
				}
				for _, a := range r.Args {
					if len(a) > 32768 {
						return
					}
				}
				accept(r.Args)
				_ = json.NewEncoder(c).Encode(true)
			}(conn)
		}
	}()
	return s, true, nil
}
func (s *Server) Close() {
	if s != nil {
		_ = s.listener.Close()
		<-s.done
		_ = s.lock.Close()
	}
}
