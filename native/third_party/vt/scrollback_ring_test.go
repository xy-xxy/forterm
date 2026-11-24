package vt

import (
	"fmt"
	uv "github.com/charmbracelet/ultraviolet"
	"testing"
)

func ringLine(text string) uv.Line {
	line := make(uv.Line, len(text))
	for i, r := range text {
		line[i] = uv.Cell{Content: string(r), Width: 1}
	}
	return line
}
func ringText(line uv.Line) string {
	out := ""
	for _, c := range line {
		out += c.Content
	}
	return out
}
func TestRingHistoryOrderCloneLimitAndClear(t *testing.T) {
	s := NewScrollback(3)
	for i := 0; i < 9; i++ {
		line := ringLine(fmt.Sprint(i))
		s.Push(line)
		line[0].Content = "X"
		start := max(0, i-2)
		for j := 0; j < s.Len(); j++ {
			if got := ringText(s.Line(j)); got != fmt.Sprint(start+j) {
				t.Fatalf("history[%d]=%q", j, got)
			}
		}
	}
	if all := s.Lines(); ringText(all[0]) != "6" || ringText(all[2]) != "8" {
		t.Fatal("wrapped Lines not chronological")
	}
	s.SetMaxLines(2)
	if s.Len() != 2 || ringText(s.Line(0)) != "7" || ringText(s.Line(1)) != "8" {
		t.Fatal("shrink lost newest history")
	}
	s.SetMaxLines(5)
	s.Push(ringLine("longer"))
	s.Push(ringLine("new"))
	if ringText(s.Line(0)) != "7" || ringText(s.Line(3)) != "new" {
		t.Fatal("grow changed order")
	}
	s.Clear()
	if s.Len() != 0 || s.Line(0) != nil {
		t.Fatal("clear retained visible history")
	}
	s.Push(ringLine("reset"))
	if ringText(s.Line(0)) != "reset" {
		t.Fatal("clear retained ring offset")
	}
}
func TestRingPushDoesNotAllocateOrLoseBlankAndWideTail(t *testing.T) {
	s := NewScrollback(3)
	line := ringLine("hello")
	for i := 0; i < 3; i++ {
		s.Push(line)
	}
	if allocs := testing.AllocsPerRun(1000, func() { s.Push(line) }); allocs != 0 {
		t.Fatalf("steady full history allocations %v", allocs)
	}
	wide := uv.Line{{Content: "中", Width: 2}, {Content: "", Width: 0}}
	s.Push(wide)
	if s.Line(2)[0].Content != "中" || s.CellAt(1, 2) != nil {
		t.Fatal("wide lead/trim semantics changed")
	}
	s.Push(nil)
	if s.Len() != 3 || len(s.Line(2)) != 0 {
		t.Fatal("blank line lost")
	}
}
