package settings

import "errors"

// LoginProfile is ephemeral. Sensitive fields are excluded from JSON and must
// be cleared by the caller after preparing a user-requested connection.
type LoginProfile struct {
	Profile                        Profile
	Password, Passphrase           []byte `json:"-"`
	PasswordError, PassphraseError error  `json:"-"`
}
type LoginProfiles []LoginProfile

func (items LoginProfiles) Clear() {
	for _, item := range items {
		clear(item.Password)
		clear(item.Passphrase)
	}
}

// LoginChain pairs each current realm with its credentials under the same
// transaction gate used by mutations. Call off the UI thread. No login occurs.
func (s *Store) LoginChain(id string) (LoginProfiles, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	byID := map[string]Profile{}
	for _, p := range s.data.Profiles {
		byID[p.ID] = p
	}
	profile, ok := byID[id]
	if !ok {
		return nil, errors.New("连接配置已不存在")
	}
	profiles := []Profile{profile}
	seen := map[string]bool{id: true}
	for profiles[len(profiles)-1].JumpID != "" {
		next, ok := byID[profiles[len(profiles)-1].JumpID]
		if !ok || seen[next.ID] || len(profiles) >= 9 {
			return nil, errors.New("跳板配置无效，请检查跳板链")
		}
		profiles = append(profiles, next)
		seen[next.ID] = true
	}
	return s.readLoginProfiles(profiles), nil
}

func (s *Store) readLoginProfiles(profiles []Profile) LoginProfiles {
	result := make(LoginProfiles, len(profiles))
	for i, p := range profiles {
		result[i].Profile = p
		if p.HasPassword {
			if s.vault == nil {
				result[i].PasswordError = errors.New("系统安全存储不可用")
			} else {
				result[i].Password, result[i].PasswordError = s.vault.Get("profile/" + p.ID + "/password")
			}
		}
		if p.HasPassphrase {
			if s.vault == nil {
				result[i].PassphraseError = errors.New("系统安全存储不可用")
			} else {
				result[i].Passphrase, result[i].PassphraseError = s.vault.Get("profile/" + p.ID + "/passphrase")
			}
		}
	}
	return result
}

// AccountWithSecret reads a current account realm and one secret atomically.
// The caller owns the returned bytes and must clear them after use.
func (s *Store) AccountWithSecret(id, kind string) (Account, []byte, error) {
	if kind != "totp" && kind != "password" {
		return Account{}, nil, errors.New("不支持的账户凭据类型")
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	for _, account := range s.data.Accounts {
		if account.ID == id {
			if s.vault == nil {
				return Account{}, nil, errors.New("系统安全存储不可用")
			}
			raw, e := s.vault.Get("account/" + id + "/" + kind)
			if e != nil {
				clear(raw)
				raw = nil
			}
			return account, raw, e
		}
	}
	return Account{}, nil, errors.New("账户配置已不存在")
}

// DraftLogin reads the selected saved jump chain and optional matching saved
// root secrets atomically. The draft is never persisted; call off the UI thread.
type DraftLoginOptions struct{ Password, Passphrase, RequireUnchanged bool }

func (s *Store) DraftLogin(draft Profile, opt DraftLoginOptions) (LoginProfiles, error) {
	if err := ValidateProfile(draft); err != nil {
		return nil, err
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	byID := map[string]Profile{}
	for _, p := range s.data.Profiles {
		byID[p.ID] = p
	}
	original, found := byID[draft.ID]
	if opt.RequireUnchanged && (!found || !sameLoginRealm(draft, original) || draft.JumpID != original.JumpID) {
		return nil, errors.New("连接配置已更改，请重新确认表单")
	}
	if (opt.Password || opt.Passphrase) && (!found || !sameLoginRealm(draft, original)) {
		return nil, errors.New("认证目标已更改，请填写本次密码或私钥口令")
	}
	draft.HasPassword = found && opt.Password && original.HasPassword
	draft.HasPassphrase = found && opt.Passphrase && original.HasPassphrase
	profiles := []Profile{draft}
	seen := map[string]bool{}
	if draft.ID != "" {
		seen[draft.ID] = true
	}
	for profiles[len(profiles)-1].JumpID != "" {
		id := profiles[len(profiles)-1].JumpID
		p, ok := byID[id]
		if !ok || seen[id] || len(profiles) >= 9 || p.Protocol == "ftp" {
			return nil, errors.New("跳板链无效，请检查保存的跳板")
		}
		profiles = append(profiles, p)
		seen[id] = true
	}
	return s.readLoginProfiles(profiles), nil
}
func sameLoginRealm(a, b Profile) bool {
	return a.Protocol == b.Protocol && a.Host == b.Host && a.Port == b.Port && a.Username == b.Username && normalizedAuth(a.Auth) == normalizedAuth(b.Auth) && a.KeyPath == b.KeyPath
}

func normalizedAuth(s string) string {
	if s == "" {
		return "password"
	}
	return s
}
