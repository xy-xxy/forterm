package metrics

import (
	"math"
	"strings"
	"testing"
	"time"
)

func TestPerCoreActualCountersHotplugResetAndGuest(t *testing.T) {
	a := Parse("UPTIME 100\ncpu 100 10 20 850 20 0 0 0 50 5\ncpu0 50 5 10 420 15 0 0 0 20 2\ncpu1 50 5 10 430 5 0 0 0 30 3\neth0: 100 0 0 0 0 0 0 0 200 0 0 0 0 0 0 0", time.Unix(1000, 0))
	b := Parse("UPTIME 103\ncpu 130 10 40 900 20 0 0 0 999 999\ncpu0 70 5 20 440 15 0 0 0 800 800\ncpu1 60 5 20 460 5 0 0 0 199 199\neth0: 400 0 0 0 0 0 0 0 800 0 0 0 0 0 0 0", time.Unix(1017, 0))
	d := Between(a, b)
	if !d.CPUValid || math.Abs(d.CPU-50) > .001 || len(d.Cores) != 2 || !d.Cores[0].Valid || math.Abs(d.Cores[0].Percent-60) > .001 || math.Abs(d.Cores[1].Percent-40) > .001 {
		t.Fatalf("wrong per-core ratios: %+v", d)
	}
	if !d.NetworkValid || d.RX != 100 || d.TX != 200 || d.Elapsed != 3 {
		t.Fatal("rates did not use actual remote sample elapsed time")
	}
	first := Between(Snapshot{}, a)
	if first.CPUValid || first.Cores[0].Valid {
		t.Fatal("first sample invented CPU usage")
	}
	hot := b
	hot.Cores = map[string]CPUCounter{"cpu0": b.Cores["cpu0"], "cpu2": b.Cores["cpu1"]}
	changed := Between(a, hot)
	if changed.CPUValid || changed.Cores[1].Valid || changed.Cores[1].ID != "cpu2" {
		t.Fatal("hotplug reused wrong core")
	}
	reset := Parse(strings.ReplaceAll("UPTIME 104\ncpu 140 10 50 910 20 0 0 0\ncpu0 1 0 0 10 0 0 0 0\ncpu1 70 5 30 470 5 0 0 0", "noop", ""), time.Unix(1020, 0))
	r := Between(b, reset)
	if r.CPUValid || r.Cores[0].Valid {
		t.Fatal("counter rollback was treated as percentage")
	}
	zero := Between(b, b)
	if zero.CPUValid || zero.Cores[0].Valid || zero.NetworkValid {
		t.Fatal("zero elapsed/delta became valid zero")
	}
}
func TestMalformedAndMissingMetricsStayUnknown(t *testing.T) {
	s := Parse("cpu 1 nope 2 3\ncpu0 1 0 0 0\nMemTotal: 1000 kB\nMemAvailable: nope kB", time.Now())
	if s.CPUValid || s.MemValid {
		t.Fatal("malformed counters became valid")
	}
	if len(s.Cores) != 1 {
		t.Fatal("actual core id lost")
	}
	overflow := Parse("MemTotal: 18446744073709551615 kB\nMemAvailable: 1 kB", time.Now())
	if overflow.MemValid {
		t.Fatal("overflow memory was rendered")
	}
}
