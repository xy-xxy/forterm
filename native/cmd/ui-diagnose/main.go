// Offline, opt-in Windows UI diagnostic. A fresh app/store is used for each
// child. Reports contain timings/hardware numbers only; never screenshots,
// credentials, environment dumps, window titles, input text or file contents.
package main

import (
	"encoding/json"
	"fmt"
	"forterm/native/internal/desktop"
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/app"
	"fyne.io/fyne/v2/canvas"
	"fyne.io/fyne/v2/container"
	"fyne.io/fyne/v2/dialog"
	"fyne.io/fyne/v2/widget"
	"github.com/go-gl/gl/v2.1/gl"
	"github.com/go-gl/glfw/v3.4/glfw"
	"image"
	"image/color"
	"os"
	"os/exec"
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

type distribution struct {
	Samples             int
	P50MS, P95MS, MaxMS float64
}

func stats(v []float64) distribution {
	if len(v) == 0 {
		return distribution{}
	}
	s := append([]float64(nil), v...)
	sort.Float64s(s)
	return distribution{len(s), s[len(s)/2], s[min(len(s)-1, len(s)*95/100)], s[len(s)-1]}
}
func save(path string, value any) bool {
	b, e := json.MarshalIndent(value, "", "  ")
	if e != nil {
		return false
	}
	f, e := os.CreateTemp(filepath.Dir(path), ".forterm-report-*.tmp")
	if e != nil {
		return false
	}
	temporary := f.Name()
	defer os.Remove(temporary)
	_, writeErr := f.Write(b)
	if writeErr == nil {
		writeErr = f.Sync()
	}
	closeErr := f.Close()
	if writeErr != nil || closeErr != nil {
		return false
	}
	return os.Rename(temporary, path) == nil
}

func main() {
	stage, dir := "", ""
	for _, a := range os.Args[1:] {
		if strings.HasPrefix(a, "--stage=") {
			stage = strings.TrimPrefix(a, "--stage=")
		}
		if strings.HasPrefix(a, "--output=") {
			dir = strings.TrimPrefix(a, "--output=")
		}
	}
	if stage == "standard" || stage == "forterm" {
		runStage(stage, dir, strings.Contains(strings.Join(os.Args[1:], " "), "--auto-start"))
		return
	}
	// No arbitrary arguments are forwarded; the report directory is generated here.
	exe, e := os.Executable()
	if e != nil {
		return
	}
	dir = filepath.Join(filepath.Dir(exe), "Forterm-UI-report-"+time.Now().Format("20060102-150405"))
	if os.MkdirAll(dir, 0700) != nil {
		base, _ := os.UserCacheDir()
		dir = filepath.Join(base, "Forterm-UI-report-"+time.Now().Format("20060102-150405"))
		os.MkdirAll(dir, 0700)
	}
	results := map[string]any{"diagnosticVersion": "0.2.2-diag2", "networkUsed": false, "existingConfigurationRead": false, "systemVaultAccessed": false, "collected": "UI timing, GL renderer/version, display scale/resolution and process resource counts only"}
	allCompleted := true
	for _, s := range []string{"standard", "forterm"} {
		args := []string{"--stage=" + s, "--output=" + dir}
		if s == "forterm" {
			args = append(args, "--auto-start")
		}
		cmd := exec.Command(exe, args...)
		hide(cmd)
		runErr := cmd.Run()
		raw, readErr := os.ReadFile(filepath.Join(dir, s+".json"))
		value, complete := stageOutcome(runErr, readErr, raw)
		if value != nil {
			results[s] = value
		}
		if runErr != nil {
			results[s+"Process"] = "child did not exit normally"
		}
		if readErr != nil {
			results[s+"Report"] = "cancelled or report not available"
		}
		if !complete {
			results[s+"Complete"] = false
			allCompleted = false
			break
		}
	}
	results["completedAllStages"] = allCompleted

	written := save(filepath.Join(dir, "Forterm-UI-report.json"), results)
	a := app.NewWithID("cn.forterm.ui-diagnostic.result")
	w := a.NewWindow("Forterm 离线诊断完成")
	text := widget.NewLabel("报告已生成：\n" + filepath.Join(dir, "Forterm-UI-report.json") + "\n\n请只返回这一个JSON文件。报告不含账号、连接、终端或剪贴板内容。")
	if !allCompleted {
		w.SetTitle("Forterm 诊断已取消或未完成")
		text.SetText("诊断未全部完成，已保留可用状态：\n" + filepath.Join(dir, "Forterm-UI-report.json") + "\n\n没有启动未获完整完成确认的下一阶段；没有上传数据。您可保留报告用于排查。")
	}
	if !written {
		w.SetTitle("Forterm 诊断报告写入失败")
		text.SetText("无法写入本地诊断报告。请把诊断程序解压到可写目录后再试；没有上传任何数据。")
	}
	text.Wrapping = fyne.TextWrapWord
	w.SetContent(container.NewBorder(nil, widget.NewButton("完成", a.Quit), nil, nil, text))
	w.Resize(fyne.NewSize(620, 220))
	w.ShowAndRun()
}
func runStage(stage, dir string, automatic bool) {
	if dir == "" {
		return
	}
	if os.Getenv("FORTERM_CPU_PROFILE") == "1" {
		if f, e := os.Create(filepath.Join(dir, stage+"-cpu.pprof")); e == nil {
			pprof.StartCPUProfile(f)
			defer f.Close()
			defer pprof.StopCPUProfile()
		}
	}
	start := time.Now()
	var ui fyne.App
	var w fyne.Window
	var native *desktop.App
	id := fmt.Sprintf("cn.forterm.ui-diagnostic.%s.%d", stage, time.Now().UnixNano())
	if stage == "forterm" {
		native = desktop.NewDiagnostic(id)
		ui = native.UI
		w = native.Window
		defer native.Close()
	} else {
		ui = app.NewWithID(id)
		w = ui.NewWindow("标准Fyne空窗口对照")
		w.SetContent(container.NewCenter(container.NewVBox(widget.NewLabel("标准控件 / 无连接 / 无终端"), widget.NewButton("示例按钮", func() { dialog.ShowInformation("示例", "离线标准控件", w) }), widget.NewEntry())))
	}
	w.SetTitle("Forterm 离线界面诊断 · " + stage)
	w.Resize(fyne.NewSize(1150, 760))
	startup := time.Since(start).Seconds() * 1000
	var clickCount atomic.Int64
	var painting atomic.Bool
	var mu sync.Mutex
	var lastPaint time.Time
	var intervals []float64
	var paints uint64
	var mouseReleased atomic.Int64
	var pendingTap atomic.Int64
	var dispatchTimes, feedbackTimes, swapTimes []float64
	glfw.SetSwapBuffersObserver(func(d time.Duration) {
		mu.Lock()
		swapTimes = append(swapTimes, float64(d)/float64(time.Millisecond))
		mu.Unlock()
	})
	defer glfw.SetSwapBuffersObserver(nil)
	marker := canvas.NewRaster(func(_, _ int) image.Image {
		mu.Lock()
		now := time.Now()
		if !lastPaint.IsZero() {
			intervals = append(intervals, float64(now.Sub(lastPaint))/float64(time.Millisecond))
		}
		lastPaint = now
		paints++
		if clicked := pendingTap.Swap(0); clicked > 0 {
			feedbackTimes = append(feedbackTimes, float64(now.UnixNano()-clicked)/1e6)
		}
		mu.Unlock()
		img := image.NewRGBA(image.Rect(0, 0, 1, 1))
		img.Set(0, 0, color.NRGBA{R: 70, G: 160, B: 130, A: 255})
		return img
	})
	marker.SetMinSize(fyne.NewSize(8, 8))
	status := widget.NewLabel("只采集性能与图形环境；不联网、不访问现有连接或凭据。")
	done := make(chan struct{})
	var once sync.Once
	startButton := widget.NewButton("开始离线诊断（两阶段约45秒，含全屏）", func() { once.Do(func() { close(done) }) })
	ack := widget.NewButton("点击响应测试", func() {
		now := time.Now().UnixNano()
		pendingTap.Store(now)
		if released := mouseReleased.Load(); released > 0 && now-released < 1e9 {
			mu.Lock()
			dispatchTimes = append(dispatchTimes, float64(now-released)/1e6)
			mu.Unlock()
		}
		n := clickCount.Add(1)
		marker.Refresh()
		status.SetText(fmt.Sprintf("已响应 %d 次 · %s", n, time.Now().Format("15:04:05.000")))
	})
	original := w.Content()
	w.SetContent(container.NewBorder(container.NewVBox(container.NewHBox(startButton, ack, marker), status), nil, nil, nil, original))
	report := map[string]any{"stage": stage, "diagnosticFortermSourceVersion": desktop.Version, "physicalPresentationTimingAvailable": false, "platform": runtime.GOOS + "/" + runtime.GOARCH, "startupMS": startup, "noConnections": true, "noTerminals": true, "noTransfers": true, "resourceStart": resources(), "completed": false}
	go func() {
		<-done
		fyne.DoAndWait(func() { trackMouseRelease(w, &mouseReleased) })
		var phases []map[string]any
		for _, full := range []bool{false, true} {
			fyne.DoAndWait(func() { w.SetFullScreen(full); startButton.Disable() })
			time.Sleep(500 * time.Millisecond)
			for _, animate := range []bool{false, true} {
				fyne.DoAndWait(func() {
					status.SetText(fmt.Sprintf("采样中 · 全屏%t · 控制刷新%t · 可点击响应按钮", full, animate))
				})
				mu.Lock()
				intervals = nil
				lastPaint = time.Time{}
				paints = 0
				dispatchTimes = nil
				feedbackTimes = nil
				swapTimes = nil
				mu.Unlock()
				painting.Store(animate)
				before := resources()
				clicked := clickCount.Load()
				var latencies []float64
				seconds := 5
				if n, e := strconv.Atoi(os.Getenv("FORTERM_UI_DIAG_SECONDS")); e == nil && n >= 2 && n <= 10 {
					seconds = n
				}
				end := time.Now().Add(time.Duration(seconds) * time.Second)
				for time.Now().Before(end) {
					posted := time.Now()
					fyne.DoAndWait(func() {
						latencies = append(latencies, float64(time.Since(posted))/float64(time.Millisecond))
						if painting.Load() {
							marker.Refresh()
						}
					})
					time.Sleep(time.Second / 30)
				}
				painting.Store(false)
				phase := map[string]any{"fullscreen": full, "controlledRasterRefresh30Hz": animate, "uiQueueDelay": stats(latencies), "actualResponseButtonCallbacks": clickCount.Load() - clicked, "resourceBefore": before, "resourceAfter": resources()}
				mu.Lock()
				phase["renderCallbackIntervals"] = stats(intervals)
				phase["renderCallbacks"] = paints
				phase["nativeSwapBuffersMS"] = stats(swapTimes)
				phase["nativeReleaseToOnTappedMS"] = stats(dispatchTimes)
				phase["onTappedToRenderCallbackMS"] = stats(feedbackTimes)
				phase["timingScope"] = "native release callback to OnTapped and next raster generation; SwapBuffers native call duration is measured separately; OS queue waiting and physical display presentation timestamps are unavailable"
				mu.Unlock()
				fyne.DoAndWait(func() {
					size := w.Canvas().Size()
					x, y := w.Canvas().PixelCoordinateForPosition(fyne.NewPos(size.Width, size.Height))
					phase["canvasLogicalSize"] = size
					phase["canvasScale"] = w.Canvas().Scale()
					phase["canvasPixelExtent"] = []int{x, y}
					phase["graphics"] = graphics(w)
				})
				phases = append(phases, phase)
				report["phases"] = phases
				if !save(filepath.Join(dir, stage+".json"), report) {
					fyne.Do(func() {
						w.SetFullScreen(false)
						status.SetText("报告写入失败，未上传数据；请使用可写目录")
						ui.Quit()
					})
					return
				}
			}
		}
		report["completed"] = true
		if !save(filepath.Join(dir, stage+".json"), report) {
			report["completed"] = false
			fyne.Do(func() {
				w.SetFullScreen(false)
				status.SetText("最终报告写入失败，未标记采样成功")
				ui.Quit()
			})
			return
		}
		fyne.Do(func() { w.SetFullScreen(false); status.SetText("采样完成"); ui.Quit() })
	}()
	if automatic || os.Getenv("FORTERM_UI_DIAG_AUTOSTART") == "1" {
		go fyne.Do(func() { startButton.OnTapped() })
	}
	w.ShowAndRun()
}
func graphics(w fyne.Window) map[string]any {
	result := map[string]any{"available": false}
	if ctx, ok := w.(interface{ RunWithContext(func()) }); ok {
		ctx.RunWithContext(func() {
			// Fyne already initialized this shared GL2.1 binding for this context.
			result["available"] = true
			for k, v := range map[string]uint32{"vendor": gl.VENDOR, "renderer": gl.RENDERER, "version": gl.VERSION} {
				p := gl.GetString(v)
				if p != nil {
					result[k] = gl.GoStr(p)
				}
			}
			if v := glfw.GetCurrentContext(); v != nil {
				a, b := v.GetFramebufferSize()
				c, d := v.GetSize()
				x, y := v.GetContentScale()
				result["framebufferPixels"] = []int{a, b}
				result["windowSize"] = []int{c, d}
				result["contentScale"] = []float32{x, y}
			}
			if m := glfw.GetPrimaryMonitor(); m != nil {
				if mode := m.GetVideoMode(); mode != nil {
					result["primaryMonitorPixels"] = []int{mode.Width, mode.Height}
					result["refreshHz"] = mode.RefreshRate
				}
			}
		})
	}
	return result
}

func trackMouseRelease(w fyne.Window, stamp *atomic.Int64) {
	if ctx, ok := w.(interface{ RunWithContext(func()) }); ok {
		ctx.RunWithContext(func() {
			if v := glfw.GetCurrentContext(); v != nil {
				var previous glfw.MouseButtonCallback
				previous = v.SetMouseButtonCallback(func(win *glfw.Window, button glfw.MouseButton, action glfw.Action, mods glfw.ModifierKey) {
					if action == glfw.Release {
						stamp.Store(time.Now().UnixNano())
					}
					if previous != nil {
						previous(win, button, action, mods)
					}
				})
			}
		})
	}
}

// A partial report can exist when the user closes a running phase. Never use
// that as consent to open the next full-screen diagnostic window.
func completedStage(value any) bool {
	m, ok := value.(map[string]any)
	if !ok || m["completed"] != true {
		return false
	}
	phases, ok := m["phases"].([]any)
	return ok && len(phases) == 4
}

func decodeStage(raw []byte) (any, bool) {
	var value any
	if json.Unmarshal(raw, &value) != nil {
		return nil, false
	}
	return value, completedStage(value)
}

func stageOutcome(runErr, readErr error, raw []byte) (any, bool) {
	if readErr != nil {
		return nil, false
	}
	value, complete := decodeStage(raw)
	return value, runErr == nil && complete
}
