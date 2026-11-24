package vtmodel

import (
	vt "github.com/charmbracelet/x/vt"
	"testing"
)

func TestCandidateGraphemeCells(t *testing.T) {
	for _, text := range []string{"e\u0301", "👩‍💻", "🇨🇳", "👍🏽"} {
		e := vt.NewEmulator(20, 4)
		e.WriteString(text)
		t.Logf("%q => cell=%#v cursor=%v", text, e.CellAt(0, 0), e.CursorPosition())
		if e.CellAt(0, 0).Content != text {
			t.Error("cluster lost")
		}
		e.Close()
	}
	e := vt.NewEmulator(20, 4)
	defer e.Close()
	e.WriteString("e")
	e.WriteString("\u0301")
	if e.CellAt(0, 0).Content != "e\u0301" {
		t.Fatal("combining mark split across writes lost")
	}
	for _, pieces := range [][]string{{"👩", "\u200d", "💻"}, {"🇨", "🇳"}, {"👍", "🏽"}} {
		terminal := vt.NewEmulator(20, 4)
		for _, piece := range pieces {
			terminal.WriteString(piece)
		}
		joined := ""
		for _, piece := range pieces {
			joined += piece
		}
		if terminal.CellAt(0, 0).Content != joined {
			t.Fatal("split emoji lost", terminal.CellAt(0, 0))
		}
		terminal.Close()
	}
	edge := vt.NewEmulator(8, 3)
	defer edge.Close()
	edge.WriteString("1234567中")
	if edge.CellAt(0, 1).Content != "中" || edge.CursorPosition().X != 2 {
		t.Fatal("wide edge wrap incorrect", edge.CursorPosition())
	}
}
