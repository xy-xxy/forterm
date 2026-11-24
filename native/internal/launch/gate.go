package launch

import (
	"crypto/hmac"
	"crypto/rand"
	"crypto/sha256"
	"encoding/json"
	"sync"
	"time"
)

type Gate struct {
	mu   sync.Mutex
	key  [32]byte
	seen map[[32]byte]time.Time
}

func NewGate() *Gate {
	g := &Gate{seen: map[[32]byte]time.Time{}}
	if _, e := rand.Read(g.key[:]); e != nil {
		panic("随机源不可用")
	}
	return g
}
func (g *Gate) Accept(p Plan, now time.Time) bool {
	if p.NewWindow || p.Error != "" || p.Connection == nil {
		return true
	}
	c := p.Connection
	body, _ := json.Marshal([]any{p.Diagnose, p.Entry, c.Protocol, c.Host, c.Port, c.Username, c.Password, c.HasPassword, c.KeyPath})
	mac := hmac.New(sha256.New, g.key[:])
	mac.Write(body)
	var key [32]byte
	copy(key[:], mac.Sum(nil))
	g.mu.Lock()
	defer g.mu.Unlock()
	for k, v := range g.seen {
		if now.Sub(v) > 3*time.Second {
			delete(g.seen, k)
		}
	}
	if _, ok := g.seen[key]; ok {
		return false
	}
	g.seen[key] = now
	return true
}
