package sshconn

import (
	"context"
	"errors"
	"io"
	"strings"
	"sync"
	"time"
)

// MonitorCommand is fixed and read-only. It runs on a separate SSH exec channel,
// never through terminal stdin, and does not install an agent.
const MonitorCommand = "LC_ALL=C; printf 'UPTIME '; cut -d' ' -f1 /proc/uptime; printf '负载（1 / 5 / 15 分钟）\\n'; cat /proc/loadavg; printf '\\nCPU 累计计数（USER_HZ）\\n'; awk '/^cpu([0-9]+)?[[:space:]]/ {print}' /proc/stat; printf '\\n内存（KiB）\\n'; head -5 /proc/meminfo; printf '\\n磁盘\\n'; df -kP /; printf '\\n网络累计字节 / 数据包\\n'; cat /proc/net/dev"

type limitedOutput struct {
	mu       sync.Mutex
	text     strings.Builder
	overflow bool
}

func (w *limitedOutput) Write(p []byte) (int, error) {
	w.mu.Lock()
	defer w.mu.Unlock()
	if w.text.Len()+len(p) > 64*1024 {
		w.overflow = true
		return 0, io.ErrShortBuffer
	}
	return w.text.Write(p)
}
func (c *Connection) Monitor(parent context.Context) (string, error) {
	if !c.monitoring.CompareAndSwap(false, true) {
		return "", errors.New("监控请求仍在进行，未叠加请求")
	}
	ctx, cancel := context.WithTimeout(parent, 8*time.Second)
	defer cancel()
	type response struct {
		text string
		err  error
	}
	result := make(chan response, 1)
	go func() {
		defer c.monitoring.Store(false)
		session, e := c.client.NewSession()
		if e != nil {
			result <- response{err: errors.New("exec通道未授权；终端和文件仍可使用")}
			return
		}
		defer session.Close()
		stop := context.AfterFunc(ctx, func() { _ = session.Close() })
		defer stop()
		if ctx.Err() != nil {
			result <- response{err: ctx.Err()}
			return
		}
		output := &limitedOutput{}
		session.Stdout = output
		session.Stderr = output
		e = session.Run(MonitorCommand)
		output.mu.Lock()
		text, overflow := output.text.String(), output.overflow
		output.mu.Unlock()
		if overflow {
			result <- response{err: errors.New("监控输出超过64KiB限制")}
			return
		}
		if e != nil {
			result <- response{err: errors.New("Linux指标不可用或exec执行被拒绝；其他通道保留")}
			return
		}
		result <- response{text: text}
	}()
	select {
	case <-ctx.Done():
		return "", errors.New("监控取消或超时；不会断开共享SSH或重叠请求")
	case r := <-result:
		return r.text, r.err
	}
}
