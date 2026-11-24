package sshconn

import (
	"context"
	"time"
)

type KeepAlive struct {
	Enabled         bool
	IntervalSeconds int
	SFTPProbe       bool
}

func DefaultKeepAlive() KeepAlive {
	return KeepAlive{Enabled: true, IntervalSeconds: 15, SFTPProbe: true}
}
func normalizeKeepAlive(value KeepAlive) KeepAlive {
	if value.IntervalSeconds < 5 || value.IntervalSeconds > 600 {
		value.IntervalSeconds = 15
	}
	return value
}
func (c *Connection) probeFiles(ctx context.Context) {
	select {
	case c.fileLane <- struct{}{}:
		defer func() { <-c.fileLane }()
	default:
		return
	}
	c.fileMu.Lock()
	f := c.file
	c.fileMu.Unlock()
	if f == nil {
		return
	}
	answer := make(chan error, 1)
	go func() { _, err := f.RealPath("."); answer <- err }()
	select {
	case <-ctx.Done():
		return
	case err := <-answer:
		if err == nil {
			return
		}
	case <-time.After(8 * time.Second):
	}
	c.fileMu.Lock()
	owned := c.file == f
	if owned {
		c.file = nil
	}
	c.fileMu.Unlock()
	if owned {
		_ = f.Close()
	}
}
