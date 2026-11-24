package localfile

import "testing"

func TestWindowsURIPathsPreserveSpacesUnicodeAndLiteralPercent(t *testing.T) {
	for _, v := range []struct{ p, a, want string }{{"/C:/Users/Test/中文 file.txt", "", `C:\Users\Test\中文 file.txt`}, {"D:/space dir/a%20b.txt", "", `D:\space dir\a%20b.txt`}, {"/share/中文.txt", "server", `\\server\share\中文.txt`}, {`\\server\share\a b.txt`, "", `\\server\share\a b.txt`}} {
		got, e := uriPathForOS(v.p, v.a, "windows")
		if e != nil || got != v.want {
			t.Fatalf("%q => %q %v", v.p, got, e)
		}
	}
	for _, p := range []string{"C:relative.txt", "relative.txt", "/missing-drive", "", "C:/bad\x00"} {
		if _, e := uriPathForOS(p, "", "windows"); e == nil {
			t.Fatalf("accepted ambiguous path %q", p)
		}
	}
}
