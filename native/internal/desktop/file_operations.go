package desktop

import (
	"forterm/native/internal/fileops"
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/dialog"
	"fyne.io/fyne/v2/widget"
	"path"
)

func (w *Workspace) operationPath() (string, bool) {
	entry, file, ok := w.selectedRemoteEntry()
	if !ok || !w.fileAvailable() {
		return "", false
	}
	if e := fileops.ChildName(entry.Name); e != nil {
		w.Status.SetText(e.Error())
		return "", false
	}
	if _, e := fileops.SafePath(file); e != nil {
		w.Status.SetText("目录尚未解析为安全绝对路径，请刷新后再操作")
		return "", false
	}
	return file, true
}
func (w *Workspace) MakeDirectory() {
	if w.Operations == nil {
		return
	}
	directory := w.directory
	name := widget.NewEntry()
	dialog.ShowForm("新建目录", "创建", "取消", []*widget.FormItem{widget.NewFormItem("目录名", name)}, func(ok bool) {
		if !ok {
			return
		}
		if e := fileops.ChildName(name.Text); e != nil {
			w.Status.SetText(e.Error())
			return
		}
		target := path.Join(directory, name.Text)
		go func() {
			e := w.Operations.Mkdir(w.Context, target)
			fyne.Do(func() {
				if w.Context.Err() != nil {
					return
				}
				if e != nil {
					w.Status.SetText(e.Error())
				} else {
					w.Status.SetText("目录已创建")
					w.Refresh()
				}
			})
		}()
	}, w.Window)
}
func (w *Workspace) RenameSelected() {
	source, ok := w.operationPath()
	if !ok || w.Operations == nil {
		return
	}
	name := widget.NewEntry()
	name.SetText(path.Base(source))
	dialog.ShowForm("重命名（不覆盖同名目标）", "重命名", "取消", []*widget.FormItem{widget.NewFormItem("新名称", name)}, func(ok bool) {
		if !ok {
			return
		}
		if e := fileops.ChildName(name.Text); e != nil {
			w.Status.SetText(e.Error())
			return
		}
		destination := path.Join(path.Dir(source), name.Text)
		go func() {
			e := w.Operations.Rename(w.Context, source, destination)
			fyne.Do(func() {
				if w.Context.Err() != nil {
					return
				}
				if e != nil {
					w.Status.SetText(e.Error())
				} else {
					w.Status.SetText("重命名已完成；已打开草稿仍绑定原路径")
					w.Refresh()
				}
			})
		}()
	}, w.Window)
}
func (w *Workspace) DeleteSelected() {
	file, ok := w.operationPath()
	if !ok || w.Operations == nil {
		return
	}
	warning := file + "\n将先检查目录边界和内容，再删除。最多2000项/32层/2分钟；取消不会回滚已删除项。"
	for _, d := range w.documents {
		if d.Snapshot.Path == file || path.Dir(d.Snapshot.Path) == file {
			warning += "\n注意：此路径有关联编辑草稿，删除不会保存草稿。"
			break
		}
	}
	confirm := dialog.NewConfirm("确认删除远程文件/目录？", warning, func(ok bool) {
		if !ok {
			return
		}
		go func() {
			err := w.Operations.Remove(w.Context, file)
			fyne.Do(func() {
				if w.Context.Err() != nil {
					return
				}
				if err != nil {
					w.Status.SetText(err.Error() + "；保留当前列表以便核对")
				} else {
					w.Status.SetText("删除已完成")
					w.Refresh()
				}
			})
		}()
	}, w.Window)
	confirm.SetConfirmText("删除")
	confirm.SetDismissText("取消")
	confirm.Show()
}
func (w *Workspace) CopySelectedPath() {
	if file, ok := w.operationPath(); ok {
		w.App.UI.Clipboard().SetContent(file)
		w.Status.SetText("已复制远程路径")
	}
}
func (w *Workspace) PermissionsSelected() {
	file, ok := w.operationPath()
	if !ok || w.Operations == nil {
		return
	}
	w.Status.SetText("读取文件权限…")
	go func() {
		permissions, err := w.Operations.Permissions(w.Context, file)
		fyne.Do(func() {
			if w.Context.Err() != nil {
				return
			}
			if err != nil {
				w.Status.SetText(err.Error())
				return
			}
			w.showPermissions(permissions)
		})
	}()
}

// ConfirmExactDirectory is an explicit alternative for gateways that restrict
// REALPATH. No permission error or timeout automatically triggers this path.
func (w *Workspace) ConfirmExactDirectory() {
	if w.FileConn == nil || w.FileConn.Closed() {
		w.uploadError("文件连接尚未就绪或已断开；不会自动重登")
		return
	}
	target := w.Path.Text
	if !absoluteDirectory(target) {
		w.uploadError("请先在路径栏输入完整绝对目录；不会猜测当前目录")
		return
	}
	if w.busy {
		w.uploadError("当前目录读取尚未结束，请等待后再选择直接读取")
		return
	}
	dialog.ShowConfirm("确认直接读取绝对目录？", w.sourceTitle()+" · "+target+"\n此操作只对你确认的路径执行LSTAT和LIST，不执行REALPATH、不上传或写入。成功后会将这个精确路径作为当前目录。不会重新登录或重用临时票据。", func(ok bool) {
		if !ok || w.Context.Err() != nil {
			return
		}
		if w.busy {
			w.uploadError("目录仍在读取，本次直接读取尚未开始")
			return
		}
		w.Path.SetText(target)
		w.refreshDirectory(true)
	}, w.Window)
}
