package desktop

import (
	"forterm/native/internal/settings"
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/container"
	"fyne.io/fyne/v2/layout"
	"fyne.io/fyne/v2/theme"
	"fyne.io/fyne/v2/widget"
	"slices"
	"sort"
	"strings"
)

func (a *App) profileSection() fyne.CanvasObject {
	a.rebuildProfileNodes()
	a.profileTree = widget.NewTree(func(uid widget.TreeNodeID) []widget.TreeNodeID { return slices.Clone(a.profileNodes.children[uid]) }, func(uid widget.TreeNodeID) bool { return uid == "" || strings.HasPrefix(uid, "g:") }, func(branch bool) fyne.CanvasObject {
		return newProfileTreeCell(branch)
	}, func(uid widget.TreeNodeID, branch bool, obj fyne.CanvasObject) {
		text := ""
		if branch {
			text = strings.TrimPrefix(uid, "g:")
		} else if p, ok := a.profileNodes.profiles[uid]; ok {
			text = profileName(p)
		}
		obj.(*profileTreeCell).set(text, branch)
	})
	a.profileTree.HideSeparators = true
	a.profileTree.OnSelected = func(uid widget.TreeNodeID) {
		if strings.HasPrefix(uid, "g:") {
			a.ShowLibrary()
			a.connections.selectGroup(strings.TrimPrefix(uid, "g:"))
		} else if strings.HasPrefix(uid, "p:") {
			a.ShowLibrary()
			a.connections.selectProfile(strings.TrimPrefix(uid, "p:"))
		}
	}
	a.profileTree.OnBranchOpened = func(uid widget.TreeNodeID) {
		if !a.restoringProfileBranches {
			a.UI.Preferences().SetBool("group-open-"+uid, true)
		}
	}
	a.profileTree.OnBranchClosed = func(uid widget.TreeNodeID) {
		if !a.restoringProfileBranches {
			a.UI.Preferences().SetBool("group-open-"+uid, false)
		}
	}
	heading := container.NewHBox(action("所有连接", theme.FolderOpenIcon(), func() { a.ShowLibrary(); a.connections.selectGroup("") }), layout.NewSpacer(), action("", theme.ContentAddIcon(), func() { a.ProfileDialog(nil, a.refreshProfileTree) }), action("", theme.MoreHorizontalIcon(), a.ShowLibrary))
	a.refreshProfileTree()
	return container.NewBorder(heading, nil, nil, nil, a.profileTree)
}
func (a *App) refreshProfileTree() {
	if a.connections != nil {
		a.connections.reload()
	}
	if a.profileTree == nil {
		return
	}
	a.rebuildProfileNodes()
	a.restoringProfileBranches = true
	defer func() { a.restoringProfileBranches = false }()
	a.profileTree.Refresh()
	for _, uid := range a.profileNodes.children[""] {
		if a.navigationQuery != "" || a.UI.Preferences().BoolWithFallback("group-open-"+uid, false) {
			a.profileTree.OpenBranch(uid)
		} else {
			a.profileTree.CloseBranch(uid)
		}
	}
}

// Cache immutable metadata once per explicit library/search refresh. Tree row
// rendering and layout must never serialise or clone the whole configuration.
type profileTreeData struct {
	children map[string][]string
	profiles map[string]settings.Profile
}

func buildProfileNodes(profiles []settings.Profile, query string) profileTreeData {
	nodes := profileTreeData{children: map[string][]string{}, profiles: map[string]settings.Profile{}}
	groups := map[string]bool{}
	for _, p := range profiles {
		group := p.Group
		if group == "" {
			group = "默认分组"
		}
		if query != "" && !containsFold(p.Title+" "+p.Host+" "+p.Username+" "+group, query) {
			continue
		}
		uid := "g:" + group
		if !groups[uid] {
			groups[uid] = true
			nodes.children[""] = append(nodes.children[""], uid)
		}
		leaf := "p:" + p.ID
		nodes.children[uid] = append(nodes.children[uid], leaf)
		if _, exists := nodes.profiles[leaf]; !exists {
			nodes.profiles[leaf] = p
		}
	}
	sort.Strings(nodes.children[""])
	return nodes
}
func (a *App) rebuildProfileNodes() {
	if a.Store == nil {
		a.profileNodes = buildProfileNodes(nil, a.navigationQuery)
		return
	}
	a.profileNodes = buildProfileNodes(a.Store.ViewSnapshot().Profiles, a.navigationQuery)
}
