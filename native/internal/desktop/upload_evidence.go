package desktop

import (
	"encoding/json"
	"sync"
	"time"

	"forterm/native/internal/transfers"
)

type uploadStage string

const (
	uploadRequested     uploadStage = "upload_requested"
	uploadPickerShown   uploadStage = "picker_open_requested"
	uploadPickerClosed  uploadStage = "picker_closed"
	uploadCallback      uploadStage = "picker_callback"
	uploadCancelled     uploadStage = "selection_cancelled"
	uploadReaderClose   uploadStage = "selected_reader_close"
	uploadPathConverted uploadStage = "selected_path_converted"
	uploadEnqueue       uploadStage = "enqueue_requested"
	uploadQueued        uploadStage = "queue_accepted"
	uploadRejected      uploadStage = "upload_rejected"
)

type uploadEvent struct {
	Stage   uploadStage `json:"stage"`
	AfterMS int64       `json:"afterMs"`
}

// Trace is bounded, in memory only and records a closed vocabulary. Do not add
// URIs, errors, local/remote names, session identifiers or command arguments.
type uploadTrace struct {
	mu     sync.Mutex
	start  time.Time
	events []uploadEvent
}

func (t *uploadTrace) record(stage uploadStage) {
	switch stage {
	case uploadRequested, uploadPickerShown, uploadPickerClosed, uploadCallback,
		uploadCancelled, uploadReaderClose, uploadPathConverted, uploadEnqueue,
		uploadQueued, uploadRejected:
	default:
		return
	}
	t.mu.Lock()
	defer t.mu.Unlock()
	if t.start.IsZero() {
		t.start = time.Now()
	}
	if len(t.events) == 64 {
		copy(t.events, t.events[1:])
		t.events = t.events[:63]
	}
	t.events = append(t.events, uploadEvent{stage, time.Since(t.start).Milliseconds()})
}

func (t *uploadTrace) snapshot() []uploadEvent {
	t.mu.Lock()
	defer t.mu.Unlock()
	return append([]uploadEvent{}, t.events...)
}

func (w *Workspace) UploadEvidenceJSON() string {
	type queueEntry struct {
		State transfers.State `json:"state"`
		Phase transfers.Phase `json:"phase"`
	}
	items := []queueEntry{}
	if w.Transfers != nil {
		for _, item := range w.Transfers.Snapshot() {
			if item.Direction != "upload" {
				continue
			}
			items = append(items, queueEntry{item.State, item.Phase})
		}
	}
	if len(items) > 20 {
		items = items[len(items)-20:]
	}
	data, _ := json.MarshalIndent(struct {
		Version string        `json:"version"`
		Events  []uploadEvent `json:"events"`
		Uploads []queueEntry  `json:"latestUploads"`
	}{Version, w.uploadTrace.snapshot(), items}, "", "  ")
	return string(data)
}

func (w *Workspace) CopyUploadEvidence() {
	w.Window.Clipboard().SetContent(w.UploadEvidenceJSON())
	w.Status.SetText("已复制上传阶段信息（仅版本、阶段、计时和队列状态；不含路径、文件名或凭据）")
}
