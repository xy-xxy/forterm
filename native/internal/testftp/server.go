// Package testftp is a loopback-only synthetic FTP fixture, never imported by
// the production core. It only exposes its caller-provided temporary directory.
package testftp

import (
	"bufio"
	"fmt"
	"io"
	"net"
	"os"
	"path"
	"path/filepath"
	"strconv"
	"strings"
	"sync"
	"sync/atomic"
	"time"
)

type Server struct {
	BeforeRmdir    func(string)
	Port           int
	Root           string
	Auth, Commands atomic.Int64
	MetadataDelay  atomic.Int64
	listener       net.Listener
	mu             sync.Mutex
	clients        []net.Conn
	closed         chan struct{}
}

func New(root string) (*Server, error) {
	l, e := net.Listen("tcp", "127.0.0.1:0")
	if e != nil {
		return nil, e
	}
	s := &Server{Port: l.Addr().(*net.TCPAddr).Port, Root: root, listener: l, closed: make(chan struct{})}
	go func() {
		for {
			c, e := l.Accept()
			if e != nil {
				return
			}
			s.mu.Lock()
			s.clients = append(s.clients, c)
			s.mu.Unlock()
			go s.serve(c)
		}
	}()
	return s, nil
}
func (s *Server) Close() {
	select {
	case <-s.closed:
		return
	default:
		close(s.closed)
	}
	s.listener.Close()
	s.mu.Lock()
	defer s.mu.Unlock()
	for _, c := range s.clients {
		c.Close()
	}
}
func (s *Server) resolve(remote string) string {
	return filepath.Join(s.Root, filepath.FromSlash(strings.TrimPrefix(path.Clean("/"+remote), "/")))
}
func facts(info os.FileInfo, name string) string {
	kind := "file"
	if info.IsDir() {
		kind = "dir"
	} else if info.Mode()&os.ModeSymlink != 0 {
		kind = "OS.unix=slink"
	}
	mode := uint32(info.Mode().Perm())
	if info.Mode()&os.ModeSetuid != 0 {
		mode |= 04000
	}
	if info.Mode()&os.ModeSetgid != 0 {
		mode |= 02000
	}
	if info.Mode()&os.ModeSticky != 0 {
		mode |= 01000
	}
	return fmt.Sprintf("type=%s;size=%d;modify=%s;unix.mode=%04o; %s", kind, info.Size(), info.ModTime().UTC().Format("20060102150405"), mode, name)
}
func (s *Server) serve(control net.Conn) {
	defer control.Close()
	writer := bufio.NewWriter(control)
	reply := func(code int, text string) { fmt.Fprintf(writer, "%d %s\r\n", code, text); writer.Flush() }
	reply(220, "Synthetic native FTP test")
	scanner := bufio.NewScanner(control)
	user, logged := "", false
	var dataListener net.Listener
	var renameFrom string
	defer func() {
		if dataListener != nil {
			dataListener.Close()
		}
	}()
	for scanner.Scan() {
		s.Commands.Add(1)
		parts := strings.SplitN(scanner.Text(), " ", 2)
		command := strings.ToUpper(parts[0])
		arg := ""
		if len(parts) == 2 {
			arg = parts[1]
		}
		switch command {
		case "USER":
			user = arg
			reply(331, "Password required")
			continue
		case "PASS":
			s.Auth.Add(1)
			logged = user == "native-ftp" && arg == "synthetic-ftp-password"
			if logged {
				reply(230, "Logged in")
			} else {
				reply(530, "Authentication failed")
			}
			continue
		case "QUIT":
			reply(221, "Bye")
			return
		}
		if !logged {
			reply(530, "Login first")
			continue
		}
		switch command {
		case "FEAT":
			fmt.Fprint(writer, "211-Features\r\n UTF8\r\n MLST type*;size*;modify*;unix.mode*;\r\n EPSV\r\n MDTM\r\n SIZE\r\n211 End\r\n")
			writer.Flush()
		case "OPTS", "TYPE", "NOOP":
			reply(200, "OK")
		case "PWD":
			reply(257, "\"/\" current directory")
		case "HELP":
			reply(214, "SITE CHMOD")
		case "CWD":
			info, e := os.Lstat(s.resolve(arg))
			if e != nil || !info.IsDir() {
				reply(550, "Not a directory")
			} else {
				reply(250, "Directory changed")
			}
		case "EPSV", "PASV":
			if dataListener != nil {
				dataListener.Close()
			}
			l, e := net.Listen("tcp", "127.0.0.1:0")
			if e != nil {
				reply(425, "Data unavailable")
				continue
			}
			dataListener = l
			port := l.Addr().(*net.TCPAddr).Port
			if command == "EPSV" {
				reply(229, fmt.Sprintf("Entering Extended Passive Mode (|||%d|)", port))
			} else {
				reply(227, fmt.Sprintf("Entering Passive Mode (127,0,0,1,%d,%d)", port/256, port%256))
			}
		case "MLST", "SIZE", "MDTM":
			if delay := time.Duration(s.MetadataDelay.Load()); delay > 0 {
				select {
				case <-time.After(delay):
				case <-s.closed:
					return
				}
			}
			info, e := os.Lstat(s.resolve(arg))
			if e != nil {
				reply(550, "Not found")
				continue
			}
			if command == "SIZE" {
				reply(213, strconv.FormatInt(info.Size(), 10))
			} else if command == "MDTM" {
				reply(213, info.ModTime().UTC().Format("20060102150405"))
			} else {
				fmt.Fprintf(writer, "250-Details\r\n %s\r\n250 End\r\n", facts(info, arg))
				writer.Flush()
			}
		case "MLSD", "LIST", "STOR", "RETR":
			if dataListener == nil {
				reply(425, "Select passive mode")
				continue
			}
			reply(150, "Data connection")
			if l, ok := dataListener.(*net.TCPListener); ok {
				_ = l.SetDeadline(time.Now().Add(3 * time.Second))
			}
			data, e := dataListener.Accept()
			dataListener.Close()
			dataListener = nil
			if e != nil {
				reply(425, "Data failed")
				continue
			}
			failed := false
			switch command {
			case "MLSD", "LIST":
				entries, e := os.ReadDir(s.resolve(arg))
				if e != nil {
					failed = true
				} else {
					for _, entry := range entries {
						info, e := entry.Info()
						if e != nil {
							continue
						}
						if command == "MLSD" {
							fmt.Fprintln(data, facts(info, entry.Name())+"\r")
						} else {
							fmt.Fprintf(data, "%s 1 user group %d Jan 01 2026 %s\r\n", info.Mode().String(), info.Size(), entry.Name())
						}
					}
				}
			case "STOR":
				file, e := os.OpenFile(s.resolve(arg), os.O_WRONLY|os.O_CREATE|os.O_TRUNC, 0600)
				if e != nil {
					failed = true
				} else {
					_, e = io.Copy(file, data)
					file.Close()
					failed = e != nil
				}
			case "RETR":
				file, e := os.Open(s.resolve(arg))
				if e != nil {
					failed = true
				} else {
					_, e = io.Copy(data, file)
					file.Close()
					failed = e != nil
				}
			}
			data.Close()
			if failed {
				reply(550, "File unavailable")
			} else {
				reply(226, "Transfer complete")
			}
		case "MKD":
			if e := os.Mkdir(s.resolve(arg), 0755); e != nil {
				reply(550, "Cannot create")
			} else {
				reply(257, "Created")
			}
		case "DELE", "RMD":
			if command == "RMD" && s.BeforeRmdir != nil {
				s.BeforeRmdir(s.resolve(arg))
			}
			if e := os.Remove(s.resolve(arg)); e != nil {
				reply(550, "Cannot remove")
			} else {
				reply(250, "Removed")
			}
		case "RNFR":
			renameFrom = arg
			reply(350, "Ready")
		case "RNTO":
			if e := os.Rename(s.resolve(renameFrom), s.resolve(arg)); e != nil {
				reply(550, "Rename failed")
			} else {
				reply(250, "Renamed")
			}
		case "SITE":
			fields := strings.SplitN(arg, " ", 3)
			if len(fields) != 3 || !strings.EqualFold(fields[0], "CHMOD") {
				reply(502, "Unsupported")
				continue
			}
			raw, e := strconv.ParseUint(fields[1], 8, 32)
			if e != nil {
				reply(550, "Invalid mode")
				continue
			}
			mode := os.FileMode(raw & 0777)
			if raw&04000 != 0 {
				mode |= os.ModeSetuid
			}
			if raw&02000 != 0 {
				mode |= os.ModeSetgid
			}
			if raw&01000 != 0 {
				mode |= os.ModeSticky
			}
			if e = os.Chmod(s.resolve(fields[2]), mode); e != nil {
				reply(550, "Chmod failed")
			} else {
				reply(200, "Mode changed")
			}
		default:
			reply(502, "Not implemented")
		}
	}
}
