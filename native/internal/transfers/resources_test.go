package transfers

import (
	"context"
	"testing"
	"time"
)

func TestOrderedTwoEndedResourceReservationsCancelAndReenter(t *testing.T) {
	ctx, release, e := AcquireResources(context.Background(), "B", "A")
	if e != nil {
		t.Fatal(e)
	}
	nested, again, e := AcquireResources(ctx, "A", "B")
	if e != nil || nested == nil {
		t.Fatal(e)
	}
	again()
	waiting, cancel := context.WithTimeout(context.Background(), 20*time.Millisecond)
	defer cancel()
	if _, _, e = AcquireResources(waiting, "A", "B"); e == nil {
		t.Fatal("contending mutation bypassed reservation")
	}
	release()
	ctx, release, e = AcquireResources(context.Background(), "A", "B")
	if e != nil {
		t.Fatal(e)
	}
	release()
	_ = ctx
	resources.Lock()
	defer resources.Unlock()
	if len(resources.lanes) != 0 {
		t.Fatal("idle connection keys leaked")
	}
}
