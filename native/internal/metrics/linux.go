package metrics

import (
	"math"
	"sort"
	"strconv"
	"strings"
	"time"
)

type CPUCounter struct {
	Values      [8]uint64
	Total, Idle uint64
	Valid       bool
}
type CoreUsage struct {
	ID      string
	Percent float64
	Valid   bool
	Reason  string
}
type Snapshot struct {
	NetworkCounters               map[string][2]uint64
	CPUCounter                    CPUCounter
	Cores                         map[string]CPUCounter
	Uptime                        float64
	UptimeValid                   bool
	DiskUsedBytes, DiskTotalBytes uint64
	DiskBytesValid                bool
	NetworkInterfaces             string
	At                            time.Time
	Load                          [3]string
	CPUTotal, CPUIdle             uint64
	CPUValid                      bool
	MemTotal, MemAvailable        uint64
	MemValid                      bool
	DiskUsed, DiskTotal           string
	DiskPercent                   float64
	DiskValid                     bool
	RX, TX                        uint64
	NetworkValid                  bool
}
type Delta struct {
	Cores        []CoreUsage
	CPUReason    string
	Elapsed      float64
	CPU          float64
	CPUValid     bool
	RX, TX       float64
	NetworkValid bool
}

func Parse(raw string, at time.Time) Snapshot {
	s := Snapshot{At: at, Cores: map[string]CPUCounter{}, NetworkCounters: map[string][2]uint64{}}
	diskKiB := false
	interfaceNames := []string{}
	memory := map[string]uint64{}
	memoryInvalid, networkInvalid := false, false
	var loopRX, loopTX uint64
	loop := false
	for _, line := range strings.Split(raw, "\n") {
		f := strings.Fields(line)
		if len(f) == 0 {
			continue
		}
		if len(f) >= 3 && strings.Contains(f[0], ".") {
			if _, e := strconv.ParseFloat(f[0], 64); e == nil {
				if _, e = strconv.ParseFloat(f[1], 64); e == nil {
					s.Load = [3]string{f[0], f[1], f[2]}
				}
			}
		}
		if f[0] == "UPTIME" && len(f) == 2 {
			n, e := strconv.ParseFloat(f[1], 64)
			if e == nil && n >= 0 && !math.IsNaN(n) && !math.IsInf(n, 0) {
				s.Uptime = n
				s.UptimeValid = true
			}
		}
		if strings.Contains(line, "1024-blocks") || strings.Contains(line, "1K-blocks") {
			diskKiB = true
		}
		if f[0] == "cpu" || validCPUName(f[0]) {
			counter := parseCPU(f[1:])
			if f[0] == "cpu" {
				s.CPUCounter = counter
				s.CPUValid = counter.Valid
				s.CPUTotal = counter.Total
				s.CPUIdle = counter.Idle
			} else {
				if _, exists := s.Cores[f[0]]; exists {
					counter.Valid = false
				}
				s.Cores[f[0]] = counter
			}
		}
		if len(f) >= 2 && strings.HasPrefix(f[0], "Mem") || len(f) >= 2 && (f[0] == "Buffers:" || f[0] == "Cached:") {
			n, e := strconv.ParseUint(f[1], 10, 64)
			name := strings.TrimSuffix(f[0], ":")
			if name == "MemTotal" || name == "MemAvailable" || name == "MemFree" || name == "Buffers" || name == "Cached" {
				if e == nil && n <= math.MaxInt64/1024 {
					memory[name] = n * 1024
				} else {
					memoryInvalid = true
				}
			}
		}
		if len(f) >= 6 && strings.HasSuffix(f[len(f)-2], "%") {
			n, e := strconv.ParseFloat(strings.TrimSuffix(f[len(f)-2], "%"), 64)
			if e == nil && n >= 0 && n <= 100 {
				s.DiskValid = true
				s.DiskPercent = n
				s.DiskTotal = f[1]
				s.DiskUsed = f[2]
				if diskKiB {
					total, e1 := strconv.ParseUint(f[1], 10, 64)
					used, e2 := strconv.ParseUint(f[2], 10, 64)
					if e1 == nil && e2 == nil && total > 0 && used <= total && total <= math.MaxUint64/1024 {
						s.DiskBytesValid = true
						s.DiskTotalBytes = total * 1024
						s.DiskUsedBytes = used * 1024
						s.DiskPercent = 100 * float64(used) / float64(total)
					}
				}
			}
		}
		if colon := strings.Index(line, ":"); colon > 0 {
			name := strings.TrimSpace(line[:colon])
			data := strings.Fields(line[colon+1:])
			if len(data) >= 16 {
				rx, e1 := strconv.ParseUint(data[0], 10, 64)
				tx, e2 := strconv.ParseUint(data[8], 10, 64)
				if e1 == nil && e2 == nil {
					if name == "lo" {
						loop = true
						loopRX, loopTX = rx, tx
					} else {
						s.NetworkValid = true
						interfaceNames = append(interfaceNames, name)
						if s.RX > math.MaxUint64-rx || s.TX > math.MaxUint64-tx {
							networkInvalid = true
						} else {
							s.RX += rx
							s.TX += tx
						}
						if _, exists := s.NetworkCounters[name]; exists {
							networkInvalid = true
						}
						s.NetworkCounters[name] = [2]uint64{rx, tx}
					}
				}
			}
		}
	}
	s.MemTotal = memory["MemTotal"]
	s.MemAvailable = memory["MemAvailable"]
	if _, ok := memory["MemAvailable"]; !ok {
		free, f := memory["MemFree"]
		buffers, b := memory["Buffers"]
		cached, c := memory["Cached"]
		if !f || !b || !c || free > math.MaxUint64-buffers || free+buffers > math.MaxUint64-cached {
			memoryInvalid = true
		} else {
			s.MemAvailable = free + buffers + cached
		}
	}
	s.MemValid = !memoryInvalid && s.MemTotal > 0 && s.MemAvailable <= s.MemTotal
	if !s.NetworkValid && loop {
		s.NetworkValid = true
		s.RX, s.TX = loopRX, loopTX
		s.NetworkCounters["lo"] = [2]uint64{loopRX, loopTX}
	}
	if networkInvalid {
		s.NetworkValid = false
	}
	sort.Strings(interfaceNames)
	s.NetworkInterfaces = strings.Join(interfaceNames, ",")
	if len(interfaceNames) == 0 && loop {
		s.NetworkInterfaces = "lo"
	}
	return s
}
func Between(previous, current Snapshot) Delta {
	d := Delta{}
	elapsed := current.At.Sub(previous.At).Seconds()
	reset := false
	if previous.UptimeValid && current.UptimeValid {
		elapsed = current.Uptime - previous.Uptime
		reset = elapsed <= 0
	}
	d.Elapsed = elapsed
	topology := len(previous.Cores) != len(current.Cores)
	for id := range previous.Cores {
		if _, ok := current.Cores[id]; !ok {
			topology = true
		}
	}
	ids := make([]string, 0, len(current.Cores))
	for id := range current.Cores {
		ids = append(ids, id)
	}
	sort.Slice(ids, func(i, j int) bool {
		a, _ := strconv.Atoi(strings.TrimPrefix(ids[i], "cpu"))
		b, _ := strconv.Atoi(strings.TrimPrefix(ids[j], "cpu"))
		return a < b
	})
	coreReset := false
	for _, id := range ids {
		usage := CoreUsage{ID: id, Reason: "等待两次有效采样"}
		before, found := previous.Cores[id]
		now := current.Cores[id]
		if found && !reset {
			usage.Percent, usage.Valid = cpuBetween(before, now)
			if before.Valid && now.Valid && !usage.Valid {
				usage.Reason = "计数未增长或发生重置"
				coreReset = true
			}
		} else if reset {
			usage.Reason = "采样时钟重置"
		}
		d.Cores = append(d.Cores, usage)
	}
	if !reset && !topology && !coreReset {
		if previous.CPUCounter.Valid && current.CPUCounter.Valid {
			d.CPU, d.CPUValid = cpuBetween(previous.CPUCounter, current.CPUCounter)
		} else if previous.CPUValid && current.CPUValid && current.CPUTotal > previous.CPUTotal && current.CPUIdle >= previous.CPUIdle {
			total, idle := current.CPUTotal-previous.CPUTotal, current.CPUIdle-previous.CPUIdle
			if idle <= total {
				d.CPUValid = true
				d.CPU = 100 * float64(total-idle) / float64(total)
			}
		}
	}
	if topology {
		d.CPUReason = "逻辑CPU集合变化，等待稳定双采样"
	} else if reset {
		d.CPUReason = "采样时钟重置，等待新样本"
	} else if !d.CPUValid {
		d.CPUReason = "等待两次有效且增长的CPU计数"
	}
	netReset := len(previous.NetworkCounters) != len(current.NetworkCounters)
	for id, before := range previous.NetworkCounters {
		now, ok := current.NetworkCounters[id]
		if !ok || now[0] < before[0] || now[1] < before[1] {
			netReset = true
		}
	}
	if elapsed > 0 && !reset && !netReset && previous.NetworkValid && current.NetworkValid && previous.NetworkInterfaces == current.NetworkInterfaces && current.RX >= previous.RX && current.TX >= previous.TX {
		d.NetworkValid = true
		d.RX = float64(current.RX-previous.RX) / elapsed
		d.TX = float64(current.TX-previous.TX) / elapsed
		if math.IsNaN(d.RX) || math.IsNaN(d.TX) || math.IsInf(d.RX, 0) || math.IsInf(d.TX, 0) {
			d.NetworkValid = false
		}
	}
	return d
}
func validCPUName(name string) bool {
	if !strings.HasPrefix(name, "cpu") || len(name) < 4 || len(name) > 9 {
		return false
	}
	for _, r := range name[3:] {
		if r < '0' || r > '9' {
			return false
		}
	}
	return true
}
func parseCPU(fields []string) CPUCounter {
	c := CPUCounter{}
	if len(fields) < 4 {
		return c
	}
	for i := 0; i < min(len(fields), 8); i++ {
		n, e := strconv.ParseUint(fields[i], 10, 64)
		if e != nil || c.Total > math.MaxUint64-n {
			return CPUCounter{}
		}
		c.Values[i] = n
		c.Total += n
	}
	if c.Values[3] > math.MaxUint64-c.Values[4] {
		return CPUCounter{}
	}
	c.Idle = c.Values[3] + c.Values[4]
	c.Valid = true
	return c
}
func cpuBetween(a, b CPUCounter) (float64, bool) {
	if !a.Valid || !b.Valid {
		return 0, false
	}
	for i, v := range b.Values {
		if v < a.Values[i] {
			return 0, false
		}
	}
	if b.Total <= a.Total || b.Idle < a.Idle {
		return 0, false
	}
	total, idle := b.Total-a.Total, b.Idle-a.Idle
	if idle > total {
		return 0, false
	}
	return 100 * float64(total-idle) / float64(total), true
}
