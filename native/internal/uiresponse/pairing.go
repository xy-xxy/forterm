package uiresponse

import "sync"

type activation struct {
	EventID        uint64
	Phase          int
	ReleasedNS     int64
	Status, Reason string
}
type pointerPress struct{ phase int }

// Native coordinates are classified at ingress and never stored. A pair needs
// both left-button down/up on the enabled test control in the same phase.
type eventPairer struct {
	mu                           sync.Mutex
	sequence                     uint64
	down                         *pointerPress
	pending                      []activation
	matched, unmatched, censored uint64
	reasons                      map[string]uint64
}

func (p *eventPairer) issue(reason string) {
	if p.reasons == nil {
		p.reasons = map[string]uint64{}
	}
	p.reasons[reason]++
}
func (p *eventPairer) press(hit bool, phase int) {
	p.mu.Lock()
	defer p.mu.Unlock()
	if p.down != nil {
		p.censored++
		p.issue("press_superseded")
	}
	p.down = nil
	if hit {
		p.down = &pointerPress{phase: phase}
	}
}
func (p *eventPairer) release(hit bool, phase int, now int64) {
	p.mu.Lock()
	defer p.mu.Unlock()
	down := p.down
	p.down = nil
	if down == nil {
		return
	}
	if !hit || down.phase != phase {
		p.censored++
		p.issue("pointer_gesture_cancelled_or_phase_changed")
		return
	}
	p.sequence++
	if len(p.pending) >= 512 {
		p.censored++
		p.issue("native_event_capacity")
		return
	}
	p.pending = append(p.pending, activation{EventID: p.sequence, Phase: phase, ReleasedNS: now, Status: "matched"})
}
func (p *eventPairer) take(phase int, pointer bool) activation {
	p.mu.Lock()
	defer p.mu.Unlock()
	if !pointer {
		return activation{Status: "unmatched", Reason: "non_pointer_activation"}
	}
	if len(p.pending) == 0 {
		return activation{Status: "unmatched", Reason: "no_paired_native_event"}
	}
	a := p.pending[0]
	p.pending = p.pending[1:]
	if a.Phase != phase {
		a.Status = "unmatched"
		a.Reason = "event_action_phase_mismatch"
		p.censored++
		p.issue(a.Reason)
	}
	return a
}
func (p *eventPairer) begin(a activation, now int64) (activation, *float64) {
	p.mu.Lock()
	defer p.mu.Unlock()
	if a.Status != "matched" || a.EventID == 0 {
		if a.Reason == "" {
			a.Status = "unmatched"
			a.Reason = "untracked_handler_invocation"
		}
		p.unmatched++
		p.issue(a.Reason)
		return a, nil
	}
	if now < a.ReleasedNS {
		a.Status = "unmatched"
		a.Reason = "non_monotonic_event_order"
		p.unmatched++
		p.issue(a.Reason)
		return a, nil
	}
	p.matched++
	ms := float64(now-a.ReleasedNS) / 1e6
	return a, &ms
}
func (p *eventPairer) censor(reason string) {
	p.mu.Lock()
	defer p.mu.Unlock()
	n := len(p.pending)
	if p.down != nil {
		n++
	}
	if n > 0 {
		p.censored += uint64(n)
		if p.reasons == nil {
			p.reasons = map[string]uint64{}
		}
		p.reasons[reason] += uint64(n)
	}
	p.pending = nil
	p.down = nil
}
func (p *eventPairer) snapshot() map[string]any {
	p.mu.Lock()
	defer p.mu.Unlock()
	reasons := map[string]uint64{}
	for k, v := range p.reasons {
		reasons[k] = v
	}
	return map[string]any{"matchedHandlers": p.matched, "unmatchedHandlers": p.unmatched, "censoredNativeEvents": p.censored, "pendingNativeEvents": len(p.pending), "reasons": reasons, "latencyCutoffApplied": false, "strategy": "target hit-tested left down/up, same phase, FIFO event IDs; non-pointer activation is unmatched"}
}
