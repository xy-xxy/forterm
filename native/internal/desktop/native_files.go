package desktop

import (
	"context"
	"fmt"
	"forterm/native/internal/localfile"
	"forterm/native/internal/nativepicker"
	"forterm/native/internal/transfers"
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/dialog"
	"path"
	"path/filepath"
	"time"
)

func (w *Workspace) picker(ctx context.Context, r nativepicker.Request, callback func(nativepicker.Result)) error {
	provider := nativepicker.Show
	if w.App != nil && w.App.FileDialogs != nil {
		provider = w.App.FileDialogs
	}
	return provider(ctx, w.Window, r, callback)
}
func (w *Workspace) uploadNative(start fyne.ListableURI) {
	if w.pickerBusy {
		return
	}
	directory, ok := w.uploadDirectory()
	if !ok {
		return
	}
	w.uploadTrace.record(uploadRequested)
	w.pickerBusy = true
	ctx, cancel := context.WithCancel(w.Context)
	initial := ""
	if start != nil {
		initial = start.Path()
	}
	w.Status.SetText("等待系统文件选择（可多选）；尚未加入队列")
	err := w.picker(ctx, nativepicker.Request{Title: "选择上传文件", Multiple: true, InitialDirectory: initial}, func(result nativepicker.Result) {
		defer cancel()
		w.pickerBusy = false
		w.uploadTrace.record(uploadCallback)
		if ctx.Err() != nil || !w.fileAvailable() {
			return
		}
		if result.Err != nil {
			w.uploadError(result.Err.Error())
			return
		}
		if result.Cancelled || len(result.Paths) == 0 {
			w.uploadTrace.record(uploadCancelled)
			w.Status.SetText("已取消选择文件，未加入上传队列")
			return
		}
		locals := []string{}
		for _, name := range result.Paths {
			if !filepath.IsAbs(name) {
				w.uploadError("系统返回的本地路径不是绝对路径")
				return
			}
			locals = append(locals, name)
		}
		w.EnqueueUploads(locals, directory)
	})
	if err != nil {
		cancel()
		w.pickerBusy = false
		dialog.ShowConfirm("系统文件选择器不可用", err.Error()+"\n改用内置选择器？内置选择器不是Finder/Explorer。", func(ok bool) {
			if ok && w.fileAvailable() {
				w.uploadUsingFyne(start, directory)
			}
		}, w.Window)
	} else {
		w.uploadTrace.record(uploadPickerShown)
	}
}
func (w *Workspace) downloadNative() {
	entry, remote, ok := w.selectedRemoteEntry()
	if !ok || entry.Directory || !w.fileAvailable() || w.Transfers == nil || w.pickerBusy {
		return
	}
	name := path.Base(remote)
	if !localfile.ValidName(name) {
		w.Status.SetText("文件名不适合本地保存")
		return
	}
	w.pickerBusy = true
	ctx, cancel := context.WithCancel(w.Context)
	err := w.picker(ctx, nativepicker.Request{Save: true, Title: "保存下载文件", Name: name}, func(result nativepicker.Result) {
		w.pickerBusy = false
		if ctx.Err() != nil || !w.fileAvailable() {
			cancel()
			return
		}
		if result.Err != nil {
			cancel()
			w.Status.SetText(result.Err.Error())
			return
		}
		if result.Cancelled || len(result.Paths) == 0 {
			cancel()
			w.Status.SetText("已取消保存位置，未下载")
			return
		}
		if len(result.Paths) != 1 {
			cancel()
			w.Status.SetText("系统返回多个保存路径，未下载")
			return
		}
		target := result.Paths[0]
		if !filepath.IsAbs(target) {
			cancel()
			w.Status.SetText("系统返回无效保存路径")
			return
		}
		// No local file is opened/truncated by the dialog callback.
		go func() {
			inspect, cancelInspect := context.WithTimeout(ctx, 10*time.Second)
			version, e := localfile.CaptureTargetContext(inspect, target)
			cancelInspect()
			fyne.Do(func() {
				defer cancel()
				if ctx.Err() != nil || !w.fileAvailable() {
					return
				}
				if e != nil {
					w.Status.SetText(e.Error())
					return
				}
				before := func(ctx context.Context, spec transfers.Spec) (transfers.Spec, bool, error) {
					if e := version.Check(); e != nil {
						return spec, false, e
					}
					spec.Overwrite = version.Exists
					spec.CommitGuard = version.Check
					return spec, true, nil
				}
				id := w.Transfers.Add(transfers.Spec{Direction: "download", Remote: remote, Local: target}, before)
				if id == "" {
					w.Status.SetText("队列已关闭，未下载")
				} else {
					w.queueExpanded.SetChecked(true)
					w.Status.SetText(fmt.Sprintf("下载已加入原来源队列 · %s", remote))
				}
			})
		}()
	})
	if err != nil {
		cancel()
		w.pickerBusy = false
		dialog.ShowConfirm("系统保存对话框不可用", err.Error()+"\n改用内置文件夹选择器并按原文件名保存？", func(ok bool) {
			if !ok {
				return
			}
			dialog.ShowFolderOpen(func(folder fyne.ListableURI, e error) {
				if e != nil || folder == nil {
					return
				}
				local, e := localfile.URIPath(folder)
				if e == nil && w.fileAvailable() {
					w.EnqueueDownload(remote, filepath.Join(local, name))
				}
			}, w.Window)
		}, w.Window)
	}
}
