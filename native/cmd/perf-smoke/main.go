// Synthetic native UI responsiveness diagnostic. No network, files, credentials,
// clipboard, or user terminal contents are collected.
package main

import (
	"bytes"
	"encoding/json"
	"fmt"
	"forterm/native/internal/desktop"
	"forterm/native/internal/vtwidget"
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/container"
	ftest "fyne.io/fyne/v2/test"
	"fyne.io/fyne/v2/widget"
	"io"
	"os"
	"path/filepath"
	"runtime"
	"runtime/pprof"
	"sort"
	"strconv"
	"strings"
	"sync"
	"sync/atomic"
	"time"
)

type sink struct{ bytes, controlC atomic.Int64 }

func (s *sink) Write(p []byte) (int, error) {
	s.bytes.Add(int64(len(p)))
	for _, b := range p {
		if b == 3 {
			s.controlC.Add(1)
		}
	}
	return len(p), nil
}
func (*sink) Close() error { return nil }

type counted struct {
	io.Reader
	bytes atomic.Int64
}

func (c *counted) Read(p []byte) (int, error) {
	n, e := c.Reader.Read(p)
	c.bytes.Add(int64(n))
	return n, e
}

type phaseResult struct {
	Name                string
	Samples             int
	P50MS, P95MS, MaxMS float64
	Bytes               int64
	Seconds             float64
	CanvasObjects       int
	Clicks              int64
	RequestedBytes      int64
	Columns, Rows       uint
	SampleWindowSeconds float64
}

func main() {
	dir := os.Getenv("FORTERM_PERF_DIR")
	if dir == "" {
		dir = "evidence/performance-0.2.2/current"
	}
	os.MkdirAll(dir, 0700)
	if os.Getenv("FORTERM_CPU_PROFILE") == "1" {
		f, e := os.Create(filepath.Join(dir, "cpu.pprof"))
		if e == nil {
			pprof.StartCPUProfile(f)
			defer f.Close()
			defer pprof.StopCPUProfile()
		}
	}
	ui := desktop.New("cn.forterm.native.perf-smoke")
	term := vtwidget.New()
	defer term.Close()
	phaseLabel := widget.NewLabel("Synthetic performance diagnostic")
	var clicks atomic.Int64
	var phaseName string
	button := widget.NewButton("响应测试 / Response", func() {
		n := clicks.Add(1)
		phaseLabel.SetText(fmt.Sprintf("Click acknowledged %d · %s", n, phaseName))
		data, _ := json.Marshal(map[string]any{"clicks": n, "ackAt": time.Now().Format(time.RFC3339Nano), "phase": phaseName})
		os.WriteFile(filepath.Join(dir, "click-ack.json"), data, 0600)
	})
	started := make(chan struct{})
	var startOnce sync.Once
	startButton := widget.NewButton("Start synthetic load", func() { startOnce.Do(func() { close(started) }) })
	if os.Getenv("FORTERM_PERF_MANUAL") != "1" {
		startButton.Hide()
		close(started)
	}
	toolbar := container.NewHBox(startButton, button, phaseLabel)
	ui.Window.SetContent(container.NewBorder(toolbar, nil, nil, nil, term))
	ui.Window.Resize(fyne.NewSize(1100, 700))
	dimensions := make(chan vtwidget.Config, 64)
	term.AddListener(dimensions)
	defer term.RemoveListener(dimensions)
	grid := os.Getenv("FORTERM_PERF_GRID")
	if parts := strings.Split(grid, "x"); len(parts) == 2 {
		cols, _ := strconv.Atoi(parts[0])
		rows, _ := strconv.Atoi(parts[1])
		cell := fyne.MeasureText("M", 13, fyne.TextStyle{Monospace: true})
		if cols >= 80 && rows >= 20 {
			ui.Window.Resize(fyne.NewSize(float32(cols)*cell.Width, float32(rows)*cell.Height+toolbar.MinSize().Height+4))
		}
	}
	reader, writer := io.Pipe()
	out := &counted{Reader: reader}
	input := &sink{}
	go term.RunWithConnection(input, out)
	go func() {
		defer fyne.Do(ui.UI.Quit)
		<-started
		if parts := strings.Split(grid, "x"); len(parts) == 2 {
			wantedCols, _ := strconv.Atoi(parts[0])
			wantedRows, _ := strconv.Atoi(parts[1])
			time.Sleep(200 * time.Millisecond)
			for attempt := 0; attempt < 6; attempt++ {
				exact := false
				fyne.DoAndWait(func() {
					var c vtwidget.Config
					for len(dimensions) > 0 {
						c = <-dimensions
					}
					if c.Columns == 0 {
						return
					}
					dimensions <- c
					if int(c.Columns) == wantedCols && int(c.Rows) == wantedRows {
						exact = true
						return
					}
					size := ui.Window.Canvas().Size()
					m := fyne.MeasureText("M", 13, fyne.TextStyle{Monospace: true})
					ui.Window.Resize(fyne.NewSize(size.Width+float32(wantedCols-int(c.Columns))*m.Width, size.Height+float32(wantedRows-int(c.Rows))*m.Height))
				})
				if exact {
					break
				}
				time.Sleep(100 * time.Millisecond)
			}
		}
		var results []phaseResult
		var lastColumns, lastRows uint
		duration := 3 * time.Second
		if os.Getenv("FORTERM_PERF_LONG") == "1" {
			duration = 12 * time.Second
		}
		phases := []string{"idle", "low-small-block", "fixed-rate", "burst-output", "idle-after-output"}
		if os.Getenv("FORTERM_PERF_BURST_ONLY") == "1" {
			phases = []string{"burst-output"}
			duration = 40 * time.Second
		}
		for _, name := range phases {
			fyne.DoAndWait(func() { phaseName = name; phaseLabel.SetText(name) })
			start := time.Now()
			before := out.bytes.Load()
			clickBefore := clicks.Load()
			var stop atomic.Bool
			var produced atomic.Int64
			producerDone := make(chan struct{})
			var wg sync.WaitGroup
			wg.Add(1)
			go func() {
				defer wg.Done()
				defer close(producerDone)
				if name == "idle" || name == "idle-after-output" {
					return
				}

				seq := 0
				fixed := name == "fixed-rate" || name == "low-small-block"
				blocks := 60
				interval := duration / time.Duration(blocks)
				for !stop.Load() || fixed && seq < blocks {
					if fixed && seq >= blocks {
						return
					}
					if fixed {
						time.Sleep(max(0, time.Until(start.Add(time.Duration(seq)*interval))))
					}
					seq++
					line := fmt.Sprintf("%08d \x1b[32mANSI\x1b[0m synthetic 中文 output 0123456789 abcdefghijklmnopqrstuvwxyz\r\n", seq)
					block := bytes.Repeat([]byte(line), 32)
					if name == "fixed-rate" {
						block = bytes.Repeat([]byte(line), 16)
					}
					if name == "low-small-block" {
						block = []byte(fmt.Sprintf("small-%08d\r\n", seq))
					}
					n, e := writer.Write(block)
					produced.Add(int64(n))
					if e != nil {
						return
					}
				}

			}()
			var delays []float64
			for time.Since(start) < duration || ((name == "fixed-rate" || name == "low-small-block") && !channelClosed(producerDone)) {
				posted := time.Now()
				fyne.DoAndWait(func() { delays = append(delays, float64(time.Since(posted))/float64(time.Millisecond)) })
				time.Sleep(25 * time.Millisecond)
			}
			stop.Store(true)
			wg.Wait()
			// Ordered sentinel verifies all accepted bytes were parsed before recording.
			marker := "PERF_PHASE_DONE_" + name
			if produced.Load() > 0 {
				writer.Write([]byte("\r\n" + marker + "\r\n"))
			}
			ok := produced.Load() == 0
			for i := 0; i < 100 && !ok; i++ {
				fyne.DoAndWait(func() { ok = strings.Contains(term.Text(), marker) })
				if !ok {
					time.Sleep(10 * time.Millisecond)
				}
			}
			if !ok {
				panic("accepted synthetic output did not reach VT screen")
			}
			sort.Float64s(delays)
			r := phaseResult{SampleWindowSeconds: duration.Seconds(), Name: name, Samples: len(delays), Seconds: time.Since(start).Seconds(), Bytes: out.bytes.Load() - before, Clicks: clicks.Load() - clickBefore}
			if len(delays) > 0 {
				r.P50MS = delays[len(delays)/2]
				r.P95MS = delays[min(len(delays)-1, len(delays)*95/100)]
				r.MaxMS = delays[len(delays)-1]
			}
			fyne.DoAndWait(func() {
				r.CanvasObjects = len(ftest.WidgetRenderer(term).Objects())
				for len(dimensions) > 0 {
					c := <-dimensions
					r.Columns, r.Rows = c.Columns, c.Rows
				}
			})
			if r.Columns == 0 {
				r.Columns, r.Rows = lastColumns, lastRows
			}
			lastColumns, lastRows = r.Columns, r.Rows
			if name == "fixed-rate" || name == "low-small-block" {
				r.RequestedBytes = produced.Load()
			}
			results = append(results, r)
			payload, _ := json.MarshalIndent(map[string]any{"inputByteCount": input.bytes.Load(), "controlCByteCount": input.controlC.Load(), "requestedGrid": grid, "durationPerPhaseSeconds": duration.Seconds(), "scale": os.Getenv("FYNE_SCALE"), "platform": runtime.GOOS + "/" + runtime.GOARCH, "scope": "synthetic native VT + UI queue; no production data", "outputSentinelsVerified": true, "phases": results}, "", "  ")
			os.WriteFile(filepath.Join(dir, "result.json"), payload, 0600)
		}
		writer.Close()
	}()
	ui.Window.ShowAndRun()
}

func channelClosed(ch <-chan struct{}) bool {
	select {
	case <-ch:
		return true
	default:
		return false
	}
}
