package vtwidget

import (
	"errors"
	"fyne.io/fyne/v2"
)

func (t *Terminal) RunLocalShell() error {
	var rows, cols uint
	fyne.DoAndWait(func() { rows, cols = t.config.Rows, t.config.Columns })
	in, out, closer, err := startLocal(rows, cols)
	if err != nil {
		return err
	}
	if t.stopped() {
		closer.Close()
		return errors.New("本地终端已关闭")
	}
	t.mu.Lock()
	t.local = closer
	t.mu.Unlock()
	return t.RunWithConnection(in, out)
}
