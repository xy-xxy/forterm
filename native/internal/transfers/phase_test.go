package transfers

import (
	"context"
	"testing"
)

func TestPhaseDistinguishesPreflightConfirmationAndStreamWait(t *testing.T) {
	gate := make(chan struct{})
	reached := make(chan struct{})
	q := New(context.Background(), func(ctx context.Context, _ Spec, _ func(int64, int64)) error {
		ReportPhase(ctx, PhaseRemoteOpen)
		close(reached)
		<-gate
		return nil
	}, nil)
	defer q.Close()
	id := q.Add(Spec{Direction: "upload"}, func(ctx context.Context, spec Spec) (Spec, bool, error) {
		ReportPhase(ctx, PhaseTargetStat)
		if got := q.Snapshot()[0].Phase; got != PhaseTargetStat {
			t.Errorf("bad reporter: %q", got)
		}
		ReportPhase(ctx, PhaseOverwrite)
		return spec, true, nil
	})
	if id == "" {
		t.Fatal("unexpected rejected task")
	}
	<-reached
	item := q.Snapshot()[0]
	if item.State != Running || item.Phase != PhaseRemoteOpen || item.Message != PhaseRemoteOpen.Label() {
		t.Fatalf("hidden phase: %+v", item)
	}
	close(gate)
	waitFor(t, func() bool { return q.Snapshot()[0].State == Done })
	q.Close()
	if got := q.Add(Spec{}, nil); got != "" {
		t.Fatal("closed queue accepted upload")
	}
}
