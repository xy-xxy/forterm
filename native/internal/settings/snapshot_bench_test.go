package settings

import (
	"encoding/json"
	"fmt"
	"testing"
)

// This is the exact 0.2.1 clone path, kept only for isolated comparison.
func legacyJSONSnapshot(d Data) Data {
	raw, _ := json.Marshal(d)
	var out Data
	_ = json.Unmarshal(raw, &out)
	if out.KnownHosts == nil {
		out.KnownHosts = map[string]string{}
	}
	return out
}
func snapshotFixture(n int) Data {
	d := defaults()
	for i := 0; i < n; i++ {
		id := fmt.Sprintf("synthetic-%04d", i)
		d.Profiles = append(d.Profiles, Profile{ID: id, Title: "synthetic 中文 " + id, Group: fmt.Sprint(i % 20), Protocol: "ssh", Host: id + ".invalid", Port: 22, Username: "fixture", Auth: "password"})
		d.KnownHosts[id] = "SHA256:synthetic"
	}
	return d
}

var snapshotBenchmarkSink Data

func BenchmarkConfigurationSnapshot(b *testing.B) {
	for _, n := range []int{0, 100, 1000} {
		b.Run(fmt.Sprint(n), func(b *testing.B) {
			s := &Store{data: snapshotFixture(n)}
			b.Run("legacy-json", func(b *testing.B) {
				b.ReportAllocs()
				for i := 0; i < b.N; i++ {
					s.mu.Lock()
					snapshotBenchmarkSink = legacyJSONSnapshot(s.data)
					s.mu.Unlock()
				}
			})
			b.Run("current", func(b *testing.B) {
				b.ReportAllocs()
				for i := 0; i < b.N; i++ {
					snapshotBenchmarkSink = s.Snapshot()
				}
			})
		})
	}
}
