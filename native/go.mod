module forterm/native

go 1.26.0

require (
	fyne.io/fyne/v2 v2.8.2
	github.com/ActiveState/termtest/conpty v0.5.0
	github.com/Microsoft/go-winio v0.6.2
	github.com/alecthomas/chroma/v2 v2.27.0
	github.com/charmbracelet/ultraviolet v0.0.0-20260303162955-0b88c25f3fff
	github.com/charmbracelet/x/ansi v0.11.7
	github.com/charmbracelet/x/vt v0.1.0
	github.com/creack/pty v1.1.21
	github.com/danieljoos/wincred v1.2.3
	github.com/fyne-io/terminal v0.0.0-20261023210238-45431670057e
	github.com/gofrs/flock v0.13.0
	github.com/jlaffaye/ftp v0.2.4
	github.com/keybase/go-keychain v0.0.1
	github.com/pkg/sftp v1.13.12
	github.com/rivo/uniseg v0.4.7
	golang.org/x/crypto v0.58.0
	golang.org/x/sys v0.49.0
)

require (
	fyne.io/systray v1.12.3-0.20260810170012-af4e8e793ec4 // indirect
	github.com/Azure/go-ansiterm v0.0.0-20170929234023-d6e3b3328b78 // indirect
	github.com/BurntSushi/toml v1.6.0 // indirect
	github.com/FyshOS/fancyfs v0.0.1 // indirect
	github.com/anthonynsimon/bild v0.14.0 // indirect
	github.com/charmbracelet/colorprofile v0.4.2 // indirect
	github.com/charmbracelet/x/exp/ordered v0.1.0 // indirect
	github.com/charmbracelet/x/term v0.2.2 // indirect
	github.com/charmbracelet/x/termios v0.1.1 // indirect
	github.com/charmbracelet/x/windows v0.2.2 // indirect
	github.com/clipperhouse/displaywidth v0.11.0 // indirect
	github.com/clipperhouse/uax29/v2 v2.7.0 // indirect
	github.com/dlclark/regexp2/v2 v2.2.1 // indirect
	github.com/fsnotify/fsnotify v1.9.0 // indirect
	github.com/fyne-io/gl-js v0.2.1-0.20260315212741-029c47fd27e8 // indirect
	github.com/fyne-io/glfw-js v0.4.0 // indirect
	github.com/fyne-io/image v0.1.1 // indirect
	github.com/fyne-io/oksvg v0.2.1-0.20260918172519-a9af55fa95d9 // indirect
	github.com/go-gl/gl v0.0.0-20260331235117-4566fea9a276 // indirect
	github.com/go-gl/glfw/v3.4/glfw v0.1.0-pre.2 // indirect
	github.com/go-text/render v0.2.1 // indirect
	github.com/go-text/typesetting v0.3.5
	github.com/godbus/dbus/v5 v5.2.2 // indirect
	github.com/hack-pad/go-indexeddb v0.3.2 // indirect
	github.com/hack-pad/safejs v0.1.0 // indirect
	github.com/jeandeaual/go-locale v0.0.0-20250612000132-0ef82f21eade // indirect
	github.com/jsummers/gobmp v0.0.0-20230614200233-a9de23ed2e25 // indirect
	github.com/kr/fs v0.1.0 // indirect
	github.com/lucasb-eyer/go-colorful v1.4.0 // indirect
	github.com/mattn/go-runewidth v0.0.24 // indirect
	github.com/muesli/cancelreader v0.2.2 // indirect
	github.com/nfnt/resize v0.0.0-20180221191011-83c6a9932646 // indirect
	github.com/nicksnyder/go-i18n/v2 v2.5.1 // indirect
	github.com/rymdport/portal v0.4.2 // indirect
	github.com/srwiley/oksvg v0.0.0-20221011165216-be6e8873101c // indirect
	github.com/srwiley/rasterx v0.0.0-20220730225603-2ab79fcdd4ef // indirect
	github.com/stretchr/testify v1.12.1 // indirect
	github.com/xo/terminfo v0.0.0-20220910002029-abceb7e1c41e // indirect
	github.com/yuin/goldmark v1.8.2 // indirect
	go.yaml.in/yaml/v3 v3.0.5 // indirect
	golang.org/x/image v0.24.0 // indirect
	golang.org/x/net v0.60.0 // indirect
	golang.org/x/sync v0.24.0 // indirect
	golang.org/x/text v0.43.0 // indirect
)

replace github.com/fyne-io/terminal => ./third_party/terminal

replace github.com/charmbracelet/x/vt => ./third_party/vt

replace github.com/pkg/sftp => ./third_party/sftp

replace github.com/jlaffaye/ftp => ./third_party/ftp

replace github.com/go-gl/glfw/v3.4/glfw => ./third_party/glfw
