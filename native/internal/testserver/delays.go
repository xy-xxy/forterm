package testserver

import (
	"github.com/pkg/sftp"
	"io"
	"time"
)

type delayFileWriter struct {
	server *Server
	next   sftp.FileWriter
}

func (w delayFileWriter) Filewrite(r *sftp.Request) (io.WriterAt, error) {
	writer, e := w.next.Filewrite(r)
	if e != nil {
		return nil, e
	}
	return delayedWriterAt{WriterAt: writer, request: r, server: w.server}, nil
}

type delayedWriterAt struct {
	io.WriterAt
	request *sftp.Request
	server  *Server
}

func (w delayedWriterAt) WriteAt(p []byte, off int64) (int, error) {
	if delay := time.Duration(w.server.WriteDelay.Load()); delay > 0 {
		select {
		case <-time.After(delay):
		case <-w.request.Context().Done():
			return 0, w.request.Context().Err()
		}
	}
	return w.WriterAt.WriteAt(p, off)
}
func (w delayedWriterAt) Close() error {
	if closer, ok := w.WriterAt.(io.Closer); ok {
		return closer.Close()
	}
	return nil
}

type delayFileReader struct {
	server *Server
	next   sftp.FileReader
}

func (r delayFileReader) Fileread(request *sftp.Request) (io.ReaderAt, error) {
	reader, e := r.next.Fileread(request)
	if e != nil {
		return nil, e
	}
	return delayedReaderAt{ReaderAt: reader, request: request, server: r.server}, nil
}

type delayedReaderAt struct {
	io.ReaderAt
	request *sftp.Request
	server  *Server
}

func (r delayedReaderAt) ReadAt(p []byte, off int64) (int, error) {
	if delay := time.Duration(r.server.ReadDelay.Load()); delay > 0 {
		select {
		case <-time.After(delay):
		case <-r.request.Context().Done():
			return 0, r.request.Context().Err()
		}
	}
	return r.ReaderAt.ReadAt(p, off)
}
func (r delayedReaderAt) Close() error {
	if closer, ok := r.ReaderAt.(io.Closer); ok {
		return closer.Close()
	}
	return nil
}
