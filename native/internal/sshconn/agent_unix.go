//go:build !windows

package sshconn

import (
	"context"
	"errors"
	"net"
	"os"
	"time"
)

func openAgent(ctx context.Context) (net.Conn, error) {
	socket := os.Getenv("SSH_AUTH_SOCK")
	if socket == "" {
		return nil, errors.New("未找到SSH认证代理")
	}
	return (&net.Dialer{Timeout: 2 * time.Second}).DialContext(ctx, "unix", socket)
}
