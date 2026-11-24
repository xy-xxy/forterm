package desktop

import (
	"fmt"
	"forterm/native/internal/metrics"
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/canvas"
	"fyne.io/fyne/v2/container"
	"fyne.io/fyne/v2/theme"
	"fyne.io/fyne/v2/widget"
	"image"
	"image/color"
	"math"
	"strings"
)

type usageRing struct {
	widget.BaseWidget
	value  float64
	known  bool
	label  *widget.Label
	raster *canvas.Raster
}

func newUsageRing() *usageRing {
	r := &usageRing{label: widget.NewLabel("—")}
	r.label.Alignment = fyne.TextAlignCenter
	r.label.TextStyle = fyne.TextStyle{Monospace: true}
	r.ExtendBaseWidget(r)
	r.raster = canvas.NewRaster(func(w, h int) image.Image {
		img := image.NewNRGBA(image.Rect(0, 0, w, h))
		cx, cy := float64(w)/2, float64(h)/2
		radius := math.Min(cx, cy) - 2
		stroke := math.Max(2, math.Min(cx, cy)*.12)
		track, active := theme.Color(theme.ColorNameSeparator), theme.Color(theme.ColorNamePrimary)
		for y := 0; y < h; y++ {
			for x := 0; x < w; x++ {
				dx, dy := float64(x)+.5-cx, float64(y)+.5-cy
				distance := math.Hypot(dx, dy)
				if math.Abs(distance-radius) > stroke/2 {
					continue
				}
				angle := math.Atan2(dx, -dy)
				if angle < 0 {
					angle += 2 * math.Pi
				}
				c := track
				if r.known && angle <= r.value*2*math.Pi {
					c = active
				}
				img.Set(x, y, c)
			}
		}
		return img
	})
	return r
}
func (r *usageRing) Set(value float64, known bool) {
	value = max(0, min(1, value))
	if r.value == value && r.known == known {
		return
	}
	r.value, r.known = value, known
	text := "—"
	if known {
		text = fmt.Sprintf("%.0f%%", 100*value)
	}
	r.label.SetText(text)
	r.raster.Refresh()
}
func (r *usageRing) CreateRenderer() fyne.WidgetRenderer { return &ringRenderer{r: r} }

type ringRenderer struct{ r *usageRing }

func (r *ringRenderer) MinSize() fyne.Size { return fyne.NewSize(56, 56) }
func (r *ringRenderer) Layout(s fyne.Size) {
	side := min(s.Width, s.Height)
	r.r.raster.Move(fyne.NewPos((s.Width-side)/2, (s.Height-side)/2))
	r.r.raster.Resize(fyne.NewSize(side, side))
	r.r.label.Move(fyne.NewPos(0, (s.Height-r.r.label.MinSize().Height)/2))
	r.r.label.Resize(fyne.NewSize(s.Width, r.r.label.MinSize().Height))
}
func (r *ringRenderer) Refresh() { r.r.raster.Refresh(); r.Layout(r.r.Size()) }
func (r *ringRenderer) Objects() []fyne.CanvasObject {
	return []fyne.CanvasObject{r.r.raster, r.r.label}
}
func (r *ringRenderer) Destroy() {}

type cpuGrid struct {
	widget.BaseWidget
	values      []metrics.CoreUsage
	columns     int
	list        *widget.List
	rowsCreated int
}

func newCPUGrid() *cpuGrid {
	g := &cpuGrid{columns: 2}
	g.ExtendBaseWidget(g)
	g.list = widget.NewList(func() int { return (len(g.values) + g.columns - 1) / g.columns }, func() fyne.CanvasObject { g.rowsCreated++; return newCPUGridRow(g) }, func(id widget.ListItemID, obj fyne.CanvasObject) { row := obj.(*cpuGridRow); row.bind(id) })
	return g
}
func (g *cpuGrid) Set(values []metrics.CoreUsage)      { g.values = values; g.list.Refresh(); g.Refresh() }
func (g *cpuGrid) CreateRenderer() fyne.WidgetRenderer { return &cpuGridRenderer{g: g} }

type cpuGridRenderer struct{ g *cpuGrid }

func (r *cpuGridRenderer) MinSize() fyne.Size { return fyne.NewSize(1, 160) }
func (r *cpuGridRenderer) Layout(s fyne.Size) {
	cols := max(1, min(6, int(s.Width/76)))
	if cols != r.g.columns {
		r.g.columns = cols
		r.g.list.Refresh()
	}
	r.g.list.Resize(s)
}
func (r *cpuGridRenderer) Refresh()                     { r.Layout(r.g.Size()); r.g.list.Refresh() }
func (r *cpuGridRenderer) Objects() []fyne.CanvasObject { return []fyne.CanvasObject{r.g.list} }
func (r *cpuGridRenderer) Destroy()                     {}

type cpuGridRow struct {
	widget.BaseWidget
	grid        *cpuGrid
	rings       []*usageRing
	labels      []*widget.Label
	cells       *fyne.Container
	index       int
	initialized bool
}

func newCPUGridRow(g *cpuGrid) *cpuGridRow {
	r := &cpuGridRow{grid: g, initialized: true, cells: container.NewGridWithColumns(g.columns)}
	r.ExtendBaseWidget(r)
	return r
}
func (r *cpuGridRow) bind(index int) {
	r.index = index
	if !r.initialized {
		r.ExtendBaseWidget(r)
		r.cells = container.NewGridWithColumns(r.grid.columns)
		r.initialized = true
	}
	for len(r.rings) < r.grid.columns {
		ring := newUsageRing()
		label := widget.NewLabel("")
		label.Alignment = fyne.TextAlignCenter
		r.rings = append(r.rings, ring)
		r.labels = append(r.labels, label)
		r.cells.Add(container.NewVBox(container.NewGridWrap(fyne.NewSize(50, 50), ring), label))
	}
	r.cells.Layout = container.NewGridWithColumns(r.grid.columns).Layout
	for col := range r.rings {
		cell := r.cells.Objects[col]
		i := index*r.grid.columns + col
		if col >= r.grid.columns || i >= len(r.grid.values) {
			cell.Hide()
			continue
		}
		v := r.grid.values[i]
		r.rings[col].Set(v.Percent/100, v.Valid)
		r.labels[col].SetText(strings.ToUpper(v.ID))
		cell.Show()
	}
	r.Refresh()
}
func (r *cpuGridRow) CreateRenderer() fyne.WidgetRenderer {
	if !r.initialized {
		r.bind(0)
	}
	return &cpuRowRenderer{row: r}
}

type cpuRowRenderer struct{ row *cpuGridRow }

func (r *cpuRowRenderer) MinSize() fyne.Size           { return fyne.NewSize(1, 78) }
func (r *cpuRowRenderer) Layout(s fyne.Size)           { r.row.cells.Resize(s) }
func (r *cpuRowRenderer) Refresh()                     { r.row.cells.Refresh() }
func (r *cpuRowRenderer) Objects() []fyne.CanvasObject { return []fyne.CanvasObject{r.row.cells} }
func (r *cpuRowRenderer) Destroy()                     {}

type netPoint struct {
	at, rx, tx float64
	valid      bool
}
type networkTrend struct {
	widget.BaseWidget
	points []netPoint
	raster *canvas.Raster
}

func newNetworkTrend() *networkTrend {
	n := &networkTrend{}
	n.ExtendBaseWidget(n)
	n.raster = canvas.NewRaster(n.paint)
	return n
}
func (n *networkTrend) Add(p netPoint) {
	n.points = append(n.points, p)
	if len(n.points) > 60 {
		copy(n.points, n.points[len(n.points)-60:])
		n.points = n.points[:60]
	}
	n.raster.Refresh()
}
func (n *networkTrend) Clear() { n.points = nil; n.raster.Refresh() }
func (n *networkTrend) paint(w, h int) image.Image {
	img := image.NewNRGBA(image.Rect(0, 0, w, h))
	if len(n.points) < 2 {
		return img
	}
	start, end := n.points[0].at, n.points[len(n.points)-1].at
	if end <= start {
		return img
	}
	peak := float64(1)
	for _, p := range n.points {
		if p.valid {
			peak = max(peak, max(p.rx, p.tx))
		}
	}
	line := func(x0, y0, x1, y1 int, c color.Color) {
		steps := max(abs(x1-x0), abs(y1-y0))
		for i := 0; i <= steps; i++ {
			t := float64(i) / float64(max(1, steps))
			x := int(float64(x0) + float64(x1-x0)*t)
			y := int(float64(y0) + float64(y1-y0)*t)
			if x >= 0 && x < w && y >= 0 && y < h {
				img.Set(x, y, c)
			}
		}
	}
	for i := 1; i < len(n.points); i++ {
		a, b := n.points[i-1], n.points[i]
		if !a.valid || !b.valid {
			continue
		}
		x0, x1 := int((a.at-start)/(end-start)*float64(w-1)), int((b.at-start)/(end-start)*float64(w-1))
		line(x0, int(float64(h-2)*(1-a.rx/peak)), x1, int(float64(h-2)*(1-b.rx/peak)), theme.Color(theme.ColorNamePrimary))
		line(x0, int(float64(h-2)*(1-a.tx/peak)), x1, int(float64(h-2)*(1-b.tx/peak)), theme.Color(theme.ColorNameDisabled))
	}
	return img
}
func (n *networkTrend) CreateRenderer() fyne.WidgetRenderer { return &trendRenderer{n: n} }

type trendRenderer struct{ n *networkTrend }

func (r *trendRenderer) MinSize() fyne.Size           { return fyne.NewSize(1, 70) }
func (r *trendRenderer) Layout(s fyne.Size)           { r.n.raster.Resize(s) }
func (r *trendRenderer) Refresh()                     { r.n.raster.Refresh() }
func (r *trendRenderer) Objects() []fyne.CanvasObject { return []fyne.CanvasObject{r.n.raster} }
func (r *trendRenderer) Destroy()                     {}
func abs(x int) int {
	if x < 0 {
		return -x
	}
	return x
}
func monitorIcon(kind string) fyne.Resource {
	paths := map[string]string{"cpu": `M5 5h14v14H5z M7 7v10h10V7z M8 1h2v4H8z M14 1h2v4h-2z M8 19h2v4H8z M14 19h2v4h-2z M1 8h4v2H1z M1 14h4v2H1z M19 8h4v2h-4z M19 14h4v2h-4z`, "memory": `M2 5h20v13H2z M4 7v8h16V7z M5 8h3v5H5z M10 8h3v5h-3z M15 8h3v5h-3z M5 18h2v3H5z M10 18h2v3h-2z M15 18h2v3h-2z`, "disk": `M4 3h16v18H4z M6 5v14h12V5z M8 7h8v2H8z M8 15h5v2H8z M15 15h2v2h-2z`, "load": `M3 3h2v16h16v2H3z M7 13h3v4H7z M12 9h3v8h-3z M17 5h3v12h-3z`}
	if path := paths[kind]; path != "" {
		return theme.NewThemedResource(fyne.NewStaticResource("monitor-"+kind+".svg", []byte(`<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path fill="#ffffff" fill-rule="evenodd" d="`+path+`"/></svg>`)))
	}
	if kind == "code" {
		return theme.DocumentIcon()
	}
	return theme.ComputerIcon()
}
func monitorHeading(name, kind string) fyne.CanvasObject {
	return container.NewHBox(container.NewGridWrap(fyne.NewSize(16, 16), widget.NewIcon(monitorIcon(kind))), widget.NewLabelWithStyle(name, fyne.TextAlignLeading, fyne.TextStyle{Bold: true}))
}
