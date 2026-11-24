package sshconn

import (
	"context"
	"errors"
	"github.com/pkg/sftp"
	"io"
	"os"
)

func (c *Connection) ReadStream(ctx context.Context, remote string, consume func(io.Reader, os.FileInfo) error) error {
	var file *sftp.File
	var client *sftp.Client
	var info os.FileInfo
	err := c.fileCall(ctx, func(f *sftp.Client) error {
		client = f
		stat, e := f.Lstat(remote)
		if e != nil {
			return e
		}
		if !stat.Mode().IsRegular() {
			return errors.New("仅允许普通文件，不跟随符号链接")
		}
		file, e = f.Open(remote)
		info = stat
		return e
	})
	if err != nil {
		return err
	}
	stop := context.AfterFunc(ctx, func() { c.resetStreamClient(client) })
	err = consume(file, info)
	closed := file.Close()
	stop()
	if err != nil {
		return err
	}
	return closed
}
func (c *Connection) WriteStream(ctx context.Context, remote string, produce func(io.Writer) error) error {
	var file *sftp.File
	var client *sftp.Client
	err := c.fileCall(ctx, func(f *sftp.Client) error {
		client = f
		var e error
		file, e = f.OpenFileWithMode(remote, os.O_CREATE|os.O_EXCL|os.O_WRONLY, 0600)
		return e
	})
	if err != nil {
		return err
	}
	stop := context.AfterFunc(ctx, func() { c.resetStreamClient(client) })
	defer stop()
	err = produce(file)
	closed := file.Close()
	if err != nil {
		return err
	}
	return closed
}
func (c *Connection) resetStreamClient(f *sftp.Client) {
	c.fileMu.Lock()
	if c.file == f {
		c.file = nil
	}
	c.fileMu.Unlock()
	_ = f.Close()
}
