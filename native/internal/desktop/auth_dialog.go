package desktop

import (
	"context"
	"errors"
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/container"
	"fyne.io/fyne/v2/dialog"
	"fyne.io/fyne/v2/widget"
	"sync"
)

func (a *App) credentialQuestions(ctx context.Context, title string, questions []string, echo []bool) ([]string, error) {
	if len(questions) > 16 {
		return nil, errors.New("认证问题过多")
	}
	reply := make(chan []string, 1)
	answered := make(chan struct{})
	var once sync.Once
	fyne.Do(func() {
		if ctx.Err() != nil {
			return
		}
		entries := make([]*formEntry, len(questions))
		body := container.NewVBox()
		viewport := newFormViewport(body)
		for i, q := range questions {
			if len([]rune(q)) > 1024 {
				q = string([]rune(q)[:1024]) + "…"
			}
			label := widget.NewLabel(q)
			label.Wrapping = fyne.TextWrapWord
			entry := newFormEntry(true)
			if i < len(echo) && echo[i] {
				entry = newFormEntry(false)
			}
			entries[i] = entry
			entry.reveal = viewport.Reveal
			entry.viewport = viewport
			body.Add(label)
			body.Add(entry)
		}
		note := widget.NewLabel("服务器要求的验证；内容仅用于当前认证。取消或超时不会自动重试。")
		note.Wrapping = fyne.TextWrapWord
		body.Add(note)
		popup := dialog.NewCustomConfirm(title, "继续", "取消", container.NewThemeOverride(viewport, connectionDialogTheme{}), func(ok bool) {
			var values []string
			if ok {
				values = make([]string, len(entries))
				for i, e := range entries {
					values[i] = e.Text
				}
			}
			for _, e := range entries {
				e.SetText("")
			}
			once.Do(func() { reply <- values; close(answered) })
		}, a.Window)
		fitPopup(popup, a.Window, fyne.NewSize(800, min(float32(620), 240+float32(len(questions))*72)))
		popup.Show()
		if len(entries) > 0 {
			a.Window.Canvas().Focus(entries[0])
		}
		go func() {
			select {
			case <-ctx.Done():
				fyne.Do(popup.Hide)
			case <-answered:
			}
		}()
	})
	select {
	case values := <-reply:
		if values == nil {
			return nil, errors.New("用户取消认证")
		}
		return values, nil
	case <-ctx.Done():
		return nil, ctx.Err()
	}
}
