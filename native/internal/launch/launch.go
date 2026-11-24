package launch

import (
	"errors"
	"net"
	"net/url"
	"path/filepath"
	"strconv"
	"strings"
)

type Connection struct {
	AuthMethod, Passphrase, ProfileID                  string
	Temporary                                          bool
	Jumps                                              []Connection
	Protocol, Host, Username, Password, Title, KeyPath string
	Port                                               int
	HasPassword                                        bool
}
type Plan struct {
	Connection          *Connection
	Diagnose, NewWindow bool
	Entry               string
	Count               int
	Error               string
}

func unwrap(s string) string {
	if len(s) > 1 && s[0] == '"' && s[len(s)-1] == '"' {
		return s[1 : len(s)-1]
	}
	return s
}
func URL(raw string) (Connection, error) {
	raw = unwrap(raw)
	if strings.ContainsAny(raw, "\r\n\x00") {
		return Connection{}, errors.New("连接URL格式无效")
	}
	u, e := url.Parse(raw)
	if e != nil || u == nil {
		return Connection{}, errors.New("连接URL格式无效")
	}
	protocol := strings.ToLower(u.Scheme)
	if protocol != "ssh" && protocol != "sftp" && protocol != "ftp" {
		return Connection{}, errors.New("不支持的连接协议")
	}
	if u.User == nil || u.User.Username() == "" || u.Hostname() == "" || u.RawQuery != "" || u.Fragment != "" || (u.Path != "" && u.Path != "/") {
		return Connection{}, errors.New("连接URL缺少必要字段或含不支持的路径参数")
	}
	port := 22
	if protocol == "ftp" {
		port = 21
	}
	if u.Port() != "" {
		port, e = strconv.Atoi(u.Port())
		if e != nil || port < 1 || port > 65535 {
			return Connection{}, errors.New("连接端口无效")
		}
	}
	if strings.ContainsAny(u.Hostname(), " \t\r\n/\\") {
		return Connection{}, errors.New("连接地址无效")
	}
	pass, hasPassword := u.User.Password()
	return Connection{Temporary: true, Protocol: protocol, Host: u.Hostname(), Port: port, Username: u.User.Username(), Password: pass, HasPassword: hasPassword}, nil
}
func Parse(args []string, entry string) Plan {
	p := Plan{Entry: strings.TrimSuffix(strings.ToLower(filepath.Base(entry)), ".exe"), Count: len(args)}
	if p.Entry == "forterm-diagnose" {
		p.Diagnose = true
	}
	var raw, title, key string
	known := func(s string) bool {
		switch strings.ToLower(s) {
		case "-url", "-newtab", "-newwin", "-i", "--diagnose", "--entry=xshell", "--entry=xftp", "--entry=diagnose":
			return true
		}
		return false
	}
	if len(args) > 128 {
		p.Error = "启动参数过多"
		return p
	}
	for _, arg := range args {
		if len(arg) > 32768 {
			p.Error = "启动参数过长"
			return p
		}
	}
	for i := 0; i < len(args); i++ {
		a := unwrap(args[i])
		if len(a) > 32768 {
			p.Error = "启动参数过长"
			break
		}
		low := strings.ToLower(a)
		switch low {
		case "--diagnose", "--entry=diagnose":
			p.Diagnose = true
		case "--entry=xshell":
			p.Entry = "xshell"
		case "--entry=xftp":
			p.Entry = "xftp"
		case "-url", "-newtab", "-newwin", "-i":
			if low == "-newwin" {
				p.NewWindow = true
				if i+1 < len(args) && strings.EqualFold(unwrap(args[i+1]), "-url") {
					continue
				}
			}
			if i+1 >= len(args) || known(unwrap(args[i+1])) {
				p.Error = "启动选项缺少参数"
				continue
			}
			i++
			value := unwrap(args[i])
			switch low {
			case "-newtab":
				title = value
			case "-i":
				key = value
			default:
				if raw != "" {
					p.Error = "一次只接受一个连接"
				}
				raw = value
			}
		default:
			if strings.HasPrefix(low, "ssh://") || strings.HasPrefix(low, "sftp://") || strings.HasPrefix(low, "ftp://") {
				if raw != "" {
					p.Error = "一次只接受一个连接"
				}
				raw = a
			} else {
				p.Error = "包含不支持的启动参数"
			}
		}
	}
	if p.Error != "" {
		return p
	}
	if raw == "" {
		if title != "" || key != "" || p.NewWindow {
			p.Error = "没有提供连接URL"
		}
		return p
	}
	c, e := URL(raw)
	if e != nil {
		p.Error = e.Error()
		return p
	}
	c.Title = title
	c.KeyPath = key
	if p.Entry == "xftp" && c.Protocol != "ftp" {
		c.Protocol = "sftp"
	}
	p.Connection = &c
	return p
}
func (c Connection) Address() string { return net.JoinHostPort(c.Host, strconv.Itoa(c.Port)) }

// Report never serializes argv, endpoint, username, title, password or key path.
func (p Plan) Report() map[string]any {
	return map[string]any{"entry": p.Entry, "argumentCount": p.Count, "diagnose": p.Diagnose, "newWindow": p.NewWindow, "recognized": p.Error == "", "message": p.Error, "networkRequested": p.Connection != nil && !p.Diagnose && p.Error == ""}
}
