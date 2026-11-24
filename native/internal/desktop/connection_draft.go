package desktop

import (
	"errors"
	"forterm/native/internal/launch"
	"forterm/native/internal/settings"
)

type draftSecrets struct {
	password, passphrase                                                          string
	providedPassword, providedPhrase, inheritPassword, inheritPhrase, requireSame bool
}

func (s *draftSecrets) clear() { s.password = ""; s.passphrase = "" }
func (a *App) prepareConnectionDraft(profile settings.Profile, secrets draftSecrets) ([]launch.Connection, error) {
	rows, err := a.Store.DraftLogin(profile, settings.DraftLoginOptions{Password: secrets.inheritPassword, Passphrase: secrets.inheritPhrase, RequireUnchanged: secrets.requireSame})
	if err != nil {
		return nil, err
	}
	defer rows.Clear()
	result := make([]launch.Connection, len(rows))
	for i, row := range rows {
		p := row.Profile
		c := launch.Connection{Protocol: p.Protocol, Host: p.Host, Port: p.Port, Username: p.Username, Title: p.Title, AuthMethod: p.Auth, KeyPath: p.KeyPath, ProfileID: p.ID}
		if p.HasPassword {
			if row.PasswordError != nil {
				clearPrepared(result)
				return nil, errors.New("系统保存的密码读取失败；未自动改为重复询问，请重新保存或填写本次密码")
			}
			c.Password = string(row.Password)
			c.HasPassword = true
		}
		if p.HasPassphrase {
			if row.PassphraseError != nil {
				clearPrepared(result)
				return nil, errors.New("系统保存的私钥口令不可用，请填写本次口令")
			}
			c.Passphrase = string(row.Passphrase)
		}
		result[i] = c
	}
	if secrets.providedPassword {
		result[0].Password = secrets.password
		result[0].HasPassword = true
	}
	if secrets.providedPhrase {
		result[0].Passphrase = secrets.passphrase
	}
	return result, nil
}
func clearPrepared(rows []launch.Connection) {
	for i := range rows {
		rows[i].Password = ""
		rows[i].Passphrase = ""
		rows[i].Jumps = nil
	}
}
func assembledConnection(rows []launch.Connection) launch.Connection {
	destination := rows[0]
	for i := len(rows) - 1; i >= 1; i-- {
		destination.Jumps = append(destination.Jumps, rows[i])
	}
	return destination
}
