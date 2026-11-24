package settings

import (
	"errors"
	"forterm/native/internal/vault"
)

type mutation struct {
	key     string
	update  SecretUpdate
	before  []byte
	present bool
}

func (s *Store) withSecrets(changes []mutation, commit func() error) error {
	active := []mutation{}
	for _, change := range changes {
		switch change.update.Action {
		case "", "keep":
			continue
		case "replace", "clear":
		default:
			return errors.New("凭据操作无效")
		}
		if s.vault == nil {
			return errors.New("系统安全存储不可用，未回退明文")
		}
		old, e := s.vault.Get(change.key)
		if e != nil && !errors.Is(e, vault.ErrNotFound) {
			return e
		}
		change.before = old
		change.present = e == nil
		active = append(active, change)
	}
	defer func() {
		for _, change := range active {
			for i := range change.before {
				change.before[i] = 0
			}
		}
	}()
	applied := 0
	rollback := func() bool {
		ok := true
		for i := applied - 1; i >= 0; i-- {
			change := active[i]
			var e error
			if change.present {
				e = s.vault.Set(change.key, change.before)
			} else {
				e = s.vault.Delete(change.key)
			}
			if e != nil {
				ok = false
			}
		}
		return ok
	}
	for _, change := range active {
		var e error
		if change.update.Action == "replace" {
			e = s.vault.Set(change.key, change.update.Data)
		} else {
			e = s.vault.Delete(change.key)
		}
		if e != nil {
			if !rollback() {
				return errors.New("凭据保存失败，回滚未确认，请核对系统凭据状态")
			}
			return e
		}
		applied++
	}
	if e := commit(); e != nil {
		if !rollback() {
			return errors.New("配置未提交，系统凭据回滚未确认，请核对保存状态")
		}
		return e
	}
	return nil
}
func presence(previous bool, action string) bool {
	switch action {
	case "replace":
		return true
	case "clear":
		return false
	}
	return previous
}
