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
	"strconv"
	"time"
)

func (w *Workspace) showPermissions(permissions fileops.Permissions) {
	if !permissions.Known || permissions.Link {
		dialog.ShowInformation("权限不可修改", permissions.Message, w.Window)
		return
	}
	fs, ok := w.FileConn.(remotefs.Operations)
	if !ok {
		return
	}
	normal, files := newFormEntry(false), newFormEntry(false)
	normal.SetText(fmt.Sprintf("%03o", permissions.Mode&0777))
	files.PlaceHolder = "留空保留文件原权限；不默认添加执行位"
	recursive := newFormCheck("包含子目录和文件")
	sudo := newFormCheck("使用sudo")
	identity := newFormCheck("我确认此SSH来源指向期望的真实目标，与文件路径空间一致")
	if _, supported := fs.(fileops.PrivilegedPermissions); !supported {
		sudo.Disable()
		identity.Disable()
	}
	hint := widget.NewLabel(fmt.Sprintf("%s\n来源：%s\n特殊位按各项原值保留；递归先预览。取消不撤销已完成项。", permissions.Path, w.sourceTitle()))
	hint.Wrapping = fyne.TextWrapWord
	status := widget.NewLabel("尚未生成计划")
	status.Wrapping = fyne.TextWrapWord
	controls := container.NewGridWithColumns(3)
	checks := make([]*formCheck, 9)
	changing := false
	for i, label := range []string{"所有者读", "所有者写", "所有者执行", "组读", "组写", "组执行", "其他读", "其他写", "其他执行"} {
		checks[i] = newFormCheck(label)
		checks[i].OnChanged = func(bool) {
			if changing {
				return
			}
			var value uint32
			for j, c := range checks {
				if c != nil && c.Checked {
					value |= 1 << uint(8-j)
				}
			}
			changing = true
			normal.SetText(fmt.Sprintf("%03o", value))
			changing = false
		}
		controls.Add(checks[i])
	}
	normal.OnChanged = func(text string) {
		if changing {
			return
		}
		value, e := strconv.ParseUint(text, 8, 32)
		if e != nil || len(text) != 3 || value > 0777 {
			return
		}
		changing = true
		for i, c := range checks {
			c.SetChecked(value&(1<<uint(8-i)) != 0)
		}
		changing = false
	}
	normal.OnChanged(normal.Text)
	var busy bool
	var cancelPlan context.CancelFunc
	button := widget.NewButton("生成只读计划", nil)
	button.OnTapped = func() {
		if busy {
			return
		}
		value, e := strconv.ParseUint(normal.Text, 8, 32)
		if e != nil || len(normal.Text) != 3 || value > 0777 {
			status.SetText("权限须三位八进制000–777")
			return
		}
		var fileMode *uint32
		if files.Text != "" {
			v, e := strconv.ParseUint(files.Text, 8, 32)
			if e != nil || len(files.Text) != 3 || v > 0777 {
				status.SetText("文件权限须000–777或留空保留")
				return
			}
			n := uint32(v)
			fileMode = &n
		}
		useSudo := sudo.Checked
		if useSudo && !identity.Checked {
			status.SetText("请先明确确认关联SSH真实目标；不按代理IP匹配或另行登录")
			return
		}
		recurse := recursive.Checked
		busy = true
		button.Disable()
		ctx, cancel := context.WithTimeout(w.Context, 60*time.Second)
		cancelPlan = cancel
		status.SetText("正在只读枚举与核对范围，尚未更改权限…")
		go func() {
			defer cancel()
			plan, err := fileops.PlanPermissions(ctx, fs, permissions.Path, recurse, uint32(value), fileMode)
			confirmed := map[string]fileops.PrivilegeStat{}
			policy := "不使用sudo"
			if err == nil && useSudo {
				privileged := fs.(fileops.PrivilegedPermissions)
				for _, item := range plan.Items {
					if item.Skip {
						continue
					}
					snapshot, message, e := privileged.InspectPrivilege(ctx, item)
					if e != nil {
						err = e
						break
					}
					confirmed[item.Path] = snapshot
					policy = message
				}
			}
			fyne.Do(func() {
				busy = false
				button.Enable()
				if w.Context.Err() != nil {
					return
				}
				if err != nil {
					status.SetText("未生成可执行计划：" + err.Error())
					return
				}
				w.confirmPermissionPlan(fs, plan, useSudo, confirmed, policy)
			})
		}()
	}
	cancelButton := widget.NewButton("取消规划", func() {
		if cancelPlan != nil {
			cancelPlan()
		}
	})
	viewport := newFormViewport(container.NewVBox(hint, controls, widget.NewForm(widget.NewFormItem("当前项/目录普通权限", normal), widget.NewFormItem("递归普通文件权限", files)), recursive, sudo, identity))
	normal.reveal = viewport.Reveal
	normal.viewport = viewport
	files.reveal = viewport.Reveal
	files.viewport = viewport
	for _, c := range append(checks, recursive, sudo, identity) {
		c.reveal = viewport.Reveal
	}
	content := container.NewBorder(nil, container.NewVBox(status, container.NewHBox(button, cancelButton)), nil, nil, viewport)
	popup := dialog.NewCustom("文件权限", "关闭", container.NewThemeOverride(content, connectionDialogTheme{}), w.Window)
	popup.SetOnClosed(func() {
		if cancelPlan != nil {
			cancelPlan()
		}
	})
	fitPopup(popup, w.Window, fyne.NewSize(880, 650))
	popup.Show()
}
func (w *Workspace) confirmPermissionPlan(fs remotefs.Operations, plan fileops.PermissionPlan, useSudo bool, confirmed map[string]fileops.PrivilegeStat, policy string) {
	preview := widget.NewList(func() int { return len(plan.Items) }, func() fyne.CanvasObject {
		label := widget.NewLabel("")
		label.Truncation = fyne.TextTruncateEllipsis
		return label
	}, func(id widget.ListItemID, obj fyne.CanvasObject) {
		item := plan.Items[id]
		kind := "文件"
		if item.Directory {
			kind = "目录"
		}
		text := fmt.Sprintf("%s  %04o → %04o  %s", kind, fileops.RawMode(item.Before.Mode), item.Mode, item.Path)
		if item.Skip {
			text = "保留原权限 · " + item.Path
		}
		obj.(*widget.Label).SetText(text)
	})
	message := fmt.Sprintf("来源：%s\n范围：%s\n计划%d项，跳过%d个链接/特殊项\n%s\n执行后逐项重新读取验证；取消/失败不会回滚已完成项。", w.sourceTitle(), plan.Root, len(plan.Items), plan.Skipped, policy)
	if useSudo {
		message += "\nsudo身份：root；使用同一SSH独立exec，不向当前终端输入；不保存或猜试密码。"
	}
	label := widget.NewLabel(message)
	label.Wrapping = fyne.TextWrapWord
	popup := dialog.NewCustomConfirm("确认权限计划与身份", "执行计划", "返回", container.NewBorder(label, nil, nil, nil, container.New(&monitorViewport{height: 200}, preview)), func(ok bool) {
		if !ok || !w.fileAvailable() {
			return
		}
		var sudoPassword []byte
		var loaded bool
		provider := func(ctx context.Context) ([]byte, error) {
			if !loaded {
				answers, e := w.App.Challenge(ctx, "sudo权限验证 · root", []string{"sudo密码（仅本次计划，不保存，不使用SSH登录密码）"}, []bool{false})
				if e != nil {
					return nil, e
				}
				sudoPassword = []byte(answers[0])
				clear(answers)
				loaded = true
			}
			return append([]byte(nil), sudoPassword...), nil
		}
		id := w.Transfers.AddOperation(transfers.Spec{Direction: "permissions", Remote: plan.Root, Local: plan.Root}, nil, func(ctx context.Context, _ transfers.Spec, progress func(int64, int64)) error {
			defer clear(sudoPassword)
			err := fileops.ApplyPermissionPlan(ctx, fs, plan, useSudo, confirmed, provider, func(done, total int, name string) { progress(int64(done), int64(total)) })
			fyne.Do(func() {
				if w.Context.Err() == nil {
					if err != nil {
						w.Status.SetText(err.Error())
					} else {
						w.Status.SetText("权限计划已逐项读取确认")
					}
					w.App.refreshFileConnection(fs)
				}
			})
			return err
		}, transfers.ResourceKey(fs))
		if id != "" {
			w.queueExpanded.SetChecked(true)
			w.Status.SetText("权限计划已加入当前来源串行队列")
		}
	}, w.Window)
	fitPopup(popup, w.Window, fyne.NewSize(880, 620))
	popup.Show()
}
