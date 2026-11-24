package vtwidget

import (
	"fyne.io/fyne/v2/canvas"
	"testing"
)

func TestASCIIStringReuseAndImmutableChangedRuns(t *testing.T) {
	row := []cellPaint{{content: "a"}, {content: "b"}, {content: "c"}, {content: "d"}, {content: "e"}}
	r := &renderer{texts: []*canvas.Text{{Text: "abcde"}}}
	var got string
	if allocs := testing.AllocsPerRun(1000, func() { got = r.asciiRun(row, 0, len(row)) }); allocs != 0 || got != "abcde" {
		t.Fatalf("unchanged run allocated %v or lost text", allocs)
	}
	row[2].content = "X"
	if allocs := testing.AllocsPerRun(1000, func() { got = r.asciiRun(row, 0, len(row)) }); allocs > 1 || got != "abXde" {
		t.Fatalf("changed run allocated %v or lost text", allocs)
	}
	row[2].content = "Y"
	if got != "abXde" || r.texts[0].Text != "abcde" {
		t.Fatal("run strings alias mutable scratch")
	}
	if clipped := r.asciiRun(row, 1, 4); clipped != "bYd" {
		t.Fatal("run substring incorrect", clipped)
	}
}
