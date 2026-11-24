package transfers

import (
	"context"
	"errors"
	"fmt"
	"sync"
	"sync/atomic"
	"time"
)

type State string

const (
	Queued     State = "queued"
	Running    State = "running"
	Cancelling State = "cancelling"
	Done       State = "done"
	Failed     State = "error"
	Cancelled  State = "cancelled"
)

type Spec struct {
	CommitGuard              func() error `json:"-"`
	Local, Remote, Direction string
	Overwrite                bool
}
type Item struct {
	TotalKnown                                  bool
	BytesPerSecond                              float64
	ETASeconds                                  float64
	ETAKnown                                    bool
	ID, Name, Direction, Local, Remote, Message string
	Phase                                       Phase
	State                                       State
	Bytes, Total                                int64
}
type Run func(context.Context, Spec, func(int64, int64)) error
type Before func(context.Context, Spec) (Spec, bool, error)
type task struct {
	run        Run
	resources  []string
	item       Item
	spec       Spec
	cancel     context.CancelFunc
	userCancel bool
	before     Before
}
type Queue struct {
	resources        []string
	observers        map[uint64]func([]Item)
	observerSequence uint64
	mu               sync.Mutex
	tasks            []*task
	active           *task
	wake             chan struct{}
	done             chan struct{}
	ctx              context.Context
	cancel           context.CancelFunc
	run              Run
	onChange         func([]Item)
	sequence         atomic.Uint64
}

func New(parent context.Context, run Run, onChange func([]Item)) *Queue {
	ctx, cancel := context.WithCancel(parent)
	q := &Queue{ctx: ctx, cancel: cancel, run: run, onChange: onChange, wake: make(chan struct{}, 1), done: make(chan struct{}), observers: map[uint64]func([]Item){}}
	go q.worker()
	return q
}
func (q *Queue) snapshotLocked() []Item {
	out := make([]Item, 0, len(q.tasks))
	for _, t := range q.tasks {
		out = append(out, t.item)
	}
	return out
}
func (q *Queue) Snapshot() []Item { q.mu.Lock(); defer q.mu.Unlock(); return q.snapshotLocked() }
func (q *Queue) emit() {
	q.mu.Lock()
	items := q.snapshotLocked()
	observers := make([]func([]Item), 0, len(q.observers)+1)
	if q.onChange != nil {
		observers = append(observers, q.onChange)
	}
	for _, fn := range q.observers {
		observers = append(observers, fn)
	}
	q.mu.Unlock()
	for _, fn := range observers {
		fn(items)
	}
}
func (q *Queue) Subscribe(fn func([]Item)) func() {
	q.mu.Lock()
	if q.observers == nil {
		q.observers = map[uint64]func([]Item){}
	}
	q.observerSequence++
	id := q.observerSequence
	q.observers[id] = fn
	q.mu.Unlock()
	fn(q.Snapshot())
	return func() { q.mu.Lock(); delete(q.observers, id); q.mu.Unlock() }
}

func (q *Queue) SetResources(keys ...string) {
	q.mu.Lock()
	q.resources = append([]string(nil), keys...)
	q.mu.Unlock()
}
func (q *Queue) Add(spec Spec, before Before) string { return q.AddOperation(spec, before, nil) }
func (q *Queue) AddOperation(spec Spec, before Before, run Run, keys ...string) string {
	q.mu.Lock()
	if q.ctx.Err() != nil {
		q.mu.Unlock()
		return ""
	}
	id := fmt.Sprintf("transfer-%d", q.sequence.Add(1))
	if len(keys) == 0 {
		keys = append([]string(nil), q.resources...)
	}
	t := &task{run: run, resources: keys, spec: spec, before: before, item: Item{ID: id, Direction: spec.Direction, Local: spec.Local, Remote: spec.Remote, State: Queued, Phase: PhaseQueued}}
	q.tasks = append(q.tasks, t)
	q.mu.Unlock()
	q.emit()
	select {
	case q.wake <- struct{}{}:
	default:
	}
	return id
}
func (q *Queue) Cancel(id string) {
	q.mu.Lock()
	var cancel context.CancelFunc
	for _, t := range q.tasks {
		if t.item.ID != id {
			continue
		}
		switch t.item.State {
		case Queued:
			t.item.State = Cancelled
			t.item.Message = "已取消排队，未打开文件流"
		case Running:
			t.userCancel = true
			t.item.State = Cancelling
			t.item.Message = "正在取消；同连接其余排队任务将停止"
			cancel = t.cancel
		}
		break
	}
	q.mu.Unlock()
	if cancel != nil {
		cancel()
	}
	q.emit()
}
func (q *Queue) ClearFinished() {
	q.mu.Lock()
	keep := q.tasks[:0]
	for _, t := range q.tasks {
		if t.item.State == Queued || t.item.State == Running || t.item.State == Cancelling {
			keep = append(keep, t)
		}
	}
	q.tasks = keep
	q.mu.Unlock()
	q.emit()
}
func (q *Queue) Close() {
	q.cancel()
	q.mu.Lock()
	for _, t := range q.tasks {
		if t.item.State == Queued {
			t.item.State = Cancelled
			t.item.Message = "连接已关闭，未开始"
		}
		if t.cancel != nil {
			t.userCancel = true
			t.cancel()
		}
	}
	q.mu.Unlock()
	q.emit()
}
func (q *Queue) Done() <-chan struct{} { return q.done }
func (q *Queue) pick() *task {
	q.mu.Lock()
	defer q.mu.Unlock()
	for _, t := range q.tasks {
		if t.item.State == Queued {
			t.item.State = Running
			t.item.Message = "检查文件信息"
			q.active = t
			return t
		}
	}
	return nil
}
func (q *Queue) stopPendingLocked() {
	for _, t := range q.tasks {
		if t.item.State == Queued {
			t.item.State = Failed
			t.item.Message = "前一项失败或取消，此项未开始；请检查后手动重新选择，不会自动重传"
		}
	}
}
func (q *Queue) worker() {
	defer close(q.done)
	for {
		select {
		case <-q.ctx.Done():
			return
		default:
		}
		t := q.pick()
		if t == nil {
			select {
			case <-q.ctx.Done():
				return
			case <-q.wake:
				continue
			}
		}
		ctx, cancel := context.WithCancel(q.ctx)
		ctx = withPhaseReporter(ctx, func(phase Phase) {
			q.mu.Lock()
			if t.item.State != Running {
				q.mu.Unlock()
				return
			}
			t.item.Phase = phase
			t.item.Message = phase.Label()
			if phase != PhaseData {
				t.item.BytesPerSecond = 0
				t.item.ETAKnown = false
			}
			q.mu.Unlock()
			q.emit()
		})
		q.mu.Lock()
		t.cancel = cancel
		if t.userCancel {
			cancel()
		}
		if t.before != nil {
			t.item.Message = "核对目标 / 等待覆盖确认"
		}
		q.mu.Unlock()
		q.emit()
		spec, proceed, err := t.spec, true, error(nil)
		var release func()
		ctx, release, err = AcquireResources(ctx, t.resources...)
		executed := false
		if t.before != nil && err == nil {
			spec, proceed, err = t.before(ctx, spec)
		}
		last := time.Time{}
		rateAt := time.Time{}
		rateBytes := int64(0)
		rateSamples := 0
		if err == nil && proceed && ctx.Err() == nil {
			executed = true
			run := t.run
			if run == nil {
				run = q.run
			}
			err = run(ctx, spec, func(done, total int64) {
				q.mu.Lock()
				if t.item.State == Running {
					t.item.Bytes, t.item.Total = done, total
					t.item.TotalKnown = total >= 0
					now := time.Now()
					if rateAt.IsZero() {
						rateAt = now
						rateBytes = done
					} else if elapsed := now.Sub(rateAt).Seconds(); elapsed >= .5 && done >= rateBytes {
						rate := float64(done-rateBytes) / elapsed
						t.item.BytesPerSecond = rate
						rateSamples++
						t.item.ETAKnown = rateSamples >= 2 && rate > 0 && total > done
						if t.item.ETAKnown {
							t.item.ETASeconds = float64(total-done) / rate
						}
						rateAt = now
						rateBytes = done
					}
					t.item.Message = ""
				}
				notify := time.Since(last) > 100*time.Millisecond
				if notify {
					last = time.Now()
				}
				q.mu.Unlock()
				if notify {
					q.emit()
				}
			})
		}
		if release != nil {
			release()
		}
		q.mu.Lock()
		if executed && err == nil {
			t.item.State = Done
			t.item.Message = "完成"
			if t.userCancel {
				t.item.Message = "取消请求到达时写入已确认，其余排队已停止"
				q.stopPendingLocked()
			}
		} else if t.userCancel || q.ctx.Err() != nil {
			t.item.State = Cancelled
			t.item.Message = "已取消活动传输，其余排队已停止；终端保持可用"
			q.stopPendingLocked()
		} else if err != nil {
			t.item.State = Failed
			t.item.Message = err.Error()
			q.stopPendingLocked()
		} else if !proceed {
			t.item.State = Cancelled
			t.item.Message = "已跳过，未打开文件流"
		} else if ctx.Err() != nil {
			t.item.State = Failed
			t.item.Message = "文件请求已结束，未自动重传"
			q.stopPendingLocked()
		} else {
			t.item.State = Done
			t.item.Message = "完成"
		}
		t.cancel = nil
		q.active = nil
		q.mu.Unlock()
		cancel()
		q.emit()
	}
}

var ErrStalled = errors.New("传输停滞超时，文件通道已重置；终端保持可用，未自动重传")
var ErrMetadata = errors.New("文件信息读取超时，未自动重传；请保留当前文件列表")

// Watchdog measures acknowledged byte progress. Metadata/close phases have their
// own shorter bound; server keepalives never extend either timeout.
type Watchdog struct {
	ctx             context.Context
	cancel          context.CancelCauseFunc
	last            atomic.Int64
	data            atomic.Bool
	metadata, stall time.Duration
}

func NewWatchdog(parent context.Context, metadata, stall time.Duration) *Watchdog {
	ctx, cancel := context.WithCancelCause(parent)
	w := &Watchdog{ctx: ctx, cancel: cancel, metadata: metadata, stall: stall}
	w.last.Store(time.Now().UnixNano())
	go func() {
		interval := min(metadata, stall) / 4
		if interval < 5*time.Millisecond {
			interval = 5 * time.Millisecond
		}
		if interval > 500*time.Millisecond {
			interval = 500 * time.Millisecond
		}
		tick := time.NewTicker(interval)
		defer tick.Stop()
		for {
			select {
			case <-ctx.Done():
				return
			case now := <-tick.C:
				limit := w.metadata
				cause := ErrMetadata
				if w.data.Load() {
					limit = w.stall
					cause = ErrStalled
				}
				if now.Sub(time.Unix(0, w.last.Load())) > limit {
					cancel(cause)
					return
				}
			}
		}
	}()
	return w
}
func (w *Watchdog) Context() context.Context { return w.ctx }
func (w *Watchdog) Data()                    { w.data.Store(true); w.last.Store(time.Now().UnixNano()) }
func (w *Watchdog) Progress()                { w.last.Store(time.Now().UnixNano()) }
func (w *Watchdog) Metadata()                { w.data.Store(false); w.last.Store(time.Now().UnixNano()) }
func (w *Watchdog) Stop()                    { w.cancel(context.Canceled) }
func (w *Watchdog) Cause() error             { return context.Cause(w.ctx) }
