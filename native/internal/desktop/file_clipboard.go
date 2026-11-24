package desktop

import (
	"context"
	"fmt"
	"forterm/native/internal/fileops"
	"forterm/native/internal/remotefs"
	"forterm/native/internal/transfers"
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/container"
	"fyne.io/fyne/v2/dialog"
	"fyne.io/fyne/v2/widget"
	"os"
	"path"
)

type fileClipboard struct {
	SourceID, SourceTitle, Path string
	Connection                  remotefs.Streams
	Context                     context.Context
	Identity                    fileops.Identity
	Move                        bool
}

func (w *Workspace) CopySelectedFile(move bool) { w.captureFileClipboard(move, nil) }
func (w *Workspace) captureFileClipboard(move bool, ready func(*fileClipboard)) {
	entry, remote, ok := w.selectedRemoteEntry()
	if !ok || !w.fileAvailable() {
		return
	}
	if entry.Directory || entry.Mode&os.ModeSymlink != 0 {
		dialog.ShowInformation("仅支持普通文件", "当前复制和移动不递归处理目录或链接。", w.Window)
		return
	}
	conn, ok := w.FileConn.(remotefs.Streams)
	if !ok {
		w.Status.SetText("此文件协议暂不支持受控流式复制")
		return
	}
	source := w.App.session(w.ID)
	if source == nil {
		source = w
	}
	w.App.fileClipboardEpoch++
	epoch := w.App.fileClipboardEpoch
	sourceID, title, ctx := source.ID, source.Title, source.Context
	go func() {
		if err := fileops.CheckParents(ctx, conn, remote); err != nil {
			fyne.Do(func() {
				if w.Context.Err() == nil {
					w.Status.SetText(err.Error())
				}
			})
			return
		}
		info, err := conn.Stat(ctx, remote)
		fyne.Do(func() {
			if w.Context.Err() != nil || ctx.Err() != nil || epoch != w.App.fileClipboardEpoch {
				return
			}
			if err != nil || info == nil || !info.Mode().IsRegular() {
				w.Status.SetText("无法确认普通源文件，未复制到内部剪贴板")
				return
			}
			w.App.fileClipboard = &fileClipboard{SourceID: sourceID, SourceTitle: title, Path: remote, Connection: conn, Context: ctx, Identity: fileops.IdentityOf(info), Move: move}
			kind := "已复制文件引用"
			if move {
				kind = "已剪切文件引用"
			}
			w.Status.SetText(kind + " · " + title + " · " + remote + "；请选择目标目录后粘贴")
			if ready != nil {
				ready(w.App.fileClipboard)
			}
		})
	}()
}
func (w *Workspace) PasteFile() { w.pasteClipboard(w.App.fileClipboard, "") }
func (w *Workspace) pasteClipboard(clip *fileClipboard, explicitDirectory string) {
	if clip == nil {
		dialog.ShowInformation("没有待粘贴文件", "请先在文件树选择普通文件，使用复制或剪切。", w.Window)
		return
	}
	if clip.Context.Err() != nil || clip.Connection.Closed() {
		w.Status.SetText("原文件来源已关闭；不会转用其他连接或重新登录")
		return
	}
	if !w.fileAvailable() || w.Transfers == nil {
		w.Status.SetText("目标文件连接未就绪")
		return
	}
	destination := w.directory
	if explicitDirectory != "" {
		destination = explicitDirectory
	}
	if entry, remote, ok := w.selectedRemoteEntry(); explicitDirectory == "" && ok && entry.Directory && entry.Mode&os.ModeSymlink == 0 {
		destination = remote
	}
	if !absoluteDirectory(destination) {
		w.Status.SetText("目标尚未确认是安全绝对目录")
		return
	}
	dest, ok := w.FileConn.(remotefs.Streams)
	if !ok {
		w.Status.SetText("目标协议不支持受控流式复制")
		return
	}
	if clip.Move && w.App.hasDirtyFileBinding(clip) {
		dialog.ShowInformation("文件有未保存编辑", "请先保存或关闭相关草稿。不会移动后静默改绑或删除源。", w.Window)
		return
	}
	name := widget.NewEntry()
	name.SetText(path.Base(clip.Path))
	if !clip.Move && transfers.ResourceKey(clip.Connection) == transfers.ResourceKey(dest) && path.Dir(clip.Path) == destination {
		name.SetText(path.Base(clip.Path) + " (副本)")
	}
	resolution := widget.NewSelect([]string{"跳过同名目标", "重命名（修改上方名称）", "覆盖（仅协议支持安全替换时）"}, nil)
	resolution.SetSelectedIndex(0)
	info := widget.NewLabel("来源：" + clip.SourceTitle + " · " + clip.Path + "\n目标：" + w.sourceTitle() + " · " + destination)
	info.Wrapping = fyne.TextWrapWord
	note := "复制仅文件内容，不复制属主或特殊权限。"
	if clip.Move {
		note = "移动：同源优先rename；跨来源写入关闭/校验且源未变化后才删除。跨来源不是原子操作；相关已打开标签保持原路径并转为只读。"
	}
	warning := widget.NewLabel(note)
	warning.Wrapping = fyne.TextWrapWord
	popup := dialog.NewCustomConfirm("确认文件目标与同名策略", "加入队列", "取消", container.NewVBox(info, widget.NewForm(widget.NewFormItem("目标名称", name), widget.NewFormItem("同名处理", resolution)), warning), func(ok bool) {
		if !ok || w.Context.Err() != nil {
			return
		}
		if err := fileops.ChildName(name.Text); err != nil {
			w.Status.SetText(err.Error())
			return
		}
		target := path.Join(destination, name.Text)
		request := fileops.CopyRequest{Source: clip.Connection, Destination: dest, SourcePath: clip.Path, DestinationPath: target, SourceIdentity: clip.Identity, Move: clip.Move}
		root := w.App.session(w.ID)
		if root == nil {
			root = w
		}
		action := resolution.SelectedIndex()
		direction := "copy"
		if clip.Move {
			direction = "move"
		}
		before := func(ctx context.Context, spec transfers.Spec) (transfers.Spec, bool, error) {
			if clip.Context.Err() != nil {
				return spec, false, fmt.Errorf("原文件来源已关闭")
			}
			current, err := dest.Stat(ctx, target)
			if err == nil {
				if action != 2 {
					return spec, false, nil
				}
				if !current.Mode().IsRegular() {
					return spec, false, fmt.Errorf("目标不是普通文件，不能覆盖")
				}
				identity := fileops.IdentityOf(current)
				request.DestinationIdentity = &identity
				request.Overwrite = true
			} else if !os.IsNotExist(err) {
				return spec, false, err
			}
			return spec, true, nil
		}
		id := w.Transfers.AddOperation(transfers.Spec{Direction: direction, Local: clip.Path, Remote: target}, before, func(ctx context.Context, _ transfers.Spec, progress func(int64, int64)) error {
			ctx, cancel := context.WithCancel(ctx)
			defer cancel()
			stop := context.AfterFunc(clip.Context, cancel)
			defer stop()
			stopDest := context.AfterFunc(root.Context, cancel)
			defer stopDest()
			err := fileops.CopyFile(ctx, request, progress)
			fyne.Do(func() {
				if err == nil {
					if clip.Move {
						w.App.markMovedBindings(clip, target)
						if w.App.fileClipboard == clip {
							w.App.fileClipboard = nil
						}
					}
					w.App.refreshFileConnection(clip.Connection)
					w.App.refreshFileConnection(dest)
				}
			})
			return err
		}, transfers.ResourceKey(clip.Connection), transfers.ResourceKey(dest))
		if id == "" {
			w.Status.SetText("队列已关闭，未加入操作")
			return
		}
		if w.queueExpanded != nil {
			w.queueExpanded.SetChecked(true)
		}
		w.Status.SetText("已加入" + direction + "队列 · " + clip.SourceTitle + " → " + w.sourceTitle() + " · " + target)
	}, w.Window)
	fitPopup(popup, w.Window, fyne.NewSize(820, 520))
	popup.Show()
}
func (a *App) hasDirtyFileBinding(clip *fileClipboard) bool {
	for _, w := range a.sessions {
		for _, d := range w.documents {
			if d.Source == clip.Connection && d.Snapshot.Path == clip.Path && (d.New || d.Busy || d.Confirming || d.Editor.Document.Dirty()) {
				return true
			}
		}
	}
	return false
}
func (a *App) markMovedBindings(clip *fileClipboard, target string) {
	for _, w := range a.sessions {
		for _, d := range w.documents {
			if d.Source == clip.Connection && d.Snapshot.Path == clip.Path {
				d.SourceMoved = true
				d.Editor.ReadOnly = true
				d.setMessage("原文件已移动至 " + target + "；此标签保留原路径只读，请从目标重新打开。")
			}
		}
	}
}
func (a *App) refreshFileConnection(conn remotefs.Connection) {
	for _, owner := range a.sessions {
		for _, view := range owner.sourceViews {
			if view.FileConn == conn && view.fileAvailable() {
				if view.Path.Text == view.directory {
					view.Refresh()
				}
				if view.fileTree != nil {
					for id, open := range view.fileTree.expanded {
						if open && id != view.directory {
							view.fileTree.load(id, true)
						}
					}
				}
			}
		}
	}
}
func (w *Workspace) CopyOrMoveTo(move bool) {
	w.captureFileClipboard(move, func(clip *fileClipboard) {
		var targets []*Workspace
		var labels []string
		for _, target := range w.App.sessions {
			if target.fileAvailable() && target.Transfers != nil {
				targets = append(targets, target)
				labels = append(labels, fmt.Sprintf("%s · %s · %d", target.Protocol, target.Title, len(targets)))
			}
		}
		if len(targets) == 0 {
			w.Status.SetText("没有已连接的文件目标")
			return
		}
		source := widget.NewSelect(labels, nil)
		folder := widget.NewEntry()
		folder.SetText(w.directory)
		source.OnChanged = func(string) {
			i := source.SelectedIndex()
			if i >= 0 && i < len(targets) {
				folder.SetText(targets[i].directory)
			}
		}
		source.SetSelectedIndex(0)
		dialog.ShowForm("选择目标文件来源与目录", "下一步", "取消", []*widget.FormItem{widget.NewFormItem("目标来源", source), widget.NewFormItem("绝对目录", folder)}, func(ok bool) {
			if !ok {
				return
			}
			i := source.SelectedIndex()
			if i < 0 || i >= len(targets) || !absoluteDirectory(folder.Text) {
				w.Status.SetText("请选择已连接来源和安全绝对目录")
				return
			}
			targets[i].pasteClipboard(clip, folder.Text)
		}, w.Window)
	})
}
