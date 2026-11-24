package widget

import (
	"context"
	"time"

	"fyne.io/fyne/v2/container"
	"fyne.io/fyne/v2/widget"

	"fyne.io/fyne/v2"
)

const blinkingInterval = 500 * time.Millisecond

// TermGrid is a monospaced grid of characters.
// This is designed to be used by our terminal emulator.
type TermGrid struct {
	widget.TextGrid

	tickerCancel context.CancelFunc
}

// CreateRenderer is a private method to Fyne which links this widget to it's renderer
func (t *TermGrid) CreateRenderer() fyne.WidgetRenderer {
	t.ExtendBaseWidget(t)

	r := &cellRenderer{grid: t}
	r.Refresh()
	return r
}

// NewTermGrid creates a new empty TextGrid widget.
func NewTermGrid() *TermGrid {
	grid := &TermGrid{}
	grid.ExtendBaseWidget(grid)

	grid.Scroll = container.ScrollNone
	return grid
}

// Refresh will be called when this grid should update.
// We update our blinking status and then call the TextGrid we extended to refresh too.
func (t *TermGrid) Refresh() {
	t.refreshBlink(false)
}

func (t *TermGrid) refreshBlink(blink bool) {
	// reset shouldBlink which can be set by setCellRune if a cell with BlinkEnabled is found
	shouldBlink := false

	for _, row := range t.Rows {
		for _, r := range row.Cells {
			if s, ok := r.Style.(*TermTextGridStyle); ok && s != nil && s.BlinkEnabled {
				shouldBlink = true

				s.blink(blink)
			}
		}
	}
	t.TextGrid.Refresh()

	switch {
	case shouldBlink && t.tickerCancel == nil:
		t.runBlink()
	case !shouldBlink && t.tickerCancel != nil:
		t.tickerCancel()
		t.tickerCancel = nil
	}
}

func (t *TermGrid) runBlink() {
	if t.tickerCancel != nil {
		t.tickerCancel()
		t.tickerCancel = nil
	}
	var tickerContext context.Context
	tickerContext, t.tickerCancel = context.WithCancel(context.Background())
	ticker := time.NewTicker(blinkingInterval)
	blinking := false
	go func() {
		for {
			select {
			case <-tickerContext.Done():
				return
			case <-ticker.C:
				blinking = !blinking
				fyne.Do(func() {
					t.refreshBlink(blinking)
				})
			}
		}
	}()
}

// SetCell updates the backing grid. The terminal batches a single Refresh after
// parsing each read, so per-cell writes do not invoke TextGrid's private renderer.
func (t *TermGrid) SetCell(row, col int, cell widget.TextGridCell) {
	if row < 0 || col < 0 {
		return
	}
	for len(t.Rows) <= row {
		t.Rows = append(t.Rows, widget.TextGridRow{})
	}
	for len(t.Rows[row].Cells) <= col {
		t.Rows[row].Cells = append(t.Rows[row].Cells, widget.TextGridCell{})
	}
	t.Rows[row].Cells[col] = cell
}
