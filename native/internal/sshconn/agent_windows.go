//go:build windows

package sshconn

import (
	"context"
	"github.com/Microsoft/go-winio"
	"net"
	"time"
)

func openAgent(ctx context.Context) (net.Conn, error) {
	ctx, cancel := context.WithTimeout(ctx, 2*time.Second)
	defer cancel()
	return winio.DialPipeContext(ctx, `\\.\pipe\openssh-ssh-agent`)
}
