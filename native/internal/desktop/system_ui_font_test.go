package desktop

import (
	"bytes"
	"encoding/binary"
	"encoding/json"
	"os"
	"path/filepath"
	"runtime"
	"strings"
	"testing"

	"fyne.io/fyne/v2"
	textfont "github.com/go-text/typesetting/font"
)

func TestSystemUISansIsPrimaryForChineseWhileMonospacePolicyUnchanged(t *testing.T) {
	report := SystemUIFontSnapshot()
	raw, _ := json.Marshal(report)
	t.Log(string(raw))
	if runtime.GOOS == "darwin" && uiSansRegular == nil {
		t.Fatal("system CJK sans font not selected on this Mac")
	}
	if uiSansRegular == nil {
		t.Skip("no supported system CJK sans font installed")
	}
	for _, style := range []fyne.TextStyle{{}, {Bold: true}} {
		resource := desktopTheme{}.Font(style)
		face, err := textfont.ParseTTF(bytes.NewReader(resource.Content()))
		if err != nil {
			t.Fatal(err)
		}
		for _, r := range []rune("中文连接管理系统名称地址状态备注操作编辑更多导入导出刷新堡垒机账户") {
			if glyph, ok := face.NominalGlyph(r); !ok || glyph == 0 {
				t.Fatalf("primary font lacks %c", r)
			}
		}
		family := strings.ToLower(face.Font.Describe().Family)
		if !strings.Contains(family, "sans") && !strings.Contains(family, "hei") && !strings.Contains(family, "pingfang") {
			t.Fatal("UI selected a serif font: " + family)
		}
		if fontChecksum(resource.Content()) != 0xb1b0afba {
			t.Fatal("standalone SFNT checksum invalid")
		}
	}
	mono := desktopTheme{}.Font(fyne.TextStyle{Monospace: true})
	if mono == uiSansRegular || mono == uiSansBold {
		t.Fatal("CJK UI font replaced terminal monospace")
	}
	if out := os.Getenv("FORTERM_UI_EVIDENCE"); out != "" {
		os.MkdirAll(out, 0700)
		os.WriteFile(filepath.Join(out, "font-selection.json"), raw, 0600)
	}
}
func TestStandaloneSystemFaceRejectsMalformedCollectionBounds(t *testing.T) {
	for _, data := range [][]byte{nil, []byte("ttcf"), make([]byte, 12), append([]byte("ttcf"), make([]byte, 12)...)} {
		if _, err := standaloneFontFace(data, 0); err == nil {
			t.Fatal("accepted malformed font")
		}
	}
	malformed := make([]byte, 28)
	copy(malformed, "ttcf")
	binary.BigEndian.PutUint32(malformed[8:12], 1)
	binary.BigEndian.PutUint32(malformed[12:16], 0xffffffff)
	if _, err := standaloneFontFace(malformed, 0); err == nil {
		t.Fatal("accepted font offset past EOF")
	}
}
