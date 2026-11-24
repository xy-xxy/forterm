package settings

import (
	"encoding/base32"
	"errors"
)

func (s *Store) UpsertAccount(a Account, password, seed SecretUpdate) (Account, error) {
	if password.Action == "replace" && len(password.Data) == 0 {
		return Account{}, errors.New("请输入要保存的账户密码")
	}
	if e := ValidateAccount(a); e != nil {
		return Account{}, e
	}
	if seed.Action == "replace" {
		decoded, parameters, e := NormalizeTOTP(string(seed.Data), a.Totp)
		if e != nil {
			return Account{}, e
		}
		seed.Data = []byte(base32.StdEncoding.WithPadding(base32.NoPadding).EncodeToString(decoded))
		for i := range decoded {
			decoded[i] = 0
		}
		a.Totp = parameters
	}
	if a.Totp.Algorithm == "" {
		a.Totp = TotpParameters{Algorithm: "SHA1", Digits: 6, Period: 30}
	}
	if !validTOTP(a.Totp) {
		return Account{}, errors.New("验证器周期须为5–300秒，位数为6或8")
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.loadError != nil {
		return Account{}, s.loadError
	}
	next := clone(s.data)
	index := -1
	for i, old := range next.Accounts {
		if old.ID == a.ID && a.ID != "" {
			index = i
			a.HasPassword = old.HasPassword
			a.HasSeed = old.HasSeed
			if old.PortalURL != a.PortalURL || old.Username != a.Username {
				if password.Action == "" || password.Action == "keep" {
					password.Action = "clear"
				}
				if seed.Action == "" || seed.Action == "keep" {
					seed.Action = "clear"
				}
			}
			break
		}
	}
	if a.ID == "" {
		a.ID = ID()
		a.HasPassword = false
		a.HasSeed = false
	} else if index < 0 {
		return Account{}, errors.New("账户配置已不存在")
	}
	a.HasPassword = presence(a.HasPassword, password.Action)
	a.HasSeed = presence(a.HasSeed, seed.Action)
	if a.TotpEnabled && !a.HasSeed && seed.Action != "clear" {
		return Account{}, errors.New("请先保存验证器key")
	}
	if !a.HasSeed {
		a.TotpEnabled = false
	}
	if index < 0 {
		next.Accounts = append(next.Accounts, a)
	} else {
		next.Accounts[index] = a
	}
	if e := s.withSecrets([]mutation{{key: "account/" + a.ID + "/password", update: password}, {key: "account/" + a.ID + "/totp", update: seed}}, func() error { return s.commit(next) }); e != nil {
		return Account{}, e
	}
	return a, nil
}
func (s *Store) AccountSecret(id, kind string) ([]byte, error) {
	if s.vault == nil {
		return nil, errors.New("系统安全存储不可用")
	}
	return s.vault.Get("account/" + id + "/" + kind)
}
func (s *Store) DeleteAccount(id string) error {
	s.mu.Lock()
	defer s.mu.Unlock()
	next := clone(s.data)
	keep := next.Accounts[:0]
	for _, a := range next.Accounts {
		if a.ID != id {
			keep = append(keep, a)
		}
	}
	next.Accounts = keep
	if e := s.commit(next); e != nil {
		return e
	}
	if s.vault != nil {
		for _, kind := range []string{"password", "totp"} {
			if e := s.vault.Delete("account/" + id + "/" + kind); e != nil {
				return errors.New("账户已移除，但系统凭据清理未获确认")
			}
		}
	}
	return nil
}
