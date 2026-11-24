// Package uiresponse provides an explicit, offline operator-controlled check.
// Normal Forterm launches never enter it. Only static action IDs and timing
// numbers from synthetic metadata are recorded, never input text or profiles.
package uiresponse

import (
	"encoding/json"
	"fmt"
	"forterm/native/internal/desktop"
	"forterm/native/internal/settings"
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/canvas"
	"fyne.io/fyne/v2/container"
	"fyne.io/fyne/v2/dialog"
	"fyne.io/fyne/v2/widget"
	"github.com/go-gl/glfw/v3.4/glfw"
	"image"
	"image/color"
	"os"
	"path/filepath"
	"runtime"
	"sync"
	"time"
)

func Requested(args []string) bool { return len(args) == 1 && args[0] == "--ui-check" }

type sample struct {
	Action                   string   `json:"action"`
	NativeEventID            uint64   `json:"nativeEventID,omitempty"`
	PairingStatus            string   `json:"pairingStatus"`
	PairingReason            string   `json:"pairingReason,omitempty"`
	RasterStatus             string   `json:"rasterStatus"`
	RasterReason             string   `json:"rasterReason,omitempty"`
	Profiles                 int      `json:"syntheticProfiles"`
	NativeReleaseToHandlerMS *float64 `json:"nativeReleaseToHandlerMS,omitempty"`
	HandlerMS                float64  `json:"handlerMS"`
	HandlerToRasterMS        *float64 `json:"handlerToRasterMS,omitempty"`
}
type phase struct {
	tag, title string
	profiles   int
}

func Run() {
	tmp, e := os.MkdirTemp("", "forterm-ui-check-")
	if e != nil {
		return
	}
	defer os.RemoveAll(tmp)
	stores := map[int]*settings.Store{}
	for _, n := range []int{0, 1000} {
		dir := filepath.Join(tmp, fmt.Sprint(n))
		os.MkdirAll(dir, 0700)
		data := settings.Data{Version: 1, KnownHosts: map[string]string{}}
		for i := 0; i < n; i++ {
			id := fmt.Sprintf("fixture-%04d", i)
			data.Profiles = append(data.Profiles, settings.Profile{ID: id, Title: fmt.Sprintf("合成连接%04d", i), Group: fmt.Sprintf("合成分组%02d", i/100), Host: id + ".invalid", Username: "fixture", Protocol: "ssh", Port: 22})
		}
		raw, _ := json.Marshal(data)
		os.WriteFile(filepath.Join(dir, "config.json"), raw, 0600)
		stores[n] = settings.Open(dir, nil)
	}
	app := desktop.NewDiagnostic(fmt.Sprintf("cn.forterm.native.ui-check.%d", time.Now().UnixNano()))
	defer app.Close()
	app.Store = stores[0]
	app.Window.Resize(fyne.NewSize(1150, 760))
	base, _ := os.UserCacheDir()
	reportDir, e := os.MkdirTemp(base, "Forterm-UI-actions-")
	if e != nil {
		reportDir, e = os.MkdirTemp("", "Forterm-UI-actions-")
		if e != nil {
			app.Window.SetContent(widget.NewLabel("无法创建本机报告目录，检查尚未开始。"))
			app.Window.ShowAndRun()
			return
		}
	}
	reportPath := filepath.Join(reportDir, "Forterm-UI-actions.json")
	epoch := time.Now()
	pairer := &eventPairer{}
	var mu sync.Mutex
	records := []sample{}
	droppedSamples := 0
	pending := -1
	var pendingAt int64
	var writeMu sync.Mutex
	save := func() bool {
		writeMu.Lock()
		defer writeMu.Unlock()
		mu.Lock()
		copy := append([]sample(nil), records...)
		dropped := droppedSamples
		rasterCounts := map[string]int{}
		for _, s := range copy {
			rasterCounts[s.RasterStatus]++
		}
		mu.Unlock()
		return writeJSON(reportPath, map[string]any{"sourceVersion": desktop.Version, "platform": runtime.GOOS + "/" + runtime.GOARCH, "scope": "offline synthetic metadata, manual phases, monotonic timing; no existing connection configuration, input text, credential reads or network", "timingBoundary": "hit-tested GLFW left down/up to handler; marker generation only for unobscured probes. Modal/phase/close/coalesced probes are censored. Not popup visibility or final display presentation", "sampleLimit": 512, "samplesCensoredByCapacity": dropped, "rasterStatusCounts": rasterCounts, "pairingSummary": pairer.snapshot(), "samples": copy})
	}

	marker := canvas.NewRaster(func(_, _ int) image.Image {
		mu.Lock()
		if pending >= 0 {
			ms := float64(time.Since(epoch).Nanoseconds()-pendingAt) / 1e6
			if app.Window.Canvas().Overlays().Top() != nil {
				records[pending].RasterStatus = "censored"
				records[pending].RasterReason = "modal_overlay_at_marker; operator wait cannot be separated"
			} else {
				records[pending].HandlerToRasterMS = &ms
				records[pending].RasterStatus = "observed_marker"
			}
			pending = -1
		}
		mu.Unlock()
		img := image.NewRGBA(image.Rect(0, 0, 1, 1))
		img.Set(0, 0, color.NRGBA{G: 180, A: 255})
		return img
	})
	marker.SetMinSize(fyne.NewSize(8, 8))
	phases := []phase{{"response", "响应按钮", 0}, {"new_connection", "打开新建连接，再取消", 0}, {"preferences", "打开设置，再取消", 0}, {"library", "打开连接库，再关闭", 0}, {"navigation", "切换导航栏", 0}, {"library", "打开1000条合成连接库，再关闭", 1000}, {"preferences", "打开设置，再取消", 1000}, {"navigation", "切换导航栏", 1000}}
	index := 0
	count := 0
	status := widget.NewLabel("")
	status.Truncation = fyne.TextTruncateEllipsis
	hint := widget.NewLabel("每项建议点击3次；关闭弹窗后点下一项，不会自动计时切换。")
	var next *widget.Button
	var test *measuredButton
	update := func() {
		status.SetText(fmt.Sprintf("%d/%d · %s · 已处理%d次", index+1, len(phases), phases[index].title, count))
		if count == 0 {
			next.Disable()
		} else {
			next.Enable()
		}
	}
	test = newMeasuredButton("执行本项（离线）", pairer, func() int { return index }, func(activation activation) {
		p := phases[index]
		began := time.Since(epoch).Nanoseconds()
		activation, delay := pairer.begin(activation, began)
		switch p.tag {
		case "new_connection":
			app.ConnectDialog()
		case "preferences":
			app.ShowPreferences()
		case "library":
			app.ShowLibrary()
		case "navigation":
			app.ToggleNavigation()
		default:
		}
		ended := time.Since(epoch).Nanoseconds()
		s := sample{Action: p.tag, Profiles: p.profiles, HandlerMS: float64(ended-began) / 1e6, NativeEventID: activation.EventID, PairingStatus: activation.Status, PairingReason: activation.Reason, NativeReleaseToHandlerMS: delay, RasterStatus: "pending"}
		if app.Window.Canvas().Overlays().Top() != nil {
			s.RasterStatus = "censored"
			s.RasterReason = "modal_overlay_after_handler; operator wait cannot be separated"
		}
		mu.Lock()
		if pending >= 0 {
			records[pending].RasterStatus = "censored"
			records[pending].RasterReason = "superseded_before_marker_generation"
			pending = -1
		}
		if len(records) < 512 {
			records = append(records, s)
			if s.RasterStatus == "pending" {
				pending = len(records) - 1
			}
		} else {
			droppedSamples++
			pending = -1
		}
		pendingAt = ended
		mu.Unlock()
		marker.Refresh()
		count++
		update()
	})
	next = widget.NewButton("下一项", func() {
		pairer.censor("phase_advanced_before_handler")
		mu.Lock()
		if pending >= 0 {
			records[pending].RasterStatus = "censored"
			records[pending].RasterReason = "phase_advanced_before_marker"
			pending = -1
		}
		mu.Unlock()
		last := index+1 == len(phases)
		go func() {
			ok := save()
			fyne.Do(func() {
				if !ok {
					status.SetText("报告写入失败，未标记成功")
				} else if last {
					status.SetText("报告已保存：" + reportPath)
				}
			})
		}()
		if last {
			status.SetText("正在保存报告…")
			test.Disable()
			next.Disable()
			return
		}
		index++
		count = 0
		app.Store = stores[phases[index].profiles]
		update()
	})
	original := app.Window.Content()
	app.Window.SetContent(container.NewBorder(container.NewVBox(container.NewHBox(test, next, widget.NewButton("报告位置", func() { dialog.ShowInformation("离线操作检查报告", reportPath, app.Window) }), marker, status), hint), nil, nil, nil, original))
	update()
	app.UI.Lifecycle().SetOnStarted(func() {
		if ctx, ok := app.Window.(interface{ RunWithContext(func()) }); ok {
			ctx.RunWithContext(func() {
				if window := glfw.GetCurrentContext(); window != nil {
					var previous glfw.MouseButtonCallback
					previous = window.SetMouseButtonCallback(func(w *glfw.Window, b glfw.MouseButton, a glfw.Action, m glfw.ModifierKey) {
						if b == glfw.MouseButtonLeft {
							// Coordinates are used for classification only, never reported.
							hit := false
							if app.Window.Canvas().Overlays().Top() == nil && !test.Disabled() {
								x, y := w.GetCursorPos()
								ww, wh := w.GetSize()
								fw, fh := w.GetFramebufferSize()
								pos := app.UI.Driver().AbsolutePositionForObject(test)
								size := test.Size()
								left, top := app.Window.Canvas().PixelCoordinateForPosition(pos)
								right, bottom := app.Window.Canvas().PixelCoordinateForPosition(pos.Add(fyne.NewPos(size.Width, size.Height)))
								if ww > 0 && wh > 0 {
									px, py := x*float64(fw)/float64(ww), y*float64(fh)/float64(wh)
									hit = px >= float64(left) && px < float64(right) && py >= float64(top) && py < float64(bottom)
								}
							}
							if a == glfw.Press {
								pairer.press(hit, index)
							} else if a == glfw.Release {
								pairer.release(hit, index, time.Since(epoch).Nanoseconds())
							}
						}
						if previous != nil {
							previous(w, b, a, m)
						}
					})
				}
			})
		}
	})

	app.Window.ShowAndRun()
	pairer.censor("window_closed_before_handler")
	mu.Lock()
	if pending >= 0 {
		records[pending].RasterStatus = "censored"
		records[pending].RasterReason = "window_closed_before_marker"
		pending = -1
	}
	mu.Unlock()
	save()
}

func writeJSON(path string, value any) bool {
	raw, e := json.MarshalIndent(value, "", "  ")
	if e != nil {
		return false
	}
	file, e := os.CreateTemp(filepath.Dir(path), ".forterm-actions-*.tmp")
	if e != nil {
		return false
	}
	name := file.Name()
	defer os.Remove(name)
	_, e = file.Write(raw)
	if e == nil {
		e = file.Sync()
	}
	closed := file.Close()
	return e == nil && closed == nil && os.Rename(name, path) == nil
}
