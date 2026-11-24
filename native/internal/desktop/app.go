package desktop

import (
	"context"
	"crypto/rand"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"forterm/native/internal/codeedit"
	"forterm/native/internal/fileops"
	"forterm/native/internal/ftpconn"
	"forterm/native/internal/launch"
	"forterm/native/internal/localfile"
	"forterm/native/internal/nativepicker"
	"forterm/native/internal/remotefs"
	"forterm/native/internal/settings"
	"forterm/native/internal/sshconn"
	"forterm/native/internal/transfers"
	terminal "forterm/native/internal/vtwidget"
	ws "forterm/native/internal/workspace"
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/app"
	"fyne.io/fyne/v2/container"
	"fyne.io/fyne/v2/dialog"
	"fyne.io/fyne/v2/theme"
	"fyne.io/fyne/v2/widget"
	"os"
	"strconv"
	"strings"
	"sync"
	"sync/atomic"
	"time"
)

const Version = "0.2.2-dev.11"

type App struct {
	FileDialogs                 nativepicker.Provider
	fileClipboard               *fileClipboard
	fileClipboardEpoch          uint64
	navigationEpoch             uint64
	autoNavigationCollapsed     bool
	connectionForms             map[*connectionForm]bool
	connections                 *connectionManager
	connectionsTab, accountsTab *container.TabItem
	diagnostic                  bool
	navigationWidth             float32
	navDivider                  *paneDivider
	navButton                   *widget.Button
	profileTree                 *widget.Tree
	profileNodes                profileTreeData
	restoringProfileBranches    bool
	profilePreparing            atomic.Bool
	Store                       *settings.Store
	temporarySettings           string
	navigation                  fyne.CanvasObject
	navigationList              *widget.List
	navigationQuery             string
	navigationVisible           bool
	chromeBody                  *fyne.Container
	footer                      *widget.Label
	model                       *ws.Model
	miscTabs                    []*container.TabItem
	UI                          fyne.App
	Window                      fyne.Window
	Tabs                        *container.DocTabs
	Status                      *widget.Label
	gate                        *launch.Gate
	trusted                     map[string]string
	sessions                    []*Workspace
	mu                          sync.Mutex
	closed                      bool
}

func New(id string) *App { return newApp(id, false) }

// NewDiagnostic uses an empty temporary store and a disabled credential service.
// Network connections and local process launch are blocked for this instance.
func NewDiagnostic(id string) *App { return newApp(id, true) }

func newApp(id string, diagnostic bool) *App {
	a := &App{diagnostic: diagnostic, UI: app.NewWithID(id), gate: launch.NewGate(), trusted: map[string]string{}, model: ws.New()}
	a.initSettings(id)
	a.ensureAppearance()
	a.Window = a.UI.NewWindow("Forterm Native · " + Version)
	a.Window.Resize(fyne.NewSize(float32(a.UI.Preferences().IntWithFallback("windowWidth", 1150)), float32(a.UI.Preferences().IntWithFallback("windowHeight", 760))))
	a.Status = widget.NewLabel("就绪")
	a.Tabs = container.NewDocTabs(container.NewTabItem("连接管理", a.startPage()))
	a.connectionsTab = a.Tabs.Items[0]
	a.miscTabs = append([]*container.TabItem(nil), a.Tabs.Items...)
	a.Tabs.OnSelected = func(tab *container.TabItem) {
		for _, w := range a.sessions {
			if w.Window != a.Window {
				continue
			}
			w.setWorkspaceVisible(w.Tab == tab)
		}
	}

	a.Tabs.CloseIntercept = func(tab *container.TabItem) {
		if tab == a.connectionsTab {
			a.connectionsTab = nil
		}
		if tab == a.accountsTab {
			a.accountsTab = nil
		}
		for _, s := range a.sessions {
			if s.Tab == tab {
				a.CloseWorkspace(s)
				return
			}
		}
		for i, t := range a.miscTabs {
			if t == tab {
				a.miscTabs = append(a.miscTabs[:i], a.miscTabs[i+1:]...)
				break
			}
		}
		a.Tabs.Remove(tab)
	}
	a.initChrome()
	a.installShortcuts()
	a.Window.SetOnDropped(func(_ fyne.Position, uris []fyne.URI) {
		for _, w := range a.sessions {
			if w.Tab == a.Tabs.Selected() && w.Transfers != nil {
				paths := []string{}
				for _, uri := range uris {
					if uri.Scheme() == "file" {
						if p, e := localfile.URIPath(uri); e == nil {
							paths = append(paths, p)
						} else {
							w.uploadError(e.Error())
						}
					}
				}
				if len(paths) > 0 {
					if directory, ok := w.uploadDirectory(); ok {
						w.ChooseUploads(paths, directory)
					}
				}
				return
			}
		}
	})
	a.Window.SetCloseIntercept(a.RequestExit)
	quit := fyne.NewMenuItem("退出", a.RequestExit)
	quit.IsQuit = true
	a.Window.SetMainMenu(fyne.NewMainMenu(fyne.NewMenu("文件", quit)))

	return a
}
func (a *App) Close() {
	a.mu.Lock()
	if a.closed {
		a.mu.Unlock()
		return
	}
	a.closed = true
	size := a.Window.Canvas().Size()
	if size.Width >= 540 && size.Height >= 320 {
		a.UI.Preferences().SetInt("windowWidth", int(size.Width))
		a.UI.Preferences().SetInt("windowHeight", int(size.Height))
	}
	a.mu.Unlock()
	for form := range a.connectionForms {
		form.dispose()
	}
	for _, s := range a.sessions {
		s.Close()
	}
}
func (a *App) Queue(args []string, entry string) {
	p := launch.Parse(args, entry)
	fyne.Do(func() { a.Open(p) })
}
func (a *App) Open(p launch.Plan) {
	if a.diagnostic {
		dialog.ShowInformation("离线诊断", "诊断窗口不建立连接、不运行终端。", a.Window)
		return
	}
	a.Window.Show()
	a.Window.RequestFocus()
	if p.Entry != "manual" && !a.gate.Accept(p, time.Now()) {
		a.Status.SetText("已合并3秒内重复启动；未重复使用票据建立连接")
		return
	}
	if p.Diagnose || p.Error != "" {
		b, _ := json.MarshalIndent(p.Report(), "", "  ")
		report := widget.NewLabel(string(b))
		report.Wrapping = fyne.TextWrapWord
		tab := container.NewTabItem("脱敏诊断", container.NewScroll(report))
		a.miscTabs = append(a.miscTabs, tab)
		a.Tabs.Append(tab)
		a.Tabs.SelectIndex(len(a.Tabs.Items) - 1)
		return
	}
	if p.Connection == nil {
		return
	}
	c := *p.Connection
	p.Connection.Password = ""

	title := c.Title
	if title == "" {
		title = c.Host
	}
	w := NewWorkspace(a, title, c.Protocol)
	w.profileID = c.ProfileID
	w.Protocol = c.Protocol
	if p.NewWindow {
		window := a.UI.NewWindow("Forterm Native · " + title)
		window.Resize(fyne.NewSize(1000, 700))
		w.Window = window
		window.SetContent(w.Content)
		window.SetCloseIntercept(func() { w.RequestClose(func() { w.Close(); window.SetCloseIntercept(nil); window.Close() }) })
		window.Show()
	} else {
		a.Tabs.Append(w.Tab)
		a.Tabs.Select(w.Tab)
	}
	go func() {
		ctx, cancel := context.WithTimeout(w.Context, 60*time.Second)
		defer cancel()
		if c.Protocol == "ftp" {
			conn, e := ftpconn.Dial(ctx, c)
			c.Password = ""
			if e != nil {
				fyne.Do(func() {
					if w.Context.Err() == nil {
						w.Status.SetText(e.Error())
					}
				})
				return
			}
			fyne.Do(func() {
				if w.Context.Err() != nil {
					conn.Close()
					return
				}
				w.SetFileConnection(conn)
				w.MonitorToggle.Hide()
				w.bodyHost.Objects = []fyne.CanvasObject{w.fileSourcesRoot}
				w.bodyHost.Refresh()
				w.Status.SetText("FTP文件连接已就绪，使用独立认证")
				w.Refresh()
			})
			return
		}
		conn, e := sshconn.Dial(ctx, c, sshconn.Callbacks{HostKey: a.ConfirmHost, Challenge: a.Challenge, KeepAlive: func() *sshconn.KeepAlive {
			value := a.Store.Snapshot().KeepAlive
			return &sshconn.KeepAlive{Enabled: value.Enabled, IntervalSeconds: value.IntervalSeconds, SFTPProbe: value.SFTPProbe}
		}()})
		c.Password = ""
		if e != nil {
			fyne.Do(func() {
				w.openingShell = false
				if w.Context.Err() == nil {
					w.Status.SetText(e.Error())
				}
			})
			return
		}
		if w.Context.Err() != nil {
			conn.Close()
			return
		}
		fyne.Do(func() {
			if w.Context.Err() != nil {
				conn.Close()
				return
			}
			w.SetConnection(conn)
			w.Status.SetText("SSH transport已认证；SFTP和终端按需独立打开")
			w.Refresh()
			if c.Protocol == "ssh" {
				w.StartShell()
			}
		})
	}()
}
func (a *App) ConfirmHost(ctx context.Context, endpoint, fingerprint string) (bool, error) {
	a.mu.Lock()
	old := a.trusted[endpoint]
	a.mu.Unlock()
	if old == fingerprint {
		return true, nil
	}
	reply := make(chan bool, 1)
	answered := make(chan struct{})
	var answerOnce sync.Once
	fyne.Do(func() {
		if ctx.Err() != nil {
			return
		}
		message := endpoint + "\n" + fingerprint + "\n请核对SSH主机指纹。确认后记住此主机指纹。"
		if old != "" {
			message = "警告：主机指纹已变化\n" + message
		}
		popup := dialog.NewConfirm("SSH主机指纹", message, func(ok bool) { answerOnce.Do(func() { reply <- ok; close(answered) }) }, a.Window)
		fitPopup(popup, a.Window, fyne.NewSize(780, 360))
		popup.Show()
		go func() {
			select {
			case <-ctx.Done():
				fyne.Do(popup.Hide)
			case <-answered:
			}
		}()
	})
	select {
	case ok := <-reply:
		if ok {
			a.mu.Lock()
			a.trusted[endpoint] = fingerprint
			a.mu.Unlock()
			if err := a.Store.TrustHost(endpoint, fingerprint); err != nil {
				fyne.Do(func() { a.SetStatus("本次已确认指纹，配置持久化未完成") })
			}
		}
		return ok, nil
	case <-ctx.Done():
		return false, ctx.Err()
	}
}
func (a *App) Challenge(ctx context.Context, title string, questions []string, echo []bool) ([]string, error) {
	return a.credentialQuestions(ctx, title, questions, echo)
}
func (a *App) ConnectDialog() { a.newConnectionForm(nil, true, nil).show() }
func (a *App) Local() {
	if a.diagnostic {
		dialog.ShowInformation("离线诊断", "诊断窗口不启动本地进程。", a.Window)
		return
	}
	w := NewWorkspace(a, "本地终端", "local")
	a.Tabs.Append(w.Tab)
	a.Tabs.Select(w.Tab)
	w.Status.SetText("本地终端：Windows使用ConPTY，尚待Windows现场验证")
	w.showTerminal()
	w.Window.Canvas().Focus(w.Term)
	go func() {
		e := w.Term.RunLocalShell()
		fyne.Do(func() {
			if e != nil {
				w.Status.SetText("本地终端启动失败")
			} else {
				w.Status.SetText("本地终端已结束")
			}
		})
	}()
}
func ConnectionFromFields(protocol, host, port, user, password string) (launch.Connection, error) {

	c := launch.Connection{Protocol: strings.ToLower(protocol), Host: strings.TrimSpace(host), Username: user, Password: password, HasPassword: true}
	n, e := strconv.Atoi(port)
	if e != nil || n < 1 || n > 65535 || c.Host == "" || user == "" || strings.ContainsAny(c.Host, "/\\ \t\r\n") {
		return c, fmt.Errorf("请填写有效主机、端口和用户")
	}
	c.Port = n
	return c, nil
}

type Workspace struct {
	pickerBusy                              bool
	transferRows                            []*transferRow
	transferFooter                          *widget.Label
	fileTree                                *remoteFileTree
	navigationEpoch                         uint64
	navigationHandled                       bool
	profileID                               string
	uploadTrace                             uploadTrace
	transferUIQueued                        atomic.Bool
	queueExpanded                           *widget.Check
	editorDock                              string
	primaryTerminal                         fyne.CanvasObject
	secondTerminal, focusedTerminal         *Workspace
	terminalsSideBySide                     bool
	terminalShare                           float64
	editorBottomRatio, editorRightRatio     float64
	editorDockRight                         bool
	allEntries                              []sshconn.Entry
	fileFilter                              *widget.Entry
	fileSort                                string
	showHidden                              bool
	fileWidth, monitorWidth                 float32
	filesPaneEnabled                        bool
	pendingPath                             string
	directoryError                          string
	directoryReady                          bool
	exactDirectory                          string
	editorRequestEpoch                      uint64
	ID, Title                               string
	owner, source                           *Workspace
	sourceViews                             map[string]*Workspace
	sourceButtons                           map[string]*widget.Button
	sourceTabs                              *fyne.Container
	sourceHost                              *fyne.Container
	fileSourcesRoot                         fyne.CanvasObject
	fileViewHost                            *fyne.Container
	filesVisible, workspaceVisible          bool
	unsubscribeQueue, unsubscribeOperations func()
	Operations                              *fileops.Service
	operationStatus                         *widget.Label
	operationCancel                         *widget.Button
	Protocol                                string
	FileConn                                remotefs.Connection
	filePane                                fyne.CanvasObject
	bodyHost                                *fyne.Container
	refreshPending                          bool
	Transfers                               *transfers.Queue
	transferItems                           []transfers.Item
	transferStates                          map[string]transfers.State
	transferList                            *widget.List
	transferSummary                         *widget.Label
	queuePane                               *fyne.Container
	documents                               []*RemoteDocument
	openingText                             map[string]bool
	editorTabs                              *container.DocTabs
	editorHost                              *fyne.Container
	editorSplit                             *fyne.Container
	centerCell                              *fyne.Container
	MonitorToggle                           *widget.Check
	Monitor                                 *MonitorPanel
	monitorEnabled                          bool
	monitorSplit                            *container.Split
	Window                                  fyne.Window
	App                                     *App
	Tab                                     *container.TabItem
	Content                                 fyne.CanvasObject
	Status                                  *widget.Label
	Term                                    *terminal.Terminal
	Context                                 context.Context
	cancel                                  context.CancelFunc
	Conn                                    *sshconn.Connection
	shell                                   *sshconn.Shell
	termBox                                 *fyne.Container
	Files                                   *widget.List
	Path                                    *widget.Entry
	entries                                 []sshconn.Entry
	directory                               string
	selected                                int
	busy                                    bool
	config                                  terminal.Config
	resize                                  chan terminal.Config
	pasteEpoch                              uint64
	closeOnce                               sync.Once
	openingShell                            bool
	resizeRequests                          chan terminal.Config
}

func NewWorkspace(a *App, title string, protocols ...string) *Workspace {
	protocol := "ssh"
	if len(protocols) > 0 {
		protocol = protocols[0]
	}
	var nonce [12]byte
	_, _ = rand.Read(nonce[:])
	ctx, cancel := context.WithCancel(context.Background())
	w := &Workspace{navigationEpoch: a.navigationEpoch, ID: hex.EncodeToString(nonce[:]), Title: title, Protocol: protocol, filesVisible: true, Window: a.Window, App: a, Context: ctx, cancel: cancel, directory: ".", selected: -1, resize: make(chan terminal.Config, 32), resizeRequests: make(chan terminal.Config, 1)}
	w.openingText = map[string]bool{}
	w.Status = widget.NewLabel("连接中…")
	w.operationStatus = widget.NewLabel("")
	w.operationStatus.Wrapping = fyne.TextWrapWord
	w.operationCancel = widget.NewButton("取消目录操作", func() {
		if w.Operations != nil {
			w.Operations.Cancel()
		}
	})
	w.operationCancel.Hide()
	w.Term = terminal.New()
	w.Term.OnReady = w.collapseNavigationAfterConnect
	w.Term.SetFontSize(float32(a.UI.Preferences().IntWithFallback("terminalFontSize", 13)))
	w.Term.AddListener(w.resize)
	w.Term.OnPaste = w.paste
	w.Term.OnFocus = func() {
		active := a.ActiveWorkspace()
		if active != nil && (active == w || active.secondTerminal == w) {
			active.focusedTerminal = w
			a.SetStatus("当前输入终端 · " + w.Title)
		}
	}
	w.terminalShare = a.UI.Preferences().FloatWithFallback("terminalShare", .5)
	w.Term.OnError = func(err error) {
		w.Status.SetText(err.Error())
		if w.shell != nil {
			go w.shell.Close()
		}
	}
	left := w.initFilePane()
	w.sourceViews = map[string]*Workspace{w.ID: w}
	w.sourceButtons = map[string]*widget.Button{}
	w.sourceTabs = container.NewHBox()
	w.fileViewHost = container.NewStack(left)
	fileHeading := container.NewBorder(nil, nil, widget.NewLabelWithStyle("文件", fyne.TextAlignLeading, fyne.TextStyle{Bold: true}), action("", theme.ContentAddIcon(), w.ChooseFileSource))
	w.fileSourcesRoot = paneSurface(container.NewBorder(container.NewVBox(fileHeading, container.NewHScroll(w.sourceTabs)), nil, nil, nil, w.fileViewHost), theme.ColorNameBackground)

	w.termBox = container.NewStack(container.NewCenter(container.NewVBox(widget.NewLabel("终端尚未打开"), action("打开终端", theme.ComputerIcon(), w.StartShell))))
	w.centerCell = container.NewStack(w.termBox)
	w.editorTabs = container.NewDocTabs()
	w.editorTabs.CloseIntercept = w.closeDocument
	w.editorHost = container.NewStack(w.editorTabs)
	w.editorSplit = w.makeEditorDock()
	w.Monitor = NewMonitorPanel(w)
	w.monitorEnabled = false
	w.Monitor.SetVisible(false)
	w.initWorkbench()

	w.Tab = container.NewTabItem(title, w.Content)
	a.sessions = append(a.sessions, w)
	a.model.Add(ws.Session{ID: w.ID, Title: w.Title, Protocol: w.Protocol, Open: true})
	if a.navigationList != nil {
		a.navigationList.Refresh()
	}
	w.workspaceVisible = true
	w.filesVisible = w.filesPaneEnabled
	w.MonitorToggle.SetChecked(a.UI.Preferences().BoolWithFallback("monitorVisible", false))
	w.renderSourceTabs()
	go func() {
		for {
			select {
			case <-ctx.Done():
				return
			case cfg, ok := <-w.resize:
				if !ok {
					return
				}
				fyne.Do(func() {
					if ctx.Err() != nil {
						return
					}
					w.config = cfg
					if w.shell != nil {
						select {
						case <-w.resizeRequests:
						default:
						}
						w.resizeRequests <- cfg
					}
				})
			}
		}
	}()
	go func() {
		for {
			select {
			case <-ctx.Done():
				return
			case cfg := <-w.resizeRequests:
				var shell *sshconn.Shell
				fyne.DoAndWait(func() {
					if ctx.Err() == nil {
						shell = w.shell
					}
				})
				if shell != nil {
					_ = shell.Resize(int(cfg.Rows), int(cfg.Columns))
				}
			}
		}
	}()
	return w
}
func (w *Workspace) Close() {
	if w.owner != nil {
		w.closeBorrowedView()
		return
	}
	w.closeOnce.Do(func() {
		w.cancel()
		for _, view := range w.sourceViews {
			if view != w {
				view.closeBorrowedView()
			}
		}
		if w.Operations != nil {
			w.Operations.Cancel()
		}
		if w.Transfers != nil {
			w.Transfers.Close()
		}
		w.pasteEpoch++
		w.Term.Close()
		if w.shell != nil {
			w.shell.Close()
		}
		if w.Conn != nil {
			w.Conn.Release()
		}
		if w.FileConn != nil && w.FileConn != w.Conn {
			w.FileConn.Close()
		}
		for _, d := range w.documents {
			d.Editor.Close()
		}
		w.documents = nil
		w.editorTabs.SetItems(nil)
		w.Term.RemoveListener(w.resize)
	})
}
func (w *Workspace) showTerminal() {
	find := widget.NewEntry()
	find.PlaceHolder = "搜索终端与历史"
	find.OnSubmitted = func(query string) {
		if !w.Term.Search(query, false) {
			w.Status.SetText("未找到匹配文本")
		}
	}
	searchRow := container.NewBorder(nil, nil, nil, container.NewHBox(action("", theme.MoveUpIcon(), func() { w.Term.Search(find.Text, true) }), action("", theme.MoveDownIcon(), func() { w.Term.Search(find.Text, false) })), find)
	searchRow.Hide()
	heading := widget.NewLabelWithStyle("终端", fyne.TextAlignLeading, fyne.TextStyle{Bold: true})
	title := widget.NewLabel(w.Title)
	title.Importance = widget.LowImportance
	title.Truncation = fyne.TextTruncateEllipsis
	top := container.NewBorder(nil, nil, container.NewHBox(heading, title), action("", theme.SearchIcon(), func() {
		if searchRow.Visible() {
			searchRow.Hide()
		} else {
			searchRow.Show()
			w.Window.Canvas().Focus(find)
		}
	}))
	top = container.NewBorder(nil, nil, nil, action("会话", theme.MoreHorizontalIcon(), w.showSessionMenu), top)
	w.primaryTerminal = paneSurface(container.NewBorder(container.NewVBox(top, searchRow), nil, nil, nil, container.NewPadded(w.Term)), theme.ColorNameInputBackground)
	w.renderTerminals()
}

func (w *Workspace) StartShell() {
	if w.Context.Err() != nil {
		return
	}
	if w.owner != nil {
		w.owner.StartShell()
		return
	}
	if w.Conn == nil || w.shell != nil || w.openingShell {
		return
	}
	focusAtStart := w.Window.Canvas().Focused()
	w.openingShell = true
	w.Status.SetText("正在打开shell通道…")
	rows, cols := int(w.config.Rows), int(w.config.Columns)
	if rows < 1 {
		rows = 24
	}
	if cols < 2 {
		cols = 80
	}
	go func() {
		s, e := w.Conn.ShellContext(w.Context, rows, cols)
		if e != nil {
			fyne.Do(func() {
				w.openingShell = false
				if w.Context.Err() == nil {
					w.Status.SetText(e.Error())
				}
			})
			return
		}
		if w.Context.Err() != nil {
			s.Close()
			return
		}
		fyne.Do(func() {
			w.openingShell = false
			if w.Context.Err() != nil {
				s.Close()
				return
			}
			w.shell = s
			w.showTerminal()
			if w.active() && (w.Window.Canvas().Focused() == focusAtStart || w.Window.Canvas().Focused() == nil) {
				w.Window.Canvas().Focus(w.Term)
			}
			w.Status.SetText("终端已就绪")
		})
		_ = w.Term.RunWithConnection(s.In, s.Out)
		fyne.Do(func() { w.Status.SetText("终端已结束；没有自动重连临时票据") })
	}()
}
func (w *Workspace) paste(text string) {
	if w.Context.Err() != nil || text == "" {
		return
	}
	if len([]byte(text)) > 1024*1024 {
		w.Status.SetText("剪贴板超过1MiB")
		return
	}
	for _, r := range text {
		if (r < 32 && r != '\n' && r != '\r' && r != '\t') || r == 127 {
			w.Status.SetText("剪贴板含终端控制字符，请检查后再粘贴")
			return
		}
	}
	w.pasteEpoch++
	token := w.pasteEpoch
	send := func() {
		if w.Context.Err() == nil && token == w.pasteEpoch && w.Window.Canvas().Focused() == w.Term {
			w.Term.Paste(text)
		}
	}
	if strings.ContainsAny(text, "\r\n") {
		dialogDone := make(chan struct{})
		var doneOnce sync.Once
		preview := codeedit.New("clipboard.txt", text)
		preview.ReadOnly = true
		summary := widget.NewLabel(fmt.Sprintf("%d字节 · 原样发送，不追加回车。请检查全部内容。", len(text)))
		summary.Wrapping = fyne.TextWrapWord
		content := container.NewBorder(summary, nil, nil, nil, preview)
		size := w.Window.Canvas().Size()
		prompt := dialog.NewCustomConfirm("确认多行终端粘贴 · "+w.Tab.Text, "粘贴", "取消", content, func(ok bool) {
			preview.Close()
			doneOnce.Do(func() { close(dialogDone) })
			if ok && w.active() && w.Context.Err() == nil && token == w.pasteEpoch {
				w.Window.Canvas().Focus(w.Term)
				send()
			}
		}, w.Window)
		prompt.Resize(fyne.NewSize(max(260, min(720, size.Width-60)), max(200, min(460, size.Height-100))))
		prompt.Show()
		go func() {
			select {
			case <-w.Context.Done():
				fyne.Do(func() { prompt.Hide(); preview.Close() })
			case <-dialogDone:
			}
		}()

		return
	}
	send()
}
func (w *Workspace) Refresh() { w.refreshDirectory(false) }
func (w *Workspace) refreshDirectory(exact bool) {
	if !w.filesVisible && !exact {
		w.refreshPending = true
		return
	}
	if !w.fileAvailable() {
		return
	}
	if w.busy {
		w.refreshPending = true
		w.pendingPath = w.Path.Text
		return
	}
	w.refreshPending = false
	w.pendingPath = ""
	w.busy = true
	target := w.Path.Text
	useExact := exact || (w.exactDirectory != "" && target == w.exactDirectory)
	w.Status.SetText("读取文件目录…")
	go func() {
		ctx, cancel := context.WithTimeout(w.Context, 20*time.Second)
		defer cancel()
		var resolved, notice string
		var entries []remotefs.Entry
		var e error
		if useExact {
			resolved, entries, notice, e = readExactDirectory(ctx, w.FileConn, target)
		} else {
			resolved, entries, notice, e = readDirectory(ctx, w.FileConn, target)
		}
		fyne.Do(func() {
			w.busy = false
			if w.Context.Err() != nil {
				return
			}
			if w.refreshPending {
				w.refreshPending = false
				pending := w.pendingPath
				defer func() { w.Path.SetText(pending); w.Refresh() }()
			}
			if e != nil {
				w.directoryReady = false
				w.directoryError = e.Error()
				w.Status.SetText(e.Error() + "；保留旧列表，目标目录尚未切换")
				return
			}
			w.directoryReady = true
			if exact {
				w.exactDirectory = resolved
			}
			w.directoryError = ""
			w.directory = resolved
			w.Path.SetText(resolved)
			w.allEntries = entries
			w.entries = nil
			w.applyFileFilter()
			if notice != "" {
				w.Status.SetText(notice)
			} else {
				w.Status.SetText("文件目录已读取")
			}
		})
	}()
}
func (w *Workspace) EnterDirectory() {
	entry, remote, ok := w.selectedRemoteEntry()
	if !ok || !entry.Directory || entry.Mode&os.ModeSymlink != 0 {
		return
	}
	w.Path.SetText(remote)
	w.Refresh()
}
func (w *Workspace) Upload() { w.UploadAt(nil) }

// UploadAt opens the normal picker at an optional explicitly chosen local
// directory; destination and source are captured before the picker opens.
func (w *Workspace) UploadAt(start fyne.ListableURI) { w.uploadNative(start) }

// Explicitly chosen fallback only; it is never described as a system picker.
func (w *Workspace) uploadUsingFyne(start fyne.ListableURI, directory string) {
	picker := dialog.NewFileOpen(func(reader fyne.URIReadCloser, err error) {
		if err != nil {
			w.uploadError(err.Error())
			return
		}
		if reader == nil {
			w.Status.SetText("已取消选择，未加入队列")
			return
		}
		local, e := localfile.URIPath(reader.URI())
		go reader.Close()
		if e != nil {
			w.uploadError(e.Error())
			return
		}
		if w.fileAvailable() {
			w.EnqueueUploads([]string{local}, directory)
		}
	}, w.Window)
	if start != nil {
		picker.SetLocation(start)
	}
	picker.Show()
}
func (w *Workspace) active() bool { return w.Window != w.App.Window || w.App.Tabs.Selected() == w.Tab }
func (w *Workspace) Download()    { w.downloadNative() }

func (a *App) ActiveWorkspace() *Workspace {
	selected := a.Tabs.Selected()
	for _, w := range a.sessions {
		if w.Tab == selected {
			return w
		}
	}
	return nil
}
func (w *Workspace) SelectFile(name string) bool {
	for i, entry := range w.entries {
		if entry.Name == name {
			w.Files.Select(i)
			w.Files.ScrollTo(i)
			return true
		}
	}
	return false
}

func (w *Workspace) initFilePane() fyne.CanvasObject {
	w.Path = widget.NewEntry()
	w.Path.SetText(".")
	w.Path.OnSubmitted = func(string) { w.Refresh() }
	w.Files = widget.NewList(func() int { return len(w.entries) }, func() fyne.CanvasObject { return newFileRow(w) }, func(id widget.ListItemID, obj fyne.CanvasObject) {
		e := w.entries[id]

		row := obj.(*fileRow)
		row.index = id
		row.label.SetText(e.Name)
		if e.Directory {
			row.icon.SetResource(theme.FolderIcon())
			row.detail.SetText("")
		} else {
			row.icon.SetResource(theme.DocumentIcon())
			row.detail.SetText(formatBytes(e.Size))
		}
	})
	w.Files.OnSelected = func(id widget.ListItemID) { w.selected = id }
	w.fileTree = newRemoteFileTree(w)
	tools := w.compactFileTools()
	w.Status.Truncation = fyne.TextTruncateEllipsis
	w.Status.Importance = widget.LowImportance
	var bottom fyne.CanvasObject = w.initTransferPanel()
	if w.owner != nil {
		bottom = container.NewVBox(bottom, w.Status)
	}
	left := scrollableFilePane(tools, w.fileTree.view, bottom)
	w.filePane = left
	return left
}

// Queue background-operation feedback only while this application is open.
func (a *App) postOpen(fn func()) {
	fyne.Do(func() {
		a.mu.Lock()
		closed := a.closed
		a.mu.Unlock()
		if !closed {
			fn()
		}
	})
}
