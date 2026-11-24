package codeedit

import (
	"strings"
	"testing"
)

func TestBytePreservationAndUndoAcrossSave(t *testing.T) {
	raw := "\ufeffa  \r\n\tb\r\n"
	d := NewDocument(raw)
	if d.Dirty() || d.Serialize() != raw || d.Text() != "a  \n\tb\n" {
		t.Fatal("initial bytes changed")
	}
	d.SetPosition(1, 2, false)
	d.Replace("  ")
	changed := d.Serialize()
	if !strings.Contains(changed, "\tb  \r\n") {
		t.Fatal("insert or newline format wrong")
	}
	d.MarkSaved(changed)
	if d.Dirty() {
		t.Fatal("save did not clear dirty")
	}
	d.Undo()
	if d.Serialize() != raw || !d.Dirty() {
		t.Fatal("saved state erased undo or BOM")
	}
	d.Redo()
	if d.Serialize() != changed || d.Dirty() {
		t.Fatal("redo after save wrong")
	}
}
func TestMixedReadOnlyAndExplicitConversion(t *testing.T) {
	raw := "\ufeffone\r\ntwo\nthree\r"
	d := NewDocument(raw)
	d.Replace("oops")
	if d.Serialize() != raw || d.Dirty() {
		t.Fatal("mixed EOL edited implicitly")
	}
	d.ConvertEOL("\n")
	if d.Serialize() != "\ufeffone\ntwo\nthree\n" || !d.Dirty() {
		t.Fatal("explicit EOL conversion failed")
	}
	d.Undo()
	if d.Serialize() != raw || d.Dirty() {
		t.Fatal("conversion undo lost mixed bytes")
	}
}
func TestGraphemeDeletionAndVerticalCaretBoundaries(t *testing.T) {
	d := NewDocument("é👩‍💻🇨🇳👍🏽")
	d.SelectAll()
	d.Anchor = d.Caret
	d.Delete(true)
	if d.Text() != "é👩‍💻🇨🇳" {
		t.Fatal("skin-tone grapheme split")
	}
	d.Delete(true)
	d.Delete(true)
	d.Delete(true)
	if d.Text() != "" {
		t.Fatal("combining/ZWJ deletion split")
	}
	d = NewDocument("👩‍💻abc\nx")
	d.SetPosition(0, 1, false)
	if d.Caret != 0 {
		t.Fatal("caret inside grapheme")
	}
}
