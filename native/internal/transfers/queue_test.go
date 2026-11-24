package transfers

import (
	"context"
	"fmt"
	"sync"
	"testing"
	"time"
)

func waitFor(t *testing.T, check func() bool) {
	t.Helper()
	until := time.Now().Add(2 * time.Second)
	for !check() {
		if time.Now().After(until) {
			t.Fatal("queue transition timeout")
		}
		time.Sleep(time.Millisecond)
	}
}
func TestTwentyPlusAppendAndSerialExecution(t *testing.T) {
	var mu sync.Mutex
	var order []string
	active, maxActive := 0, 0
	gate := make(chan struct{})
	q := New(context.Background(), func(ctx context.Context, s Spec, progress func(int64, int64)) error {
		mu.Lock()
		active++
		if active > maxActive {
			maxActive = active
		}
		mu.Unlock()
		if s.Remote == "0" {
			<-gate
		}
		progress(5, 10)
		mu.Lock()
		order = append(order, s.Remote)
		active--
		mu.Unlock()
		return nil
	}, nil)
	defer q.Close()
	for i := 0; i < 21; i++ {
		q.Add(Spec{Remote: fmt.Sprint(i)}, nil)
	}
	waitFor(t, func() bool { return q.Snapshot()[0].State == Running })
	for i := 21; i < 25; i++ {
		q.Add(Spec{Remote: fmt.Sprint(i)}, nil)
	}
	close(gate)
	waitFor(t, func() bool { snapshot := q.Snapshot(); return snapshot[len(snapshot)-1].State == Done })
	mu.Lock()
	defer mu.Unlock()
	if maxActive != 1 || len(order) != 25 {
		t.Fatal("queue overlapped or lost appended tasks")
	}
	for i, name := range order {
		if name != fmt.Sprint(i) {
			t.Fatal("FIFO changed")
		}
	}
}
func TestCancelQueuedDoesNotOpenAndActiveCancellationStopsPending(t *testing.T) {
	entered := make(chan struct{}, 2)
	q := New(context.Background(), func(ctx context.Context, s Spec, p func(int64, int64)) error {
		entered <- struct{}{}
		<-ctx.Done()
		return ctx.Err()
	}, nil)
	defer q.Close()
	first := q.Add(Spec{Remote: "active"}, nil)
	second := q.Add(Spec{Remote: "queued"}, nil)
	q.Add(Spec{Remote: "stop-after-active"}, nil)
	<-entered
	q.Cancel(second)
	if q.Snapshot()[1].State != Cancelled {
		t.Fatal("queued cancel did not finish")
	}
	q.Cancel(first)
	waitFor(t, func() bool { items := q.Snapshot(); return items[0].State == Cancelled && items[2].State == Failed })
	select {
	case <-entered:
		t.Fatal("pending transfer opened after active cancellation")
	default:
	}
}
func TestMetadataAndByteStallBounds(t *testing.T) {
	w := NewWatchdog(context.Background(), 30*time.Millisecond, 70*time.Millisecond)
	defer w.Stop()
	select {
	case <-w.Context().Done():
		if w.Cause() != ErrMetadata {
			t.Fatal(w.Cause())
		}
	case <-time.After(time.Second):
		t.Fatal("metadata did not timeout")
	}
	w = NewWatchdog(context.Background(), 30*time.Millisecond, 70*time.Millisecond)
	defer w.Stop()
	w.Data()
	for i := 0; i < 3; i++ {
		time.Sleep(20 * time.Millisecond)
		w.Progress()
	}
	if w.Context().Err() != nil {
		t.Fatal("acknowledged progress timed out")
	}
	select {
	case <-w.Context().Done():
		if w.Cause() != ErrStalled {
			t.Fatal(w.Cause())
		}
	case <-time.After(time.Second):
		t.Fatal("stalled stream did not timeout")
	}
}
