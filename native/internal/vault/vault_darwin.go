//go:build darwin

package vault

import (
	"errors"
	keychain "github.com/keybase/go-keychain"
)

type nativeVault struct{}

func New() Vault { return nativeVault{} }
func (nativeVault) Get(key string) ([]byte, error) {
	if !validKey(key) {
		return nil, errors.New("凭据标识无效")
	}
	query := keychain.NewItem()
	query.SetSecClass(keychain.SecClassGenericPassword)
	query.SetService(service)
	query.SetAccount(key)
	query.SetMatchLimit(keychain.MatchLimitOne)
	query.SetReturnData(true)
	results, e := keychain.QueryItem(query)
	if e == keychain.ErrorItemNotFound {
		return nil, ErrNotFound
	}
	if e != nil {
		return nil, errors.New("系统钥匙串不可用或访问未获允许")
	}
	if len(results) == 0 {
		return nil, ErrNotFound
	}
	if len(results) != 1 {
		return nil, errors.New("系统钥匙串返回重复项，未使用凭据")
	}
	return append([]byte(nil), results[0].Data...), nil
}
func (nativeVault) Set(key string, data []byte) error {
	if !validKey(key) || len(data) > 65536 {
		return errors.New("凭据内容或标识超出限制")
	}
	item := keychain.NewGenericPassword(service, key, "Forterm Native saved credential", data, "")
	item.SetSynchronizable(keychain.SynchronizableNo)
	item.SetAccessible(keychain.AccessibleWhenUnlocked)
	err := keychain.AddItem(item)
	if err == keychain.ErrorDuplicateItem {
		query := keychain.NewItem()
		query.SetSecClass(keychain.SecClassGenericPassword)
		query.SetService(service)
		query.SetAccount(key)
		update := keychain.NewItem()
		update.SetData(data)
		err = keychain.UpdateItem(query, update)
	}
	if err != nil {
		return errors.New("系统钥匙串保存失败，未回退明文")
	}
	return nil
}
func (nativeVault) Delete(key string) error {
	if !validKey(key) {
		return errors.New("凭据标识无效")
	}
	err := keychain.DeleteGenericPasswordItem(service, key)
	if err == keychain.ErrorItemNotFound {
		return nil
	}
	if err != nil {
		return errors.New("系统钥匙串删除未获确认")
	}
	return nil
}
