package testserver

import (
	"context"
	"golang.org/x/crypto/ssh"
	"io"
	"net"
	"strconv"
	"time"
)

func (s *Server) forward(incoming ssh.NewChannel) {
	var p struct {
		Host       string
		Port       uint32
		Origin     string
		OriginPort uint32
	}
	if ssh.Unmarshal(incoming.ExtraData(), &p) != nil || p.Host != "127.0.0.1" || p.Port < 1 || p.Port > 65535 {
		incoming.Reject(ssh.Prohibited, "synthetic loopback only")
		return
	}
	ctx, cancel := context.WithTimeout(context.Background(), 2*time.Second)
	defer cancel()
	remote, e := (&net.Dialer{}).DialContext(ctx, "tcp", net.JoinHostPort(p.Host, strconv.Itoa(int(p.Port))))
	if e != nil {
		incoming.Reject(ssh.ConnectionFailed, "unavailable")
		return
	}
	channel, reqs, e := incoming.Accept()
	if e != nil {
		remote.Close()
		return
	}
	s.Forward.Add(1)
	go ssh.DiscardRequests(reqs)
	go func() { io.Copy(channel, remote); channel.Close(); remote.Close() }()
	go func() { io.Copy(remote, channel); remote.Close(); channel.Close() }()
}
