package uiresponse

import (
	"fyne.io/fyne/v2"
	ftest "fyne.io/fyne/v2/test"
	"testing"
)

func TestSlowMatchedEventsAreNeverDroppedAtOneSecond(t *testing.T) {
	for _, ms := range []int64{999, 1000, 1500, 12000} {
		p := &eventPairer{}
		p.press(true, 3)
		p.release(true, 3, 100)
		a, delay := p.begin(p.take(3, true), 100+ms*1e6)
		if a.Status != "matched" || a.EventID != 1 || delay == nil || *delay != float64(ms) {
			t.Fatalf("slow event%d lost or misreported: %+v %v", ms, a, delay)
		}
	}
}
func TestPairingMissingMismatchCancellationAndFIFOAreExplicit(t *testing.T) {
	p := &eventPairer{}
	a, d := p.begin(p.take(0, true), 100)
	if a.Status != "unmatched" || d != nil || a.Reason != "no_paired_native_event" {
		t.Fatal("missing event became a numeric latency")
	}
	p.press(true, 0)
	p.release(true, 0, 200)
	a, d = p.begin(p.take(0, false), 300)
	if a.Status != "unmatched" || d != nil {
		t.Fatal("keyboard stole native event")
	}
	a, d = p.begin(p.take(0, true), 2000000300)
	if a.Status != "matched" || a.EventID != 1 || d == nil || *d < 2000 {
		t.Fatal("long pending event discarded")
	}
	p.press(true, 0)
	p.release(true, 0, 400)
	a, d = p.begin(p.take(1, true), 500)
	if a.Status != "unmatched" || d != nil || a.Reason != "event_action_phase_mismatch" {
		t.Fatal("phase mismatch hidden")
	}
	p.press(true, 2)
	p.release(false, 2, 600)
	p.press(true, 2)
	p.release(true, 2, 700)
	p.censor("phase_advance")
	if p.snapshot()["censoredNativeEvents"].(uint64) < 3 {
		t.Fatal("censored events not counted")
	}
	for i := 0; i < 2; i++ {
		p.press(true, 4)
		p.release(true, 4, int64(900+i))
	}
	first := p.take(4, true)
	second := p.take(4, true)
	if first.EventID >= second.EventID {
		t.Fatal("event IDs not FIFO")
	}
	a, d = p.begin(first, first.ReleasedNS-1)
	if a.Status != "unmatched" || d != nil {
		t.Fatal("negative event order became zero")
	}
}
func TestMeasuredButtonKeyboardAndPointerContextsDoNotMix(t *testing.T) {
	app := ftest.NewApp()
	defer app.Quit()
	p := &eventPairer{}
	var contexts []activation
	b := newMeasuredButton("fixture", p, func() int { return 0 }, func(a activation) { contexts = append(contexts, a) })
	p.press(true, 0)
	p.release(true, 0, 100)
	b.Tapped(&fyne.PointEvent{})
	b.TypedKey(&fyne.KeyEvent{Name: fyne.KeySpace})
	b.OnTapped()
	if len(contexts) != 3 || contexts[0].EventID != 1 || contexts[1].Reason != "keyboard_activation" || contexts[2].EventID != 0 {
		t.Fatal("button activation contexts leaked", contexts)
	}
}
