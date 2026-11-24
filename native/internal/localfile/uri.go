package localfile

import (
	"errors"
	"fyne.io/fyne/v2"
	"runtime"
	"strings"
)

// URIPath uses the already-decoded URI path exactly once. Fyne file pickers can
// return /C:/... on Windows; that prefix must not become a drive-relative path.
func URIPath(uri fyne.URI) (string, error) {
	if uri == nil || strings.ToLower(uri.Scheme()) != "file" {
		return "", errors.New("请选择本地文件或目录")
	}
	return uriPathForOS(uri.Path(), uri.Authority(), runtime.GOOS)
}
func uriPathForOS(p, authority, platform string) (string, error) {
	if p == "" || strings.ContainsRune(p, 0) {
		return "", errors.New("本地路径为空或无效")
	}
	if platform != "windows" {
		if authority != "" && authority != "localhost" {
			return "", errors.New("不支持此本地文件地址")
		}
		if !strings.HasPrefix(p, "/") {
			return "", errors.New("本地路径不是绝对路径")
		}
		return p, nil
	}
	if authority != "" && authority != "localhost" {
		p = "//" + authority + "/" + strings.TrimPrefix(p, "/")
	}
	if len(p) >= 4 && p[0] == '/' && isDrive(p[1]) && p[2] == ':' && (p[3] == '/' || p[3] == '\\') {
		p = p[1:]
	}
	p = strings.ReplaceAll(p, "/", `\`)
	if len(p) >= 3 && isDrive(p[0]) && p[1] == ':' && p[2] == '\\' {
		return p, nil
	}
	if strings.HasPrefix(p, `\\`) {
		parts := strings.Split(strings.TrimPrefix(p, `\\`), `\`)
		if len(parts) >= 2 && parts[0] != "" && parts[1] != "" {
			return p, nil
		}
	}
	return "", errors.New("Windows本地路径缺少驱动器或共享目录")
}
func isDrive(c byte) bool { return c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' }
