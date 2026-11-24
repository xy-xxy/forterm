package desktop

import (
	"forterm/native/internal/settings"
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/container"
	"fyne.io/fyne/v2/dialog"
	"fyne.io/fyne/v2/theme"
	"fyne.io/fyne/v2/widget"
	"sort"
)

type libraryEntry struct {
	group   string
	header  bool
	profile settings.Profile
}

func libraryEntries(profiles []settings.Profile) []libraryEntry {
	sort.SliceStable(profiles, func(i, j int) bool {
		if profiles[i].Group != profiles[j].Group {
			return profiles[i].Group < profiles[j].Group
		}
		return profiles[i].Title < profiles[j].Title
	})
	entries := make([]libraryEntry, 0, len(profiles))
	last := "\x00"
	for _, p := range profiles {
		if p.Group != last {
			last = p.Group
			title := last
			if title == "" {
				title = "默认分组"
			}
			entries = append(entries, libraryEntry{group: title, header: true})
		}
		entries = append(entries, libraryEntry{profile: p})
	}
	return entries
}

type libraryRow struct {
	widget.BaseWidget
	content  *fyne.Container
	group    *widget.Label
	controls *fyne.Container
	connect  *widget.Button
	entry    libraryEntry
}

func newLibraryRow(a *App, refresh func()) *libraryRow {
	r := &libraryRow{group: widget.NewLabelWithStyle("", fyne.TextAlignLeading, fyne.TextStyle{Bold: true})}
	r.group.Truncation = fyne.TextTruncateEllipsis
	r.connect = action("", theme.ComputerIcon(), func() { a.ConnectProfile(r.entry.profile.ID) })
	edit := action("", theme.DocumentCreateIcon(), func() { p := r.entry.profile; a.ProfileDialog(&p, refresh) })
	remove := action("", theme.DeleteIcon(), func() {
		p := r.entry.profile
		name := profileName(p)
		dialog.ShowConfirm("删除保存的连接？", name+"\n不关闭已有会话；同时清理此连接的系统保存凭据。", func(ok bool) {
			if !ok {
				return
			}
			go func() {
				err := a.Store.DeleteProfile(p.ID)
				fyne.Do(func() {
					if err != nil {
						dialog.ShowInformation("删除结果", err.Error(), a.Window)
					}
					refresh()
				})
			}()
		}, a.Window)
	})
	r.controls = container.NewBorder(nil, nil, nil, container.NewHBox(edit, remove), r.connect)
	r.content = container.NewStack(r.group, r.controls)
	r.ExtendBaseWidget(r)
	return r
}
func (r *libraryRow) CreateRenderer() fyne.WidgetRenderer { return widget.NewSimpleRenderer(r.content) }
func (r *libraryRow) setEntry(entry libraryEntry) {
	r.entry = entry
	if entry.header {
		r.group.SetText(entry.group)
		if !r.group.Visible() {
			r.group.Show()
		}
		if r.controls.Visible() {
			r.controls.Hide()
		}
	} else {
		name := []rune(profileName(entry.profile))
		if len(name) > 32 {
			name = append(name[:31], '…')
		}
		r.connect.SetText(string(name))
		if r.group.Visible() {
			r.group.Hide()
		}
		if !r.controls.Visible() {
			r.controls.Show()
		}
	}
}
func profileName(p settings.Profile) string {
	if p.Title != "" {
		return p.Title
	}
	return p.Host
}
