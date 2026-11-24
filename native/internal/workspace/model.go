// Package workspace owns explicit source associations. It never matches by IP
// or retries authentication. All methods are called from the UI event loop.
package workspace

import "errors"

type Session struct {
	ID, Title, Protocol string
	Open, Online        bool
}
type Group struct {
	Sources []string
	Active  string
}
type Model struct {
	Sessions []Session
	Groups   map[string]Group
}

func New() *Model { return &Model{Groups: map[string]Group{}} }
func (m *Model) Get(id string) (Session, bool) {
	for _, s := range m.Sessions {
		if s.ID == id {
			return s, true
		}
	}
	return Session{}, false
}
func (m *Model) Add(s Session) {
	if _, ok := m.Get(s.ID); ok {
		return
	}
	m.Sessions = append(m.Sessions, s)
	m.Groups[s.ID] = Group{Sources: []string{s.ID}, Active: s.ID}
}
func (m *Model) SetOnline(id string, online bool) {
	for i, s := range m.Sessions {
		if s.ID == id {
			m.Sessions[i].Online = online
			return
		}
	}
}
func (m *Model) Attach(owner, source string) error {
	a, ok := m.Get(owner)
	if !ok || !a.Open {
		return errors.New("所属工作区已关闭")
	}
	s, ok := m.Get(source)
	if !ok || !s.Open || s.Protocol == "local" {
		return errors.New("文件来源不可用，请明确选择已打开的远程会话")
	}
	g := m.Groups[owner]
	found := false
	for _, id := range g.Sources {
		if id == source {
			found = true
		}
	}
	if !found {
		g.Sources = append(g.Sources, source)
	}
	g.Active = source
	m.Groups[owner] = g
	return nil
}
func (m *Model) Select(owner, source string) error {
	g := m.Groups[owner]
	for _, id := range g.Sources {
		if id == source {
			g.Active = source
			m.Groups[owner] = g
			return nil
		}
	}
	return errors.New("请先明确关联文件来源")
}
func (m *Model) Detach(owner, source string) {
	if owner == source {
		return
	}
	g := m.Groups[owner]
	next := make([]string, 0, len(g.Sources))
	for _, id := range g.Sources {
		if id != source {
			next = append(next, id)
		}
	}
	g.Sources = next
	if g.Active == source {
		if len(next) > 0 {
			g.Active = next[len(next)-1]
		} else {
			g.Active = ""
		}
	}
	m.Groups[owner] = g
}
func (m *Model) Close(id string) {
	for i, s := range m.Sessions {
		if s.ID == id {
			m.Sessions[i].Open = false
			m.Sessions[i].Online = false
			break
		}
	}
	delete(m.Groups, id)
}
func (m *Model) Referencing(source string) []string {
	out := []string{}
	for _, s := range m.Sessions {
		if !s.Open {
			continue
		}
		for _, id := range m.Groups[s.ID].Sources {
			if id == source && s.ID != source {
				out = append(out, s.ID)
				break
			}
		}
	}
	return out
}
func fileProtocol(protocol string) bool { return protocol == "sftp" || protocol == "ftp" }
func (m *Model) Visible() []Session {
	hidden := map[string]bool{}
	for _, s := range m.Sessions {
		if !s.Open || !fileProtocol(s.Protocol) {
			continue
		}
		if len(m.Referencing(s.ID)) > 0 {
			hidden[s.ID] = true
		}
	}
	// Cyclic file-only associations must retain a reachable top-level workspace.
	for _, s := range m.Sessions {
		if !hidden[s.ID] {
			continue
		}
		seen := map[string]bool{}
		queue := m.Referencing(s.ID)
		reachable := false
		for len(queue) > 0 {
			owner := queue[0]
			queue = queue[1:]
			if seen[owner] {
				continue
			}
			seen[owner] = true
			if !hidden[owner] {
				reachable = true
				break
			}
			queue = append(queue, m.Referencing(owner)...)
		}
		if !reachable {
			delete(hidden, s.ID)
		}
	}
	out := []Session{}
	for _, s := range m.Sessions {
		if s.Open && !hidden[s.ID] {
			out = append(out, s)
		}
	}
	return out
}
func (m *Model) OwnerFor(source string) string {
	visible := map[string]bool{}
	for _, s := range m.Visible() {
		visible[s.ID] = true
	}
	queue := []string{source}
	seen := map[string]bool{}
	for len(queue) > 0 {
		id := queue[0]
		queue = queue[1:]
		if seen[id] {
			continue
		}
		seen[id] = true
		if visible[id] {
			return id
		}
		queue = append(queue, m.Referencing(id)...)
	}
	return ""
}
