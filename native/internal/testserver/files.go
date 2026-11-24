package testserver

import (
	"encoding/binary"
	"github.com/pkg/sftp"
	"golang.org/x/crypto/ssh"
	"io"
	"path"
	"strings"
	"time"
)

// channelObserver only records synthetic fixture OPEN attributes. It forwards
// every byte unchanged, so tests assert the actual packet's creation mode.
type channelObserver struct {
	ssh.Channel
	server  *Server
	pending []byte
}

func (c *channelObserver) Read(p []byte) (int, error) {
	n, e := c.Channel.Read(p)
	c.pending = append(c.pending, p[:n]...)
	for len(c.pending) >= 4 {
		length := int(binary.BigEndian.Uint32(c.pending))
		if length < 1 || length > 1024*1024 {
			c.pending = nil
			break
		}
		if len(c.pending) < length+4 {
			break
		}
		packet := c.pending[4 : length+4]
		if len(packet) > 0 && packet[0] == 16 {
			c.server.RealPath.Add(1)
		}
		if len(packet) > 0 && (packet[0] == 7 || packet[0] == 17) {
			if delay := time.Duration(c.server.StatDelay.Load()); delay > 0 {
				time.Sleep(delay)
			}
		}
		if len(packet) >= 9 && packet[0] == 3 {
			size := int(binary.BigEndian.Uint32(packet[5:9]))
			off := 9 + size
			if size >= 0 && off+12 <= len(packet) && binary.BigEndian.Uint32(packet[off+4:off+8])&4 != 0 {
				mode := binary.BigEndian.Uint32(packet[off+8 : off+12])
				c.server.mu.Lock()
				c.server.OpenModes = append(c.server.OpenModes, mode)
				c.server.mu.Unlock()
			}
		}
		c.pending = c.pending[length+4:]
	}
	return n, e
}

type fileCommands struct {
	next   sftp.FileCmder
	server *Server
}

func (c fileCommands) Filecmd(r *sftp.Request) error {
	err := c.next.Filecmd(r)
	if err == nil && r.Method == "Setstat" && strings.Contains(r.Filepath, ".forterm-edit-") && c.server.AfterTempChmod != nil {
		c.server.AfterTempChmod()
	}
	return err
}
func (c fileCommands) PosixRename(r *sftp.Request) error {
	if delegate, ok := c.next.(sftp.PosixRenameFileCmder); ok {
		return delegate.PosixRename(r)
	}
	return c.next.Filecmd(r)
}

// ReplaceFixtureFile simulates an independent remote actor, not a second
// authentication or an operation serialized through the client's file lane.
func (s *Server) ReplaceFixtureFile(path string, data []byte) error {
	r := sftp.NewRequest("Put", path)
	r.Flags = 2 | 8 | 16
	writer, e := s.handlers.FilePut.Filewrite(r)
	if e != nil {
		return e
	}
	_, e = writer.WriteAt(data, 0)
	if c, ok := writer.(io.Closer); ok {
		_ = c.Close()
	}
	return e
}
func (s *Server) CreationModes() []uint32 {
	s.mu.Lock()
	defer s.mu.Unlock()
	return append([]uint32(nil), s.OpenModes...)
}

// directoryLister injects synthetic REALPATH errors without denying LIST/STAT.
// This isolates gateway command restrictions from the authenticated transport.
type directoryLister struct {
	next   sftp.FileLister
	server *Server
}

func (d directoryLister) Filelist(r *sftp.Request) (sftp.ListerAt, error) { return d.next.Filelist(r) }
func (d directoryLister) RealPath(input string) (string, error) {
	if delay := time.Duration(d.server.RealPathDelay.Load()); delay > 0 {
		time.Sleep(delay)
	}
	if code := d.server.RealPathStatus.Load(); code != 0 {
		if code == 8 {
			return "", sftp.ErrSSHFxOpUnsupported
		}
		if code == 3 {
			return "", sftp.ErrSSHFxPermissionDenied
		}
		return "", sftp.ErrSSHFxFailure
	}
	if next, ok := d.next.(sftp.RealPathFileLister); ok {
		return next.RealPath(input)
	}
	return path.Clean("/" + input), nil
}
