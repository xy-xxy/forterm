package desktop

import (
	"context"
	"fmt"
	"forterm/native/internal/fileops"
	"forterm/native/internal/remotefs"
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/container"
	"fyne.io/fyne/v2/theme"
	"fyne.io/fyne/v2/widget"
	"os"
	"path"
	"sort"
	"strings"
	"time"
)

type remoteTreeNode struct {
	entry           remotefs.Entry
	children        []string
	loaded, loading bool
	err             string
	generation      uint64
	parent          string
}
type remoteFileTree struct {
	currentDirectory string
	workspace        *Workspace
	view             *widget.Tree
	root, selected   string
	nodes            map[string]*remoteTreeNode
	expanded         map[string]bool
	epoch            uint64
	active           int
	pending          []string
}

func newRemoteFileTree(w *Workspace) *remoteFileTree {
	t := &remoteFileTree{workspace: w, nodes: map[string]*remoteTreeNode{}, expanded: map[string]bool{}}
	t.view = widget.NewTree(t.children, func(id string) bool {
		if id == "" {
			return true
		}
		n := t.nodes[id]
		return n != nil && n.entry.Directory && n.entry.Mode&os.ModeSymlink == 0
	}, func(bool) fyne.CanvasObject { return newRemoteTreeRow(t) }, func(id string, _ bool, obj fyne.CanvasObject) { obj.(*remoteTreeRow).bind(id) })
	t.view.HideSeparators = true
	t.view.OnSelected = func(id string) {
		if strings.HasPrefix(id, "\x00error:") {
			parent := strings.TrimPrefix(id, "\x00error:")
			if n := t.nodes[parent]; n != nil {
				w.Status.SetText("目录读取失败：" + n.err)
			}
			t.load(parent, true)
			return
		}
		if t.nodes[id] != nil {
			t.selected = id
			w.selected = -1
		}
	}
	t.view.OnBranchOpened = func(id string) {
		t.expanded[id] = true
		if n := t.nodes[id]; n != nil && !n.loaded && !n.loading {
			t.load(id, false)
		}
	}
	t.view.OnBranchClosed = func(id string) { t.expanded[id] = false }
	return t
}
func (t *remoteFileTree) children(id string) []string {
	if id == "" {
		if t.root == "" {
			return nil
		}
		return []string{t.root}
	}
	n := t.nodes[id]
	if n == nil {
		return nil
	}
	if n.loading {
		return []string{"\x00loading:" + id}
	}
	if n.err != "" {
		return []string{"\x00error:" + id}
	}
	if !n.loaded {
		return []string{"\x00unloaded:" + id}
	}
	out := make([]string, 0, len(n.children))
	query := ""
	if t.workspace.fileFilter != nil {
		query = t.workspace.fileFilter.Text
	}
	for _, child := range n.children {
		v := t.nodes[child]
		if v == nil {
			continue
		}
		if !t.workspace.showHidden && strings.HasPrefix(v.entry.Name, ".") {
			continue
		}
		if query != "" && !v.entry.Directory && !containsFold(v.entry.Name, query) {
			continue
		}
		out = append(out, child)
	}
	return out
}
func (t *remoteFileTree) syncDirectory(directory string, entries []remotefs.Entry) {
	if !absoluteDirectory(directory) {
		return
	}
	navigating := directory != t.currentDirectory
	t.currentDirectory = directory
	if t.root == "" || (t.nodes[directory] == nil && directory != t.root) || !(directory == t.root || strings.HasPrefix(directory, strings.TrimSuffix(t.root, "/")+"/")) {
		t.epoch++
		t.nodes = map[string]*remoteTreeNode{}
		t.pending = nil
		t.root = directory
		t.selected = ""
	}
	n := t.nodes[directory]
	if n == nil {
		n = &remoteTreeNode{entry: remotefs.Entry{Name: path.Base(directory), Directory: true, Mode: os.ModeDir}, parent: path.Dir(directory)}
		t.nodes[directory] = n
	}
	t.install(directory, entries)
	if navigating {
		t.expanded[directory] = true
	}
	t.view.Refresh()
	if t.expanded[directory] {
		t.view.OpenBranch(directory)
	}
	for id, open := range t.expanded {
		if open && t.nodes[id] != nil {
			t.view.OpenBranch(id)
		}
	}
	if t.selected != "" && t.nodes[t.selected] != nil {
		t.view.Select(t.selected)
	}
}
func (t *remoteFileTree) install(directory string, entries []remotefs.Entry) {
	n := t.nodes[directory]
	if n == nil {
		return
	}
	n.generation++
	n.loading = false
	n.err = ""
	n.loaded = true
	previous := map[string]bool{}
	for _, id := range n.children {
		previous[id] = true
	}
	n.children = nil
	sort.SliceStable(entries, func(i, j int) bool {
		a, b := entries[i], entries[j]
		if a.Directory != b.Directory {
			return a.Directory
		}
		switch t.workspace.fileSort {
		case "size":
			return a.Size > b.Size
		case "modified":
			return a.Modified.After(b.Modified)
		}
		return strings.ToLower(a.Name) < strings.ToLower(b.Name)
	})
	for _, entry := range entries {
		if fileops.ChildName(entry.Name) != nil {
			n.err = "服务器返回不安全目录项，未展示"
			continue
		}
		id := path.Join(directory, entry.Name)
		delete(previous, id)
		existing := t.nodes[id]
		if existing == nil || existing.entry.Directory != entry.Directory || existing.entry.Mode&os.ModeSymlink != entry.Mode&os.ModeSymlink {
			existing = &remoteTreeNode{parent: directory}
			t.nodes[id] = existing
		}
		existing.entry = entry
		n.children = append(n.children, id)
	}
	for id := range previous {
		t.remove(id)
	}
}
func (t *remoteFileTree) remove(id string) {
	if n := t.nodes[id]; n != nil {
		for _, child := range n.children {
			t.remove(child)
		}
	}
	delete(t.nodes, id)
	delete(t.expanded, id)
	if t.selected == id {
		t.selected = ""
	}
}
func (t *remoteFileTree) load(id string, force bool) {
	n := t.nodes[id]
	w := t.workspace
	if n == nil || !n.entry.Directory || n.entry.Mode&os.ModeSymlink != 0 || n.loading || (!force && n.loaded) || !w.fileAvailable() {
		return
	}
	if len(t.pending) >= 128 {
		n.err = "等待目录过多，请收起部分目录后重试"
		t.view.Refresh()
		return
	}
	n.loading = true
	n.err = ""
	n.generation++
	t.pending = append(t.pending, id)
	t.view.Refresh()
	t.next()
}
func (t *remoteFileTree) next() {
	for t.active < 2 && len(t.pending) > 0 {
		id := t.pending[0]
		t.pending = t.pending[1:]
		n := t.nodes[id]
		if n == nil {
			continue
		}
		if !t.expanded[id] {
			n.loading = false
			continue
		}
		t.active++
		epoch, generation, conn := t.epoch, n.generation, t.workspace.FileConn
		go func() {
			ctx, cancel := context.WithTimeout(t.workspace.Context, 20*time.Second)
			defer cancel()
			entries, err := readTreeDirectory(ctx, conn, id)
			fyne.Do(func() {
				t.active--
				current := t.nodes[id]
				if epoch == t.epoch && current != nil && current.generation == generation && t.workspace.fileAvailable() && t.workspace.FileConn == conn {
					current.loading = false
					if err != nil {
						current.err = err.Error()
					} else {
						t.install(id, entries)
					}
					t.view.Refresh()
				}
				t.next()
			})
		}()
	}
}
func readTreeDirectory(ctx context.Context, fs remotefs.Connection, directory string) ([]remotefs.Entry, error) {
	if !absoluteDirectory(directory) {
		return nil, fmt.Errorf("不是安全绝对目录")
	}
	if directory != "/" {
		parts := strings.Split(strings.TrimPrefix(directory, "/"), "/")
		prefix := ""
		for _, part := range parts {
			if fileops.ChildName(part) != nil {
				return nil, fmt.Errorf("不安全目录段")
			}
			prefix += "/" + part
			info, err := fs.Stat(ctx, prefix)
			if err != nil {
				return nil, fmt.Errorf("目录边界核对失败：%w", err)
			}
			if !info.IsDir() || info.Mode()&os.ModeSymlink != 0 {
				return nil, fmt.Errorf("不展开符号链接或非目录路径")
			}
		}
	}
	entries, err := fs.List(ctx, directory)
	if err != nil {
		return nil, err
	}
	if len(entries) > 20000 {
		return nil, fmt.Errorf("单目录超过20000项，停止加载")
	}
	return entries, nil
}
func (w *Workspace) fileAvailable() bool {
	return w.FileConn != nil && !w.FileConn.Closed() && w.Context.Err() == nil && (w.source == nil || w.source.Context.Err() == nil)
}
func (w *Workspace) selectedRemoteEntry() (remotefs.Entry, string, bool) {
	if w.fileTree != nil && w.fileTree.selected != "" {
		if n := w.fileTree.nodes[w.fileTree.selected]; n != nil {
			return n.entry, w.fileTree.selected, true
		}
	}
	if w.selected >= 0 && w.selected < len(w.entries) {
		e := w.entries[w.selected]
		return e, path.Join(w.directory, e.Name), true
	}
	return remotefs.Entry{}, "", false
}

type remoteTreeRow struct {
	widget.BaseWidget
	tree         *remoteFileTree
	id           string
	icon         *widget.Icon
	name, detail *widget.Label
	content      *fyne.Container
}

func newRemoteTreeRow(t *remoteFileTree) *remoteTreeRow {
	r := &remoteTreeRow{tree: t, icon: widget.NewIcon(theme.DocumentIcon()), name: widget.NewLabel(""), detail: widget.NewLabel("")}
	r.name.Truncation = fyne.TextTruncateEllipsis
	r.detail.Importance = widget.LowImportance
	r.content = container.NewBorder(nil, nil, r.icon, r.detail, r.name)
	r.ExtendBaseWidget(r)
	return r
}
func (r *remoteTreeRow) CreateRenderer() fyne.WidgetRenderer {
	return widget.NewSimpleRenderer(r.content)
}
func (r *remoteTreeRow) bind(id string) {
	r.id = id
	r.detail.SetText("")
	if strings.HasPrefix(id, "\x00") {
		text := "未加载"
		if strings.HasPrefix(id, "\x00loading:") {
			text = "加载中…"
		}
		if strings.HasPrefix(id, "\x00error:") {
			parent := strings.TrimPrefix(id, "\x00error:")
			text = "读取失败，点击重试"
			if n := r.tree.nodes[parent]; n != nil {
				r.detail.SetText("!")
			}
		}
		r.name.SetText(text)
		r.icon.SetResource(theme.InfoIcon())
		return
	}
	n := r.tree.nodes[id]
	if n == nil {
		return
	}
	name := n.entry.Name
	if id == r.tree.root {
		name = id
	}
	r.name.SetText(name)
	if n.entry.Directory {
		r.icon.SetResource(theme.FolderIcon())
	} else {
		r.icon.SetResource(theme.DocumentIcon())
		r.detail.SetText(formatBytes(n.entry.Size))
	}
	if n.entry.Mode&os.ModeSymlink != 0 {
		r.detail.SetText("链接")
	}
}
func (r *remoteTreeRow) Tapped(*fyne.PointEvent) { r.tree.view.Select(r.id) }
func (r *remoteTreeRow) DoubleTapped(*fyne.PointEvent) {
	t := r.tree
	w := t.workspace
	t.view.Select(r.id)
	if n := t.nodes[r.id]; n != nil {
		if n.entry.Directory && n.entry.Mode&os.ModeSymlink == 0 {
			w.Path.SetText(r.id)
			w.Refresh()
		} else {
			w.OpenSelectedText()
		}
	}
}
func (r *remoteTreeRow) TappedSecondary(event *fyne.PointEvent) {
	t := r.tree
	w := t.workspace
	t.view.Select(r.id)
	if t.nodes[r.id] == nil {
		return
	}
	menu := w.fileContextMenu()
	widget.ShowPopUpMenuAtPosition(menu, w.Window.Canvas(), w.App.UI.Driver().AbsolutePositionForObject(r).Add(event.Position))
}
func (w *Workspace) fileContextMenu() *fyne.Menu {
	return fyne.NewMenu("文件", fyne.NewMenuItem("打开/在线编辑", w.OpenSelectedText), fyne.NewMenuItem("刷新所选目录", func() {
		if w.fileTree != nil {
			w.fileTree.load(w.fileTree.selected, true)
		}
	}), fyne.NewMenuItem("下载", w.Download), fyne.NewMenuItem("复制文件", func() { w.CopySelectedFile(false) }), fyne.NewMenuItem("剪切文件", func() { w.CopySelectedFile(true) }), fyne.NewMenuItem("粘贴到此目录…", w.PasteFile), fyne.NewMenuItem("复制到…", func() { w.CopyOrMoveTo(false) }), fyne.NewMenuItem("移动到…", func() { w.CopyOrMoveTo(true) }), fyne.NewMenuItem("复制路径", w.CopySelectedPath), fyne.NewMenuItemSeparator(), fyne.NewMenuItem("重命名", w.RenameSelected), fyne.NewMenuItem("权限", w.PermissionsSelected), fyne.NewMenuItem("删除", w.DeleteSelected))
}
