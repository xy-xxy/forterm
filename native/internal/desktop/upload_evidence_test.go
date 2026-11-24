package desktop

import (
	"context"
	"encoding/json"
	"strings"
	"testing"

	"forterm/native/internal/transfers"
	"fyne.io/fyne/v2/test"
	"fyne.io/fyne/v2/widget"
)

func TestUploadEvidenceExcludesPathsMessagesAndCredentials(t *testing.T) {
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	q := transfers.New(ctx, func(ctx context.Context, _ transfers.Spec, _ func(int64, int64)) error {
		<-ctx.Done()
		return ctx.Err()
	}, nil)
	defer q.Close()
	q.Add(transfers.Spec{Direction: "upload", Local: "C:/secret/unique-password.txt", Remote: "/private/ticket-123"}, nil)
	w := &Workspace{Transfers: q, ID: "credential-id", Title: "private-host"}
	for i := 0; i < 100; i++ {
		w.uploadTrace.record(uploadCallback)
	}
	w.uploadTrace.record(uploadStage("secret-untrusted-string"))
	encoded := w.UploadEvidenceJSON()
	for _, secret := range []string{"secret", "password", "ticket-123", "private", "credential-id"} {
		if strings.Contains(encoded, secret) {
			t.Fatalf("evidence leaked %q", secret)
		}
	}
	var result struct {
		Version       string
		Events        []uploadEvent
		LatestUploads []struct {
			State transfers.State
			Phase transfers.Phase
		}
	}
	if err := json.Unmarshal([]byte(encoded), &result); err != nil {
		t.Fatal(err)
	}
	if result.Version != Version || len(result.Events) != 64 || len(result.LatestUploads) != 1 {
		t.Fatalf("invalid report: %+v", result)
	}
}

func TestClosedQueueNeverReportsSuccessfulUploadEnqueue(t *testing.T) {
	a := test.NewApp()
	defer a.Quit()
	q := transfers.New(context.Background(), func(context.Context, transfers.Spec, func(int64, int64)) error {
		t.Error("closed queue ran transfer")
		return nil
	}, nil)
	q.Close()
	window := a.NewWindow("closed queue")
	w := &Workspace{Context: context.Background(), FileConn: &missingTargetFS{}, Transfers: q, Window: window, Status: widget.NewLabel("")}
	w.EnqueueUploads([]string{"selected.txt"}, "/confirmed")
	if !strings.Contains(w.Status.Text, "队列已经关闭") || strings.Contains(w.Status.Text, "已加入") {
		t.Fatal(w.Status.Text)
	}
	if len(q.Snapshot()) != 0 {
		t.Fatal("closed queue received file")
	}
}
