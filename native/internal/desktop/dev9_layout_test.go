package desktop

import (
	"context"
	"fmt"
	"forterm/native/internal/metrics"
	"forterm/native/internal/transfers"
	"fyne.io/fyne/v2"
	"image/png"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"
)

func TestDev9MonitorQueueFormsSoftwareLayout(t *testing.T) {
	a, _, pump := formFixture(t)
	a.diagnostic = true
	w := a.AddSyntheticWorkspacePreview()
	if w == nil {
		t.Fatal("preview not created")
	}
	pump(func() bool { _, n, _ := w.Term.OutputStats(); return n > 0 })
	w.filesVisible = true
	w.fileWidth = 300
	w.monitorWidth = 300
	w.bodyHost.Refresh()
	var first, second strings.Builder
	first.WriteString("UPTIME 100\ncpu 100 0 50 850 0 0 0 0\n")
	second.WriteString("UPTIME 103\ncpu 150 0 75 875 0 0 0 0\n")
	for i := 0; i < 128; i++ {
		fmt.Fprintf(&first, "cpu%d 100 0 50 850 0 0 0 0\n", i)
		fmt.Fprintf(&second, "cpu%d %d 0 75 %d 0 0 0 0\n", i, 100+i%70, 925-i%70)
	}
	first.WriteString("MemTotal: 1000000 kB\nMemAvailable: 500000 kB\nFilesystem 1024-blocks Used Available Capacity Mounted on\n/dev/test 1000000 300000 700000 30% /\neth0: 100 0 0 0 0 0 0 0 200 0 0 0 0 0 0 0\n")
	second.WriteString("MemTotal: 1000000 kB\nMemAvailable: 400000 kB\nFilesystem 1024-blocks Used Available Capacity Mounted on\n/dev/test 1000000 300000 700000 30% /\neth0: 400 0 0 0 0 0 0 0 800 0 0 0 0 0 0 0\n")
	before := metrics.Parse(first.String(), time.Unix(100, 0))
	after := metrics.Parse(second.String(), time.Unix(103, 0))
	w.Monitor.present(after, metrics.Between(before, after))
	w.Monitor.sourceLabel.SetText("合成128逻辑核双样本")
	w.Monitor.status.SetText("软件测试样本，未连接真实主机")
	w.Transfers = transfers.New(w.Context, func(context.Context, transfers.Spec, func(int64, int64)) error { return nil }, nil)
	w.transferItems = nil
	for i := 0; i < 25; i++ {
		state := transfers.Done
		if i == 1 {
			state = transfers.Running
		}
		if i == 2 {
			state = transfers.Failed
		}
		w.transferItems = append(w.transferItems, transfers.Item{ID: fmt.Sprint(i), Direction: "upload", Local: "长中文文件名称 空格%# " + fmt.Sprint(i) + ".txt", Remote: "/preview/file", State: state, Bytes: 512, Total: 1024, TotalKnown: i != 1, Message: map[bool]string{true: "等待覆盖确认", false: "完成"}[i == 1], Phase: transfers.PhaseOverwrite})
	}
	w.transferSummary.SetText("1运行 · 2排队")
	w.transferFooter.SetText("21完成 · 1失败")
	w.queuePane.Show()
	w.queueExpanded.SetChecked(true)
	w.transferList.Refresh()
	w.refreshFilePaneLayout()
	output := os.Getenv("FORTERM_DEV9_EVIDENCE")
	save := func(name string) {
		t.Helper()
		if output == "" {
			return
		}
		os.MkdirAll(output, 0700)
		f, e := os.Create(filepath.Join(output, name+".png"))
		if e != nil {
			t.Fatal(e)
		}
		e = png.Encode(f, a.Window.Canvas().Capture())
		f.Close()
		if e != nil {
			t.Fatal(e)
		}
	}
	for _, tc := range []struct {
		name, mode string
		size       fyne.Size
		scale      float32
	}{{"workspace-dark", "dark", fyne.NewSize(1440, 900), 1}, {"workspace-light", "light", fyne.NewSize(1440, 900), 1}, {"workspace-small", "dark", fyne.NewSize(1050, 650), 1}, {"workspace-150", "dark", fyne.NewSize(700, 433), 1.5}} {
		a.SetAppearance(tc.mode)
		a.Window.Canvas().(interface{ SetScale(float32) }).SetScale(tc.scale)
		a.Window.Resize(tc.size)
		if tc.scale > 1 && a.navigationVisible {
			a.ToggleNavigation()
		}
		a.Window.Canvas().Capture()
		save(tc.name)
		if w.Monitor.cores.list.Size().Height > 200 {
			t.Fatal("CPU grid lost bounded internal viewport")
		}
		beforeRows := w.Monitor.cores.rowsCreated
		w.Monitor.cores.Set(w.Monitor.cores.values)
		a.Window.Canvas().Capture()
		if w.Monitor.cores.rowsCreated-beforeRows > 16 {
			t.Fatalf("one update allocated too many CPU rows: %d", w.Monitor.cores.rowsCreated-beforeRows)
		}
		for _, row := range w.transferRows {
			if row.info.Size().Height > 29 || row.cancel.Size().Height > 29 {
				t.Fatal("transfer action stretched vertically")
			}
		}
	}
	a.ShowLibrary()
	a.Window.Canvas().(interface{ SetScale(float32) }).SetScale(1)
	a.Window.Resize(fyne.NewSize(1050, 650))
	for _, quick := range []bool{true, false} {
		f := a.newConnectionForm(nil, quick, nil)
		f.show()
		name := "saved-form"
		if quick {
			name = "quick-form"
		}
		save(name)
		f.popup.Hide()
	}
}
