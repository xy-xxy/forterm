package desktop

import (
	"fmt"
	"forterm/native/internal/transfers"
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/canvas"
	"fyne.io/fyne/v2/container"
	"fyne.io/fyne/v2/dialog"
	"fyne.io/fyne/v2/theme"
	"fyne.io/fyne/v2/widget"
	"path"
	"path/filepath"
	"strings"
	"time"
)

type transferRow struct {
	widget.BaseWidget
	workspace           *Workspace
	item                transfers.Item
	name, state, detail *widget.Label
	icon                *widget.Icon
	info, cancel        *widget.Button
	track, fill, line   *canvas.Rectangle
	animation           *fyne.Animation
	offset              float32
}

func newTransferRow(w *Workspace) *transferRow {
	r := &transferRow{workspace: w, name: widget.NewLabel(""), state: widget.NewLabel(""), detail: widget.NewLabel(""), icon: widget.NewIcon(theme.DocumentIcon()), track: canvas.NewRectangle(theme.Color(theme.ColorNameSeparator)), fill: canvas.NewRectangle(theme.Color(theme.ColorNamePrimary)), line: canvas.NewRectangle(theme.Color(theme.ColorNameSeparator))}
	r.name.Truncation = fyne.TextTruncateEllipsis
	r.state.Truncation = fyne.TextTruncateEllipsis
	r.detail.Truncation = fyne.TextTruncateEllipsis
	r.detail.Importance = widget.LowImportance
	r.detail.SizeName = theme.SizeNameCaptionText
	r.info = action("", namedActionIcon("查看传输完整详情", theme.InfoIcon()), func() {
		label := widget.NewLabel(r.item.Message + "\n名称：" + r.fileName() + "\n远程：" + r.item.Remote + "\n本地/来源：" + r.item.Local)
		label.Wrapping = fyne.TextWrapWord
		popup := dialog.NewCustom("传输详情", "关闭", container.NewVScroll(label), w.Window)
		fitPopup(popup, w.Window, fyne.NewSize(780, 450))
		popup.Show()
	})
	r.cancel = action("", namedActionIcon("取消此传输", theme.CancelIcon()), func() {
		if w.Transfers != nil {
			w.Transfers.Cancel(r.item.ID)
		}
	})
	r.ExtendBaseWidget(r)
	return r
}
func (r *transferRow) fileName() string {
	if r.item.Direction == "download" {
		return path.Base(r.item.Remote)
	}
	return filepath.Base(r.item.Local)
}
func (r *transferRow) bind(item transfers.Item) {
	r.item = item
	r.name.SetText(r.fileName())
	state := map[transfers.State]string{transfers.Queued: "排队", transfers.Running: "运行", transfers.Cancelling: "取消中", transfers.Done: "完成 ✓", transfers.Failed: "失败 !", transfers.Cancelled: "已取消"}[item.State]
	if item.State == transfers.Running {
		if item.Phase == transfers.PhaseOverwrite {
			state = "待确认"
		} else if item.Phase == transfers.PhaseVerify {
			state = "校验"
		}
	}
	r.state.SetText(state)
	if item.State == transfers.Queued || item.State == transfers.Running {
		r.cancel.Enable()
	} else {
		r.cancel.Disable()
	}
	description := item.Message
	if item.State == transfers.Running && item.Message == "" || item.State == transfers.Done {
		description = formatBytes(item.Bytes)
		if item.TotalKnown {
			description += " / " + formatBytes(item.Total)
		} else {
			description += " · 总量待确认"
		}
		if item.Direction == "permissions" {
			description = fmt.Sprintf("%.0f / %.0f 项", float64(item.Bytes), float64(item.Total))
		}
		if item.State == transfers.Running && item.Direction != "permissions" && item.BytesPerSecond > 0 {
			description += " · " + formatBytes(int64(item.BytesPerSecond)) + "/s"
			if item.ETAKnown {
				description += " · 约" + time.Duration(item.ETASeconds*float64(time.Second)).Round(time.Second).String()
			}
		}
	}
	if item.State == transfers.Failed {
		description = "失败：" + item.Message
	}
	description = strings.ReplaceAll(description, "\n", " ")
	r.detail.SetText(description)
	if item.State == transfers.Failed {
		r.icon.SetResource(theme.WarningIcon())
	} else if item.State == transfers.Done {
		r.icon.SetResource(theme.ConfirmIcon())
	} else {
		r.icon.SetResource(theme.DocumentIcon())
	}
	unknown := item.State == transfers.Running && !item.TotalKnown && r.workspace.filesVisible && r.workspace.queueExpanded != nil && r.workspace.queueExpanded.Checked
	if unknown && r.animation == nil {
		r.animation = fyne.NewAnimation(time.Second, func(value float32) { r.offset = value; r.Refresh() })
		r.animation.RepeatCount = fyne.AnimationRepeatForever
		r.animation.Start()
	} else if !unknown {
		r.stopAnimation()
	}
	r.Refresh()
}
func (r *transferRow) stopAnimation() {
	if r.animation != nil {
		r.animation.Stop()
		r.animation = nil
	}
}
func (r *transferRow) CreateRenderer() fyne.WidgetRenderer { return &transferRowRenderer{r: r} }

type transferRowRenderer struct{ r *transferRow }

func (r *transferRowRenderer) MinSize() fyne.Size { return fyne.NewSize(1, 76) }
func (r *transferRowRenderer) Layout(s fyne.Size) {
	v := r.r
	v.icon.Move(fyne.NewPos(2, 9))
	v.icon.Resize(fyne.NewSize(16, 16))
	v.info.Move(fyne.NewPos(max(0, s.Width-58), 3))
	v.info.Resize(fyne.NewSize(26, 26))
	v.cancel.Move(fyne.NewPos(max(0, s.Width-29), 3))
	v.cancel.Resize(fyne.NewSize(26, 26))
	stateWidth := float32(52)
	v.state.Move(fyne.NewPos(max(20, s.Width-62-stateWidth), 2))
	v.state.Resize(fyne.NewSize(stateWidth, 29))
	v.name.Move(fyne.NewPos(22, 2))
	v.name.Resize(fyne.NewSize(max(1, s.Width-84-stateWidth), 29))
	v.detail.Move(fyne.NewPos(4, 34))
	v.detail.Resize(fyne.NewSize(max(0, s.Width-8), 23))
	v.track.Move(fyne.NewPos(4, 63))
	v.track.Resize(fyne.NewSize(max(0, s.Width-8), 3))
	fraction := float32(0)
	if v.item.TotalKnown {
		if v.item.Total > 0 {
			fraction = float32(min(1, float64(v.item.Bytes)/float64(v.item.Total)))
		} else if v.item.State == transfers.Done {
			fraction = 1
		}
	} else if v.item.State == transfers.Running {
		fraction = .2
	}
	width := max(0, s.Width-8) * fraction
	x := float32(4)
	if !v.item.TotalKnown {
		x += max(0, s.Width-8-width) * v.offset
	}
	v.fill.Move(fyne.NewPos(x, 63))
	v.fill.Resize(fyne.NewSize(width, 3))
	v.line.Move(fyne.NewPos(0, s.Height-1))
	v.line.Resize(fyne.NewSize(s.Width, 1))
}
func (r *transferRowRenderer) Refresh() {
	r.r.track.FillColor = theme.Color(theme.ColorNameSeparator)
	r.r.fill.FillColor = theme.Color(theme.ColorNamePrimary)
	r.r.line.FillColor = theme.Color(theme.ColorNameSeparator)
	r.r.track.Refresh()
	r.r.fill.Refresh()
	r.r.line.Refresh()
	r.Layout(r.r.Size())
}
func (r *transferRowRenderer) Objects() []fyne.CanvasObject {
	v := r.r
	return []fyne.CanvasObject{v.icon, v.name, v.state, v.info, v.cancel, v.detail, v.track, v.fill, v.line}
}
func (r *transferRowRenderer) Destroy() { r.r.stopAnimation() }
