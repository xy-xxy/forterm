package ftpconn

import (
	"context"
	"errors"
	"io"
	"os"
)

func (c *Connection) ReadStream(ctx context.Context, remote string, consume func(io.Reader, os.FileInfo) error) error {
	name, err := c.resolve(remote)
	if err != nil {
		return err
	}
	return c.run(ctx, func() error {
		info, e := c.stat(name)
		if e != nil {
			return e
		}
		if !info.Mode().IsRegular() {
			return errors.New("仅允许普通FTP文件，不跟随链接")
		}
		reader, e := c.client.Retr(name)
		if e != nil {
			return e
		}
		e = consume(reader, info)
		closed := reader.Close()
		if e != nil {
			return e
		}
		return closed
	})
}
func (c *Connection) WriteStream(ctx context.Context, remote string, produce func(io.Writer) error) error {
	name, err := c.resolve(remote)
	if err != nil {
		return err
	}
	return c.run(ctx, func() error {
		if _, e := c.stat(name); e == nil {
			return errors.New("临时目标已存在，未覆盖")
		} else if !os.IsNotExist(e) {
			return e
		}
		reader, writer := io.Pipe()
		finished := make(chan error, 1)
		go func() { e := produce(writer); writer.CloseWithError(e); finished <- e }()
		e := c.client.Stor(name, reader)
		reader.CloseWithError(e)
		p := <-finished
		if e != nil {
			return e
		}
		return p
	})
}
