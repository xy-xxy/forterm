package metrics

import (
	"testing"
	"time"
)

func TestRealCounterDeltasAndMissingData(t *testing.T) {
	a := Parse("1.00 2.00 3.00 1/10\ncpu 100 0 50 850 0 0 0 0\nMemTotal: 1000 kB\nMemAvailable: 400 kB\n/dev/test 10G 3G 7G 30% /\neth0: 1000 0 0 0 0 0 0 0 2000 0 0 0 0 0 0 0", time.Unix(100, 0))
	b := Parse("cpu 125 0 50 925 0 0 0 0\neth0: 1200 0 0 0 0 0 0 0 2100 0 0 0 0 0 0 0", time.Unix(102, 0))
	d := Between(a, b)
	if !d.CPUValid || d.CPU != 25 || d.RX != 100 || d.TX != 50 || a.DiskPercent != 30 || !a.MemValid {
		t.Fatal(a, d)
	}
	if Between(Snapshot{}, b).CPUValid {
		t.Fatal("invented a first-sample CPU rate")
	}
	if Between(a, a).CPUValid {
		t.Fatal("load/counter reset shown as CPU usage")
	}
}
