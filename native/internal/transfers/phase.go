package transfers

import "context"

// Phase is a fixed vocabulary. It never contains file paths or server text.
type Phase string

const (
	PhaseVerify     Phase = "verify"
	PhaseQueued     Phase = "queued"
	PhaseFileLane   Phase = "file_channel"
	PhaseTargetStat Phase = "target_stat"
	PhaseOverwrite  Phase = "overwrite_confirmation"
	PhaseLocalOpen  Phase = "local_open"
	PhaseRemoteOpen Phase = "remote_open"
	PhaseData       Phase = "data"
	PhaseClose      Phase = "close"
)

func (p Phase) Label() string {
	switch p {
	case PhaseVerify:
		return "校验文件字节与版本"
	case PhaseQueued:
		return "排队等待"
	case PhaseFileLane:
		return "等待文件通道"
	case PhaseTargetStat:
		return "核对远程目标（STAT）"
	case PhaseOverwrite:
		return "等待覆盖确认"
	case PhaseLocalOpen:
		return "读取本地文件"
	case PhaseRemoteOpen:
		return "打开远程文件流"
	case PhaseData:
		return "传输数据"
	case PhaseClose:
		return "等待文件流关闭确认"
	}
	return ""
}

type phaseReporterKey struct{}

func withPhaseReporter(ctx context.Context, report func(Phase)) context.Context {
	return context.WithValue(ctx, phaseReporterKey{}, report)
}

func ReportPhase(ctx context.Context, phase Phase) {
	if ctx.Err() != nil || phase.Label() == "" {
		return
	}
	if report, ok := ctx.Value(phaseReporterKey{}).(func(Phase)); ok {
		report(phase)
	}
}
