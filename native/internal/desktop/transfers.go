package desktop

import (
	"context"
	"errors"
	"fmt"
	"forterm/native/internal/fileops"
	"forterm/native/internal/localfile"
	"forterm/native/internal/remotefs"
	"forterm/native/internal/sshconn"
	"forterm/native/internal/transfers"
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/container"
	"fyne.io/fyne/v2/dialog"
	"fyne.io/fyne/v2/theme"
	"fyne.io/fyne/v2/widget"
	"os"
	"path"
	"path/filepath"
	"strings"
	"sync"
)

func (w *Workspace) SetConnection(conn *sshconn.Connection) {
	if w.Context.Err() != nil {
		conn.Close()
		return
	}
	if w.Conn != nil && w.Conn != conn {
		conn.Close()
		return
	}
	w.Conn = conn
	w.SetFileConnection(conn)
}
func (w *Workspace) SetFileConnection(conn remotefs.Connection) {
	if w.Context.Err() != nil {
		conn.Close()
		return
	}
	if w.FileConn != nil && w.FileConn != conn {
		conn.Close()
		return
	}
	w.FileConn = conn
	w.App.model.SetOnline(w.ID, true)
	if w.Protocol != "ssh" {
		w.collapseNavigationAfterConnect()
	}
	if w.App.connections != nil {
		w.App.connections.refreshStates()
	}
	w.renderSourceTabs()
	if w.Transfers != nil {
		return
	}

	if fs, ok := conn.(remotefs.Operations); ok {
		w.Operations = fileops.New(fs, w.Protocol == "ftp", w.hasTransferAtPath, w.updateOperationView)
	}
	w.Transfers = transfers.New(w.Context, conn.RunTransfer, w.updateTransferView)
	w.Transfers.SetResources(transfers.ResourceKey(conn))
	if w.Operations != nil {
		w.Operations.SetResourceKey(transfers.ResourceKey(conn))
	}
}

func (w *Workspace) updateTransferView(_ []transfers.Item) {
	if w.Context.Err() != nil || !w.transferUIQueued.CompareAndSwap(false, true) {
		return
	}
	fyne.Do(func() {
		w.transferUIQueued.Store(false)
		if w.Context.Err() != nil {
			return
		}
		items := w.Transfers.Snapshot()
		w.transferItems = items
		queued, running, done, failed := 0, 0, 0, 0
		refresh := false
		firstFailure := ""
		focusIndex := -1
		for index, item := range items {
			previous, known := w.transferStates[item.ID]
			if !known || (item.State == transfers.Running && previous != transfers.Running) {
				focusIndex = index
			}
			switch item.State {
			case transfers.Queued:
				queued++
			case transfers.Running, transfers.Cancelling:
				running++
			case transfers.Failed:
				failed++
				if w.transferStates[item.ID] != transfers.Failed {
					if firstFailure == "" {
						firstFailure = item.Message
						focusIndex = index
					}
					if w.queueExpanded != nil {
						w.queueExpanded.SetChecked(true)
					}
				}
			case transfers.Done:
				done++
				if w.transferStates[item.ID] != transfers.Done && item.Direction == "upload" {
					refresh = true
				}
			}
			w.transferStates[item.ID] = item.State
		}
		if firstFailure != "" {
			w.Status.SetText("传输失败：" + firstFailure)
		}
		w.transferSummary.SetText(fmt.Sprintf("%d运行 · %d排队", running, queued))
		w.transferFooter.SetText(fmt.Sprintf("%d完成 · %d失败", done, failed))
		byID := map[string]transfers.Item{}
		for _, item := range items {
			byID[item.ID] = item
		}
		for _, row := range w.transferRows {
			if item, ok := byID[row.item.ID]; ok {
				row.bind(item)
			}
		}
		if !w.queuePane.Visible() {
			w.queuePane.Show()
			w.refreshFilePaneLayout()
		}
		if w.filesVisible {
			w.transferList.Refresh()
			if focusIndex >= 0 {
				w.transferList.ScrollTo(focusIndex)
			}
		}
		if refresh {
			w.Refresh()
		}
	})
}

func (w *Workspace) initTransferPanel() fyne.CanvasObject {
	w.transferStates = map[string]transfers.State{}
	w.transferSummary = widget.NewLabel("0运行 · 0排队")
	w.transferSummary.Truncation = fyne.TextTruncateEllipsis
	w.transferFooter = widget.NewLabel("0完成 · 0失败")
	w.transferFooter.Importance = widget.LowImportance
	w.transferList = widget.NewList(func() int { return len(w.transferItems) }, func() fyne.CanvasObject {
		row := newTransferRow(w)
		w.transferRows = append(w.transferRows, row)
		return row
	}, func(id widget.ListItemID, obj fyne.CanvasObject) {
		if id >= 0 && id < len(w.transferItems) {
			obj.(*transferRow).bind(w.transferItems[id])
		}
	})
	area := container.New(&queueViewportLayout{window: w.Window}, w.transferList)
	area.Hide()
	var toggle *widget.Button
	w.queueExpanded = widget.NewCheck("展开队列", func(open bool) {
		if toggle != nil {
			if open {
				toggle.SetIcon(theme.MoveDownIcon())
			} else {
				toggle.SetIcon(theme.NavigateNextIcon())
			}
		}
		if open {
			area.Show()
		} else {
			area.Hide()
			w.stopTransferAnimations()
		}
		w.refreshFilePaneLayout()
	})
	w.queueExpanded.Hide()
	toggle = action("传输", theme.NavigateNextIcon(), func() { w.queueExpanded.SetChecked(!w.queueExpanded.Checked) })
	var more *widget.Button
	more = action("", namedActionIcon("传输队列操作", theme.MoreHorizontalIcon()), func() {
		menu := fyne.NewMenu("传输", fyne.NewMenuItem("清理已结束任务", func() {
			if w.Transfers != nil {
				w.Transfers.ClearFinished()
			}
		}))
		widget.ShowPopUpMenuAtPosition(menu, w.Window.Canvas(), w.App.UI.Driver().AbsolutePositionForObject(more).Add(fyne.NewPos(0, more.Size().Height)))
	})
	heading := container.NewBorder(nil, nil, toggle, more, w.transferSummary)
	w.queuePane = container.NewVBox(heading, w.transferFooter, area)
	w.queuePane.Hide()
	return w.queuePane
}
func (w *Workspace) stopTransferAnimations() {
	for _, row := range w.transferRows {
		row.stopAnimation()
	}
}
func (w *Workspace) refreshFilePaneLayout() {
	if w.filePane != nil {
		w.filePane.Refresh()
	}
}

type queueViewportLayout struct{ window fyne.Window }

func (queueViewportLayout) MinSize([]fyne.CanvasObject) fyne.Size { return fyne.NewSize(1, 176) }
func (queueViewportLayout) Layout(objects []fyne.CanvasObject, size fyne.Size) {
	for _, obj := range objects {
		obj.Move(fyne.NewPos(0, 0))
		obj.Resize(fyne.NewSize(max(0, size.Width), max(0, size.Height)))
	}
}
func formatBytes(n int64) string {
	if n < 1024 {
		return fmt.Sprintf("%d B", n)
	}
	if n < 1024*1024 {
		return fmt.Sprintf("%.1f KiB", float64(n)/1024)
	}
	if n < 1024*1024*1024 {
		return fmt.Sprintf("%.1f MiB", float64(n)/(1024*1024))
	}
	return fmt.Sprintf("%.2f GiB", float64(n)/(1024*1024*1024))
}
func (w *Workspace) transferBefore(ctx context.Context, spec transfers.Spec) (transfers.Spec, bool, error) {
	exists := false
	var version localfile.TargetVersion
	if spec.Direction == "upload" {
		transfers.ReportPhase(ctx, transfers.PhaseTargetStat)
		info, err := w.FileConn.Stat(ctx, spec.Remote)
		if err == nil {
			if !info.Mode().IsRegular() {
				return spec, false, errors.New("远程目标不是普通文件，未覆盖")
			}
			exists = true
		} else if !os.IsNotExist(err) {
			return spec, false, fmt.Errorf("上传前核对失败（STAT）；未打开写入流：%w", err)
		}
	} else {
		var inspectErr error
		version, inspectErr = localfile.CaptureTarget(spec.Local)
		if inspectErr != nil {
			return spec, false, inspectErr
		}
		info, err := os.Lstat(spec.Local)
		if err == nil {
			if !info.Mode().IsRegular() {
				return spec, false, errors.New("本地目标不是普通文件，未覆盖")
			}
			exists = true
		} else if !os.IsNotExist(err) {
			return spec, false, errors.New("无法核对本地目标，未打开写入流")
		}
	}
	if !exists {
		return spec, true, nil
	}
	transfers.ReportPhase(ctx, transfers.PhaseOverwrite)
	answer := make(chan bool, 1)
	fyne.Do(func() {
		if ctx.Err() != nil || w.Context.Err() != nil {
			answer <- false
			return
		}
		target := spec.Remote
		if spec.Direction == "download" {
			target = spec.Local
		}
		answered := make(chan struct{})
		var once sync.Once
		prompt := dialog.NewConfirm("确认覆盖文件？", target+"\n仅确认后开始此项；跳过不会写入此项。", func(ok bool) { once.Do(func() { answer <- ok; close(answered) }) }, w.Window)
		prompt.SetConfirmText("覆盖")
		prompt.SetDismissText("跳过")
		prompt.Show()
		go func() {
			select {
			case <-ctx.Done():
				fyne.Do(prompt.Hide)
			case <-w.Context.Done():
				fyne.Do(prompt.Hide)
			case <-answered:
			}
		}()
	})
	select {
	case <-ctx.Done():
		return spec, false, ctx.Err()
	case <-w.Context.Done():
		// A borrowed view can close while its source transport and queue stay
		// alive. An unconfirmed overwrite must not wait on the vanished view.
		return spec, false, nil
	case ok := <-answer:
		spec.Overwrite = ok
		if ok && spec.Direction == "download" {
			spec.CommitGuard = version.Check
		}
		return spec, ok, nil
	}
}
func (w *Workspace) EnqueueUploads(locals []string, directory string) {
	w.uploadTrace.record(uploadEnqueue)
	if !w.fileAvailable() || w.Transfers == nil {
		w.uploadError("文件连接尚未就绪或已断开；没有加入传输队列，也不会自动重登")
		return
	}
	if !absoluteDirectory(directory) {
		w.uploadError("上传目标不是已确认的绝对目录；请先读取目录")
		return
	}
	added := 0
	for _, local := range locals {
		name := filepath.Base(local)
		if name == "." || name == ".." || strings.ContainsAny(name, "/\\\x00\r\n") {
			continue
		}
		id := w.Transfers.Add(transfers.Spec{Direction: "upload", Local: local, Remote: path.Join(directory, name)}, w.transferBefore)
		if id == "" {
			w.uploadError("传输队列已经关闭；本次文件未入队，不会自动重登或重传")
			return
		}
		added++
	}
	if added > 0 {
		w.uploadTrace.record(uploadQueued)
		if w.queueExpanded != nil {
			w.queueExpanded.SetChecked(true)
		}
		w.Status.SetText(fmt.Sprintf("已加入%d项上传 · %s · %s", added, w.sourceTitle(), directory))
	} else {
		w.uploadError("没有有效的本地文件可加入队列")
	}
}
func (w *Workspace) EnqueueDownload(remote, local string) {
	if w.Transfers == nil || w.FileConn == nil || w.FileConn.Closed() {
		return
	}
	w.Transfers.Add(transfers.Spec{Direction: "download", Local: local, Remote: remote}, w.transferBefore)
}

func (w *Workspace) ActiveTransfers() int {
	if w.Transfers == nil {
		return 0
	}
	count := 0
	for _, item := range w.Transfers.Snapshot() {
		if item.State == transfers.Running || item.State == transfers.Queued || item.State == transfers.Cancelling {
			count++
		}
	}
	return count
}

func (w *Workspace) hasTransferAtPath(remote string) bool {
	if w.Transfers == nil {
		return false
	}
	target := path.Clean(remote)
	for _, item := range w.Transfers.Snapshot() {
		if item.State != transfers.Running && item.State != transfers.Queued && item.State != transfers.Cancelling {
			continue
		}
		p := path.Clean(item.Remote)
		if p == target || strings.HasPrefix(p, target+"/") || strings.HasPrefix(target, p+"/") {
			return true
		}
	}
	return false
}

func (w *Workspace) updateOperationView(state fileops.Status) {
	if w.Context.Err() != nil {
		return
	}
	fyne.Do(func() {
		if w.Context.Err() != nil {
			return
		}
		w.operationStatus.SetText(state.Message)
		if state.Running {
			w.operationCancel.Show()
		} else {
			w.operationCancel.Hide()
		}
	})
}

func (w *Workspace) uploadError(message string) {
	w.uploadTrace.record(uploadRejected)
	w.Status.SetText(message)
	dialog.ShowInformation("未开始上传", message, w.Window)
}
func (w *Workspace) uploadDirectory() (string, bool) {
	if !w.fileAvailable() || w.Transfers == nil {
		w.uploadError("文件连接尚未就绪或已断开；不会自动重新登录")
		return "", false
	}
	if w.busy || w.refreshPending || !w.directoryReady || !absoluteDirectory(w.directory) || w.Path.Text != w.directory {
		w.uploadError("请先成功读取目标目录，再选择上传文件。当前输入的路径尚未确认；不会写入旧目录或未知目录。若服务器禁用REALPATH，可在更多菜单明确选择按绝对目录直接读取。" + w.directoryError)
		return "", false
	}
	return w.directory, true
}
