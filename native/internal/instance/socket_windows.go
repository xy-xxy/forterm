//go:build windows

package instance

import (
	"crypto/sha256"
	"fmt"
	"github.com/Microsoft/go-winio"
	"golang.org/x/sys/windows"
	"net"
	"time"
)

func sid() (string, error) {
	token := windows.GetCurrentProcessToken()
	u, e := token.GetTokenUser()
	if e != nil {
		return "", e
	}
	return u.User.Sid.String(), nil
}
func endpoint(dir string) (string, error) {
	s, e := sid()
	if e != nil {
		return "", e
	}
	sum := sha256.Sum256([]byte(s + dir))
	return fmt.Sprintf(`\\.\pipe\FortermNative-%x`, sum[:16]), nil
}
func listen(address string) (net.Listener, error) {
	s, e := sid()
	if e != nil {
		return nil, e
	}
	return winio.ListenPipe(address, &winio.PipeConfig{SecurityDescriptor: "D:P(A;;GA;;;" + s + ")", InputBufferSize: 65536, OutputBufferSize: 65536})
}
func dial(address string) (net.Conn, error) { t := time.Second; return winio.DialPipe(address, &t) }
