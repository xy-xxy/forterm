package desktop

import (
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/canvas"
	"fyne.io/fyne/v2/theme"
	"fyne.io/fyne/v2/widget"
)

// Tree owns branch state and virtualization; this cell only draws the compact
// node content and the fine guide connecting a profile to its parent group.
type profileTreeCell struct {
	widget.BaseWidget
	branch bool
	label  *widget.Label
	icon   *widget.Icon
}

func newProfileTreeCell(branch bool) *profileTreeCell {
	c := &profileTreeCell{branch: branch, label: widget.NewLabel(""), icon: widget.NewIcon(theme.FolderIcon())}
	c.label.Truncation = fyne.TextTruncateEllipsis
	c.ExtendBaseWidget(c)
	return c
}
func (c *profileTreeCell) set(text string, branch bool) {
	c.branch = branch
	if c.label.Text != text {
		c.label.SetText(text)
	}
	if branch {
		c.icon.SetResource(theme.FolderIcon())
	} else {
		c.icon.SetResource(theme.ComputerIcon())
	}
	c.Refresh()
}
func (c *profileTreeCell) CreateRenderer() fyne.WidgetRenderer {
	return &profileTreeCellRenderer{c: c, vertical: canvas.NewLine(theme.Color(theme.ColorNameSeparator)), horizontal: canvas.NewLine(theme.Color(theme.ColorNameSeparator))}
}

type profileTreeCellRenderer struct {
	c                    *profileTreeCell
	vertical, horizontal *canvas.Line
}

func (r *profileTreeCellRenderer) MinSize() fyne.Size { return fyne.NewSize(60, 32) }
func (r *profileTreeCellRenderer) Layout(s fyne.Size) {
	x := float32(0)
	if !r.c.branch {
		x = 14
	}
	r.c.icon.Move(fyne.NewPos(x, (s.Height-16)/2))
	r.c.icon.Resize(fyne.NewSize(16, 16))
	r.c.label.Move(fyne.NewPos(x+20, 0))
	r.c.label.Resize(fyne.NewSize(max(0, s.Width-x-20), s.Height))
	r.vertical.Position1 = fyne.NewPos(5, 0)
	r.vertical.Position2 = fyne.NewPos(5, s.Height)
	r.horizontal.Position1 = fyne.NewPos(5, s.Height/2)
	r.horizontal.Position2 = fyne.NewPos(12, s.Height/2)
}
func (r *profileTreeCellRenderer) Refresh() {
	for _, line := range []*canvas.Line{r.vertical, r.horizontal} {
		line.StrokeColor = theme.Color(theme.ColorNameSeparator)
		line.StrokeWidth = 1
		if r.c.branch {
			line.Hide()
		} else {
			line.Show()
		}
		line.Refresh()
	}
	r.Layout(r.c.Size())
}
func (r *profileTreeCellRenderer) Objects() []fyne.CanvasObject {
	return []fyne.CanvasObject{r.vertical, r.horizontal, r.c.icon, r.c.label}
}
func (r *profileTreeCellRenderer) Destroy() {}

func (c *profileTreeCell) AccessibilityLabel() string             { return c.label.Text }
func (c *profileTreeCell) AccessibilityRole() fyne.AccessibleRole { return fyne.AccessibleRoleText }
