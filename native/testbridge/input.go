// Package testbridge exposes terminal/editor fixtures to the dependency's
// internal dispatcher tests. Production entry points never import this package.
package testbridge

import (
	"forterm/native/internal/codeedit"
	terminal "forterm/native/internal/vtwidget"
	"io"
	"sync"
	"time"
)

type CapturedTerminal struct {
	cleanup  func()
	output   *outputBuffer
	Terminal *terminal.Terminal
	Bytes    chan []byte
	reader   *io.PipeReader
	writer   *io.PipeWriter
	once     sync.Once
}
type captureWriter struct{ ch chan []byte }

func (w captureWriter) Write(data []byte) (int, error) {
	w.ch <- append([]byte(nil), data...)
	return len(data), nil
}
func (w captureWriter) Close() error { return nil }
func NewTerminal() *CapturedTerminal {
	r, w := io.Pipe()
	c := &CapturedTerminal{Terminal: terminal.New(), Bytes: make(chan []byte, 64), reader: r, writer: w}
	go func() { _ = c.Terminal.RunWithConnection(captureWriter{c.Bytes}, r) }()
	until := time.Now().Add(time.Second)
	for time.Now().Before(until) {
		if _, e := c.Terminal.Write(nil); e == nil {
			return c
		}
		time.Sleep(time.Millisecond)
	}
	panic("synthetic terminal did not become ready")
}
func (c *CapturedTerminal) Close() {
	c.once.Do(func() {
		c.Terminal.Close()
		if c.reader != nil {
			c.reader.Close()
		}
		if c.writer != nil {
			c.writer.Close()
		}
		if c.cleanup != nil {
			c.cleanup()
		}
	})
}

func NewEditor() *codeedit.Editor { return codeedit.New("synthetic.txt", "") }

type outputBuffer struct {
	mu   sync.Mutex
	data []byte
}

func (b *outputBuffer) Write(data []byte) (int, error) {
	b.mu.Lock()
	b.data = append(b.data, data...)
	b.mu.Unlock()
	return len(data), nil
}
func (c *CapturedTerminal) Output() string {
	if c.output == nil {
		return ""
	}
	c.output.mu.Lock()
	defer c.output.mu.Unlock()
	return string(c.output.data)
}
