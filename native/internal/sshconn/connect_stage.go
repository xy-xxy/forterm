package sshconn

import "context"

type ConnectStage string

const (
	StageNetwork        ConnectStage = "network"
	StageNetworkReady   ConnectStage = "network_ready"
	StageFingerprint    ConnectStage = "fingerprint"
	StageTrusted        ConnectStage = "trusted"
	StageAuthentication ConnectStage = "authentication"
	StageAuthenticated  ConnectStage = "authenticated"
	StageInput          ConnectStage = "input"
	StageHandshake      ConnectStage = "handshake"
)

type ConnectEvent struct {
	Stage      ConnectStage
	Hop, Total int
}
type ConnectError struct {
	Stage     ConnectStage
	temporary bool
}

func (e *ConnectError) Error() string {
	switch e.Stage {
	case StageNetwork:
		return "SSH网络连接失败或超时"
	case StageFingerprint:
		return "SSH主机指纹未获确认"
	case StageAuthentication:
		if e.temporary {
			return "SSH认证未完成；临时授权未自动重试，请重新获取授权"
		}
		return "SSH认证阶段未完成，请检查认证方式、凭据或服务端规则"
	case StageInput:
		return "SSH认证参数或私钥不可用"
	default:
		return "SSH协议握手未完成，尚不能确认认证通过"
	}
}
func notifyConnect(cb Callbacks, stage ConnectStage) {
	if cb.Progress != nil {
		cb.Progress(ConnectEvent{Stage: stage})
	}
}
func (c *Connection) ProbeSFTP(ctx context.Context) error { _, err := c.getFile(ctx); return err }
