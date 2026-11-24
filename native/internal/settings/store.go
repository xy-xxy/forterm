// Package settings persists non-secret metadata. Passwords, key passphrases and
// TOTP seeds are stored separately in the system vault, never in JSON exports.
package settings

import (
	"bytes"
	"crypto/rand"
	"encoding/hex"
	"encoding/json"
	"errors"
	"forterm/native/internal/vault"
	"io"
	"maps"
	"net/url"
	"os"
	"path/filepath"
	"slices"
	"strings"
	"sync"
	"sync/atomic"
)

type KeepAlive struct {
	Enabled         bool `json:"enabled"`
	IntervalSeconds int  `json:"intervalSeconds"`
	SFTPProbe       bool `json:"sftpProbe"`
}
type Profile struct {
	ID            string `json:"id"`
	Title         string `json:"title"`
	Group         string `json:"group"`
	Protocol      string `json:"protocol"`
	Host          string `json:"host"`
	Port          int    `json:"port"`
	Username      string `json:"username"`
	Auth          string `json:"auth,omitempty"`
	KeyPath       string `json:"keyPath,omitempty"`
	JumpID        string `json:"jumpId,omitempty"`
	HasPassword   bool   `json:"hasPassword,omitempty"`
	HasPassphrase bool   `json:"hasPassphrase,omitempty"`
}
type TotpParameters struct {
	Algorithm string `json:"algorithm"`
	Digits    int    `json:"digits"`
	Period    int    `json:"period"`
}
type Account struct {
	ID          string         `json:"id"`
	Name        string         `json:"name"`
	PortalURL   string         `json:"portalUrl"`
	Username    string         `json:"username"`
	Totp        TotpParameters `json:"totp"`
	TotpEnabled bool           `json:"totpEnabled"`
	HasPassword bool           `json:"hasPassword,omitempty"`
	HasSeed     bool           `json:"hasTotpSecret,omitempty"`
}
type Data struct {
	Version    int               `json:"version"`
	Profiles   []Profile         `json:"profiles"`
	Accounts   []Account         `json:"bastionAccounts,omitempty"`
	KnownHosts map[string]string `json:"knownHosts,omitempty"`
	KeepAlive  KeepAlive         `json:"keepAlive"`
}
type SecretUpdate struct {
	Action string
	Data   []byte
}
type Store struct {
	published atomic.Pointer[Data]
	mu        sync.Mutex
	data      Data
	path      string
	vault     vault.Vault
	loadError error
}

func ID() string {
	var nonce [16]byte
	if _, e := rand.Read(nonce[:]); e != nil {
		panic("系统随机源不可用")
	}
	return hex.EncodeToString(nonce[:])
}
func defaults() Data {
	return Data{Version: 1, Profiles: []Profile{}, Accounts: []Account{}, KnownHosts: map[string]string{}, KeepAlive: KeepAlive{Enabled: true, IntervalSeconds: 15, SFTPProbe: true}}
}
func Open(directory string, secrets vault.Vault) *Store {
	s := &Store{data: defaults(), path: filepath.Join(directory, "config.json"), vault: secrets}
	defer s.publishView()
	raw, e := os.ReadFile(s.path)
	if os.IsNotExist(e) {
		return s
	}
	if e != nil || len(raw) > 2<<20 {
		s.loadError = errors.New("配置读取失败，原文件保持不变")
		return s
	}
	var data Data
	if e = json.Unmarshal(raw, &data); e != nil || data.Version != 1 {
		s.loadError = errors.New("原生配置格式无效，未覆盖原文件")
		return s
	}
	if data.KnownHosts == nil {
		data.KnownHosts = map[string]string{}
	}
	if data.KeepAlive.IntervalSeconds < 5 || data.KeepAlive.IntervalSeconds > 600 {
		data.KeepAlive = defaults().KeepAlive
	}
	s.data = data
	return s
}
func (s *Store) Error() error   { s.mu.Lock(); defer s.mu.Unlock(); return s.loadError }
func (s *Store) Snapshot() Data { s.mu.Lock(); defer s.mu.Unlock(); return clone(s.data) }

// ViewSnapshot is only for displaying committed metadata. It never waits for
// credential-store or disk IO. Authentication must use LoginChain instead.
func (s *Store) ViewSnapshot() Data {
	if data := s.published.Load(); data != nil {
		return clone(*data)
	}
	return s.Snapshot()
}
func (s *Store) publishView() { data := s.data; s.published.Store(&data) }
func clone(d Data) Data {
	copy := d
	copy.Profiles = slices.Clone(d.Profiles)
	copy.Accounts = slices.Clone(d.Accounts)
	copy.KnownHosts = maps.Clone(d.KnownHosts)
	if copy.KnownHosts == nil {
		copy.KnownHosts = map[string]string{}
	}
	return copy
}

func (s *Store) commit(next Data) error {
	if s.loadError != nil {
		return s.loadError
	}
	directory := filepath.Dir(s.path)
	if e := os.MkdirAll(directory, 0700); e != nil {
		return errors.New("无法创建原生配置目录")
	}
	raw, e := json.MarshalIndent(next, "", "  ")
	if e != nil {
		return e
	}
	temp, e := os.CreateTemp(directory, ".forterm-config-*.tmp")
	if e != nil {
		return errors.New("配置写入失败，原文件保持不变")
	}
	name := temp.Name()
	defer os.Remove(name)
	_, e = temp.Write(raw)
	if e == nil {
		e = temp.Sync()
	}
	ce := temp.Close()
	if e != nil || ce != nil {
		return errors.New("配置写入未完成，原文件保持不变")
	}
	if e = os.Rename(name, s.path); e != nil {
		return errors.New("配置替换失败，原文件保持不变")
	}
	s.data = next
	s.publishView()
	return nil
}
func ValidateProfile(p Profile) error {
	if p.Protocol != "ssh" && p.Protocol != "sftp" && p.Protocol != "ftp" {
		return errors.New("不支持的连接协议")
	}
	if p.Host == "" || len(p.Host) > 255 || strings.ContainsAny(p.Host, " \t\r\n/\\\x00") || p.Port < 1 || p.Port > 65535 || p.Username == "" || len(p.Username) > 1024 || strings.ContainsAny(p.Username, "\r\n\x00") || len(p.Title) > 256 || len(p.Group) > 128 {
		return errors.New("连接主机、端口或用户格式无效")
	}
	if p.Auth == "" {
		p.Auth = "password"
	}
	if p.Auth != "password" && p.Auth != "key" && p.Auth != "agent" && p.Auth != "keyboard" {
		return errors.New("不支持的认证方式")
	}
	if p.Auth == "key" && p.KeyPath == "" {
		return errors.New("请选择私钥文件")
	}
	if p.Protocol == "ftp" && (p.Auth != "password" || p.KeyPath != "" || p.JumpID != "") {
		return errors.New("FTP仅支持独立用户名/密码，不支持SSH跳板或私钥")
	}
	return nil
}
func validateChain(profiles []Profile) error {
	byID := map[string]Profile{}
	for _, p := range profiles {
		if _, ok := byID[p.ID]; ok {
			return errors.New("连接标识重复")
		}
		byID[p.ID] = p
	}
	for _, p := range profiles {
		seen := map[string]bool{}
		for p.JumpID != "" {
			if seen[p.ID] || len(seen) >= 8 {
				return errors.New("跳板链循环或超过8层")
			}
			seen[p.ID] = true
			next, ok := byID[p.JumpID]
			if !ok || next.Protocol == "ftp" {
				return errors.New("跳板连接不存在或不是SSH类型")
			}
			p = next
		}
	}
	return nil
}
func (s *Store) UpsertProfile(p Profile, password, passphrase SecretUpdate, temporary bool) (Profile, error) {
	if temporary {
		return Profile{}, errors.New("临时票据会话不能保存到连接库")
	}
	if p.Auth == "" {
		p.Auth = "password"
	}
	if e := ValidateProfile(p); e != nil {
		return Profile{}, e
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.loadError != nil {
		return Profile{}, s.loadError
	}
	next := clone(s.data)
	var old *Profile
	index := -1
	for i := range next.Profiles {
		if next.Profiles[i].ID == p.ID && p.ID != "" {
			copy := next.Profiles[i]
			old = &copy
			index = i
			break
		}
	}
	if p.ID == "" {
		p.HasPassword = false
		p.HasPassphrase = false
		p.ID = ID()
	} else if old == nil {
		return Profile{}, errors.New("连接配置已不存在")
	}
	if old != nil {
		p.HasPassword, p.HasPassphrase = old.HasPassword, old.HasPassphrase
		if old.Host != p.Host || old.Port != p.Port || old.Username != p.Username || old.Protocol != p.Protocol {
			if password.Action == "keep" || password.Action == "" {
				password.Action = "clear"
			}
			if passphrase.Action == "keep" || passphrase.Action == "" {
				passphrase.Action = "clear"
			}
		}
	}
	if index >= 0 {
		next.Profiles[index] = p
	} else {
		next.Profiles = append(next.Profiles, p)
	}
	if e := validateChain(next.Profiles); e != nil {
		return Profile{}, e
	}
	p.HasPassword = presence(p.HasPassword, password.Action)
	p.HasPassphrase = presence(p.HasPassphrase, passphrase.Action)
	if index >= 0 {
		next.Profiles[index] = p
	} else {
		next.Profiles[len(next.Profiles)-1] = p
	}
	err := s.withSecrets([]mutation{{key: "profile/" + p.ID + "/password", update: password}, {key: "profile/" + p.ID + "/passphrase", update: passphrase}}, func() error { return s.commit(next) })
	if err != nil {
		return Profile{}, err
	}

	return p, nil
}
func (s *Store) applySecret(key string, update SecretUpdate, present *bool) error {
	switch update.Action {
	case "", "keep":
		return nil
	case "replace":
		if s.vault == nil {
			return errors.New("系统安全存储不可用，未保存凭据")
		}
		if e := s.vault.Set(key, update.Data); e != nil {
			return e
		}
		*present = true
		return nil
	case "clear":
		if s.vault != nil {
			if e := s.vault.Delete(key); e != nil {
				return e
			}
		}
		*present = false
		return nil
	default:
		return errors.New("凭据操作无效")
	}
}
func (s *Store) Secret(id, kind string) ([]byte, error) {
	if s.vault == nil {
		return nil, errors.New("系统安全存储不可用")
	}
	return s.vault.Get("profile/" + id + "/" + kind)
}
func (s *Store) DeleteProfile(id string) error {
	s.mu.Lock()
	defer s.mu.Unlock()
	next := clone(s.data)
	for _, p := range next.Profiles {
		if p.JumpID == id {
			return errors.New("该连接被其他跳板链引用，请先修改关联")
		}
	}
	items := next.Profiles[:0]
	for _, p := range next.Profiles {
		if p.ID != id {
			items = append(items, p)
		}
	}
	next.Profiles = items
	if e := s.commit(next); e != nil {
		return e
	}
	for _, kind := range []string{"password", "passphrase"} {
		if s.vault != nil {
			if e := s.vault.Delete("profile/" + id + "/" + kind); e != nil {
				return errors.New("连接已移除，系统凭据清理未获确认")
			}
		}
	}
	return nil
}
func (s *Store) TrustHost(endpoint, fingerprint string) error {
	s.mu.Lock()
	defer s.mu.Unlock()
	next := clone(s.data)
	next.KnownHosts[endpoint] = fingerprint
	return s.commit(next)
}
func (s *Store) SetKeepAlive(value KeepAlive) error {
	if value.IntervalSeconds < 5 || value.IntervalSeconds > 600 {
		return errors.New("心跳间隔须为5–600秒")
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	next := clone(s.data)
	next.KeepAlive = value
	return s.commit(next)
}
func (s *Store) Export() ([]byte, error) {
	d := s.Snapshot()
	for i := range d.Profiles {
		d.Profiles[i].HasPassword = false
		d.Profiles[i].HasPassphrase = false
	}
	for i := range d.Accounts {
		d.Accounts[i].HasPassword = false
		d.Accounts[i].HasSeed = false
		d.Accounts[i].TotpEnabled = false
	}
	d.KnownHosts = nil
	return json.MarshalIndent(d, "", "  ")
}
func (s *Store) Import(reader io.Reader) error {
	raw, e := io.ReadAll(io.LimitReader(reader, (2<<20)+1))
	if e != nil || len(raw) > 2<<20 {
		return errors.New("导入文件超过2MiB或无法读取")
	}
	var check any
	if json.Unmarshal(raw, &check) != nil {
		return errors.New("导入JSON无效")
	}
	if containsSecretField(check) {
		return errors.New("导入文件含密码或密钥字段，请使用无秘密导出文件")
	}
	var d Data
	decoder := json.NewDecoder(bytes.NewReader(raw))
	if e = decoder.Decode(&d); e != nil || d.Version != 1 || len(d.Profiles) > 1000 || len(d.Accounts) > 1000 {
		return errors.New("需要Forterm v1 JSON，最多1000个连接/账户")
	}
	ids := map[string]string{}
	for _, p := range d.Profiles {
		if _, ok := ids[p.ID]; ok {
			return errors.New("导入含重复连接标识")
		}
		ids[p.ID] = ID()
	}
	for i := range d.Profiles {
		p := &d.Profiles[i]
		if e = ValidateProfile(*p); e != nil {
			return e
		}
		old := p.ID
		p.ID = ids[old]
		p.HasPassword = false
		p.HasPassphrase = false
		if p.JumpID != "" {
			mapped, ok := ids[p.JumpID]
			if !ok {
				return errors.New("导入缺少跳板连接")
			}
			p.JumpID = mapped
		}
	}
	if e = validateChain(d.Profiles); e != nil {
		return e
	}
	for i := range d.Accounts {
		account := &d.Accounts[i]
		account.ID = ID()
		account.HasPassword = false
		account.HasSeed = false
		account.TotpEnabled = false
		if e = ValidateAccount(*account); e != nil {
			return e
		}
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	next := clone(s.data)
	next.Profiles = append(next.Profiles, d.Profiles...)
	next.Accounts = append(next.Accounts, d.Accounts...)
	return s.commit(next)
}
func containsSecretField(value any) bool {
	switch node := value.(type) {
	case map[string]any:
		for k, v := range node {
			key := strings.ToLower(k)
			switch key {
			case "password", "passphrase", "seed", "seedinput", "totpsecret", "encryptedpassword", "encryptedseed", "encryptedpassphrase":
				return true
			}
			if containsSecretField(v) {
				return true
			}
		}
	case []any:
		for _, v := range node {
			if containsSecretField(v) {
				return true
			}
		}
	}
	return false
}
func ValidateAccount(a Account) error {
	u, e := url.Parse(a.PortalURL)
	if e != nil || u.Hostname() == "" || (u.Scheme != "http" && u.Scheme != "https") || u.User != nil || u.RawQuery != "" || u.Fragment != "" || a.Name == "" || a.Username == "" {
		return errors.New("账户名称、入口地址或用户无效；入口不能含凭据/查询参数")
	}
	return nil
}
