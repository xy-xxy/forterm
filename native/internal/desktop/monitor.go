package desktop

import (
	"context"
	"fmt"
	"forterm/native/internal/metrics"
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/container"
	"fyne.io/fyne/v2/layout"
	"fyne.io/fyne/v2/theme"
	"fyne.io/fyne/v2/widget"
	"strconv"
	"strings"
	"time"
)

type MonitorPanel struct {
	trendOrigin                                                                                  time.Time
	Content                                                                                      fyne.CanvasObject
	workspace, sampledSource                                                                     *Workspace
	sourceLabel, output, status, cpuValue, cpuHint, memValue, diskValue, networkValue, loadValue *widget.Label
	auto                                                                                         *widget.Check
	interval, method                                                                             *widget.Select
	cpuRing, memRing, diskRing                                                                   *usageRing
	cores                                                                                        *cpuGrid
	trend                                                                                        *networkTrend
	busy, visible, hasPrevious                                                                   bool
	previous                                                                                     metrics.Snapshot
	intervalSeconds                                                                              int
	off                                                                                          bool
	timerCancel, readCancel                                                                      context.CancelFunc
	request                                                                                      uint64
}

func NewMonitorPanel(w *Workspace) *MonitorPanel {
	seconds := w.App.UI.Preferences().IntWithFallback("monitorIntervalSeconds", 3)
	if seconds < 1 || seconds > 300 {
		seconds = 3
	}
	p := &MonitorPanel{workspace: w, intervalSeconds: seconds}
	p.sourceLabel = widget.NewLabel(w.Title + " · 系统未获取")
	p.sourceLabel.Truncation = fyne.TextTruncateEllipsis
	p.sourceLabel.Importance = widget.LowImportance
	p.status = widget.NewLabel("手动刷新以读取指标")
	p.status.Wrapping = fyne.TextWrapWord
	p.status.Importance = widget.LowImportance
	p.output = widget.NewLabel("")
	p.output.TextStyle = fyne.TextStyle{Monospace: true}
	p.cpuValue = widget.NewLabel("—")
	p.cpuHint = widget.NewLabel("等待两次有效采样")
	p.cpuHint.Wrapping = fyne.TextWrapWord
	p.cpuHint.Importance = widget.LowImportance
	p.memValue = widget.NewLabel("—")
	p.memValue.Wrapping = fyne.TextWrapWord
	p.diskValue = widget.NewLabel("—")
	p.diskValue.Wrapping = fyne.TextWrapWord
	p.networkValue = widget.NewLabel("接收 — / 发送 —")
	p.networkValue.Wrapping = fyne.TextWrapWord
	p.loadValue = widget.NewLabel("1分钟 —   5分钟 —   15分钟 —")
	p.loadValue.Wrapping = fyne.TextWrapWord
	p.cpuRing = newUsageRing()
	p.memRing = newUsageRing()
	p.diskRing = newUsageRing()
	p.cores = newCPUGrid()
	p.trend = newNetworkTrend()
	p.method = widget.NewSelect([]string{"当前SSH（独立通道）", "关闭监控"}, nil)
	p.method.SetSelectedIndex(0)
	explanation := widget.NewLabel("同一SSH，独立exec查询；不另登录、不向终端输入。")
	explanation.Wrapping = fyne.TextWrapWord
	explanation.Importance = widget.LowImportance
	p.method.OnChanged = func(value string) {
		p.off = value == "关闭监控"
		if p.off {
			p.pause()
			p.status.SetText("监控已关闭；终端和文件保持原状")
		} else {
			p.hasPrevious = false
			p.schedule()
			p.status.SetText("当前SSH独立查询通道；可手动刷新或开启自动")
		}
	}
	options := []string{"3秒", "5秒", "10秒", "30秒", "60秒"}
	current := fmt.Sprintf("%d秒", seconds)
	found := false
	for _, o := range options {
		if o == current {
			found = true
		}
	}
	if !found {
		options = append(options, current)
	}
	p.interval = widget.NewSelect(options, nil)
	p.interval.SetSelected(current)
	p.auto = widget.NewCheck("自动刷新", func(bool) { p.schedule() })
	p.interval.OnChanged = func(value string) {
		n, err := strconv.Atoi(strings.TrimSuffix(value, "秒"))
		if err != nil || n < 1 || n > 300 {
			return
		}
		p.intervalSeconds = n
		w.App.UI.Preferences().SetInt("monitorIntervalSeconds", n)
		p.auto.SetText("自动刷新")
		p.schedule()
	}
	heading := container.NewHBox(monitorHeading("资源监控", "load"), layout.NewSpacer(), action("", namedActionIcon("刷新资源指标", theme.ViewRefreshIcon()), p.Read), action("", namedActionIcon("收起资源监控", theme.CancelIcon()), func() { w.MonitorToggle.SetChecked(false) }))
	rawItem := widget.NewAccordionItem("原始采样", container.NewScroll(p.output))

	cpuArea := container.New(&monitorViewport{height: 170}, p.cores)
	body := container.New(fitColumn{}, p.sourceLabel, p.method, explanation, container.NewHBox(p.auto, p.interval), p.status, widget.NewSeparator(), monitorHeading("CPU · 总览/逻辑核心", "cpu"), container.NewHBox(p.cpuRing, p.cpuValue), p.cpuHint, cpuArea, widget.NewLabel("iowait按空闲计，guest不重复计数"), widget.NewSeparator(), monitorHeading("内存", "memory"), container.NewBorder(nil, nil, p.memRing, nil, p.memValue), widget.NewSeparator(), monitorHeading("根分区", "disk"), container.NewBorder(nil, nil, p.diskRing, nil, p.diskValue), widget.NewSeparator(), container.NewHBox(widget.NewIcon(theme.DownloadIcon()), widget.NewLabel("接收"), widget.NewIcon(theme.UploadIcon()), widget.NewLabel("发送")), p.networkValue, container.New(&monitorViewport{height: 70}, p.trend), widget.NewLabel("仅显示最近60个真实采样，缺失处断开"), widget.NewSeparator(), monitorHeading("负载（不是CPU百分比）", "load"), p.loadValue, widget.NewSeparator(), monitorHeading("原始采样", "code"), widget.NewAccordion(rawItem))
	p.Content = paneSurface(container.NewPadded(container.NewBorder(heading, nil, nil, nil, container.NewVScroll(body))), theme.ColorNameBackground)
	go func() { <-w.Context.Done(); fyne.Do(p.pause) }()
	return p
}

type monitorViewport struct{ height float32 }

func (l *monitorViewport) MinSize([]fyne.CanvasObject) fyne.Size { return fyne.NewSize(1, l.height) }
func (l *monitorViewport) Layout(objects []fyne.CanvasObject, s fyne.Size) {
	for _, o := range objects {
		o.Move(fyne.NewPos(0, 0))
		o.Resize(s)
	}
}
func (p *MonitorPanel) pause() {
	if p.timerCancel != nil {
		p.timerCancel()
		p.timerCancel = nil
	}
	if p.readCancel != nil {
		p.readCancel()
	}
	p.hasPrevious = false
}
func (p *MonitorPanel) schedule() {
	if p.timerCancel != nil {
		p.timerCancel()
		p.timerCancel = nil
	}
	if !p.visible || p.off || !p.auto.Checked || p.workspace.Context.Err() != nil {
		return
	}
	ctx, cancel := context.WithCancel(p.workspace.Context)
	p.timerCancel = cancel
	interval := time.Duration(p.intervalSeconds) * time.Second
	go func() {
		timer := time.NewTicker(interval)
		defer timer.Stop()
		for {
			select {
			case <-ctx.Done():
				return
			case <-timer.C:
				fyne.Do(func() {
					if ctx.Err() == nil && p.visible && !p.off && p.auto.Checked {
						p.Read()
					}
				})
			}
		}
	}()
}
func (p *MonitorPanel) SetVisible(v bool) {
	if p.visible == v {
		return
	}
	p.visible = v
	if !v {
		p.pause()
		p.status.SetText("已暂停（面板隐藏）")
		return
	}
	p.hasPrevious = false
	p.status.SetText("已恢复；刷新后重新建立双采样，旧值仅为上次样本")
	p.schedule()
	if p.auto.Checked && !p.off {
		p.Read()
	}
}
func (p *MonitorPanel) source() *Workspace {
	w := p.workspace
	if w.focusedTerminal != nil && w.focusedTerminal.Conn != nil {
		return w.focusedTerminal
	}
	return w
}
func (p *MonitorPanel) Read() {
	w := p.workspace
	if p.busy || !p.visible || p.off || w.Context.Err() != nil {
		return
	}
	source := p.source()
	if source.Conn == nil || source.Conn.Closed() {
		p.status.SetText("当前来源没有已认证SSH；此方式不适用于本地终端或独立FTP。")
		return
	}
	if p.sampledSource != source {
		p.hasPrevious = false
		p.sampledSource = source
		p.trend.Clear()
		p.sourceLabel.SetText(source.Title + " · /proc 采样")
	}
	p.busy = true
	p.request++
	request := p.request
	ctx, cancel := context.WithTimeout(w.Context, 8*time.Second)
	p.readCancel = cancel
	p.status.SetText("通过当前SSH的独立exec通道读取…")
	go func() {
		defer cancel()
		text, err := source.Conn.Monitor(ctx)
		at := time.Now()
		fyne.Do(func() {
			p.busy = false
			p.readCancel = nil
			if w.Context.Err() != nil {
				return
			}
			if !p.visible || p.off || request != p.request || p.source() != source {
				p.hasPrevious = false
				return
			}
			if err != nil {
				p.status.SetText("当前查询方式未完成：" + err.Error() + "；不切换通道、不重新登录。")
				if !strings.Contains(err.Error(), "仍在进行") {
					p.auto.SetChecked(false)
				}
				return
			}
			p.output.SetText(text)
			sample := metrics.Parse(text, at)
			delta := metrics.Between(p.previous, sample)
			if !p.hasPrevious {
				delta = metrics.Between(metrics.Snapshot{At: at}, sample)
				delta.CPUReason = "等待第二次有效CPU采样"
			}
			p.present(sample, delta)
			p.previous = sample
			p.hasPrevious = true
			p.status.SetText("已更新 · " + at.Format("15:04:05") + " · 同一SSH连接")
		})
	}()
}
func (p *MonitorPanel) present(sample metrics.Snapshot, delta metrics.Delta) {
	p.cpuRing.Set(delta.CPU/100, delta.CPUValid)
	if delta.CPUValid {
		p.cpuValue.SetText(fmt.Sprintf("总CPU %.1f%%", delta.CPU))
	} else {
		p.cpuValue.SetText("总CPU 未获取")
	}
	p.cpuHint.SetText(delta.CPUReason)
	if len(delta.Cores) == 0 {
		p.cpuHint.SetText("服务端未提供逐核计数；不推测核心数")
	}
	p.cores.Set(delta.Cores)
	p.memRing.Set(0, false)
	p.memValue.SetText("未获取")
	if sample.MemValid {
		used := sample.MemTotal - sample.MemAvailable
		p.memRing.Set(float64(used)/float64(sample.MemTotal), true)
		p.memValue.SetText(fmt.Sprintf("%s / %s\n%d / %d 字节", formatBytes(int64(used)), formatBytes(int64(sample.MemTotal)), used, sample.MemTotal))
	}
	p.diskRing.Set(sample.DiskPercent/100, sample.DiskValid)
	p.diskValue.SetText("未获取")
	if sample.DiskValid {
		value := sample.DiskUsed + " / " + sample.DiskTotal
		if sample.DiskBytesValid {
			value = fmt.Sprintf("%s / %s\n%d / %d 字节", formatBytes(int64(sample.DiskUsedBytes)), formatBytes(int64(sample.DiskTotalBytes)), sample.DiskUsedBytes, sample.DiskTotalBytes)
		}
		p.diskValue.SetText(value)
	}
	if len(p.trend.points) == 0 {
		p.trendOrigin = sample.At
	}
	at := sample.At.Sub(p.trendOrigin).Seconds()
	if delta.NetworkValid {
		p.networkValue.SetText("接收 ↓ " + formatBytes(int64(delta.RX)) + "/s\n发送 ↑ " + formatBytes(int64(delta.TX)) + "/s")
	} else {
		p.networkValue.SetText("接收 — / 发送 —\n等待两次有效网络采样")
	}
	p.trend.Add(netPoint{at: at, rx: delta.RX, tx: delta.TX, valid: delta.NetworkValid})
	values := []string{"1分钟 —", "5分钟 —", "15分钟 —"}
	for i, name := range []string{"1分钟", "5分钟", "15分钟"} {
		if sample.Load[i] != "" {
			values[i] = name + " " + sample.Load[i]
		}
	}
	p.loadValue.SetText(strings.Join(values, "\n"))
}
func (p *MonitorPanel) SetAutomatic(v bool) { p.auto.SetChecked(v) }
func (p *MonitorPanel) Text() string        { return p.output.Text }
