import { WorkspaceFiles } from "./WorkspaceFiles";
import { visibleWorkspaceTabs, workspaceForTab } from "./workspace-tabs";
import {
  ContextMenuHost,
  inputContextMenu,
  openContextMenu,
  closeContextMenu,
  menuKey,
} from "./ContextMenu";
import {
  useEffect,
  useMemo,
  useState,
  useRef,
  useLayoutEffect,
  type CSSProperties,
} from "react";
import {
  TerminalSquare,
  Plus,
  Search,
  Folder,
  ChevronDown,
  Server,
  Settings2,
  PanelLeftClose,
  PanelLeftOpen,
  PanelRightClose,
  PanelRightOpen,
  Columns2,
  Files,
  Activity,
  ArrowDownToLine,
  ArrowUpFromLine,
  ShieldCheck,
  Command,
  ArrowUpRight,
  FileJson,
  ScanLine,
  X,
  Pencil,
  Trash2,
  Sun,
  Moon,
  KeyRound,
  RefreshCw,
  Check,
} from "lucide-react";
import type {
  SessionInfo,
  Profile,
  Report,
  Prompt,
  Transfer,
  KeepAliveSettings,
  FileSourceGroup,
} from "../shared/types";
import { TerminalView } from "./TerminalView";
import { ConnectionDialog } from "./ConnectionDialog";
import { FilePanel, size } from "./FilePanel";
import { mergeReports } from "../shared/reports";
import { APP_VERSION } from "../shared/version";
import { DEFAULT_KEEP_ALIVE } from "../shared/keep-alive";
import { KeepAliveDialog } from "./KeepAliveDialog";
import { RemoteEditor, type EditorDocument } from "./RemoteEditor";
import { MonitorPanel } from "./MonitorPanel";
import { CompactToolbar } from "./CompactToolbar";
import { BastionAccountsDialog } from "./BastionAccountsDialog";
import type { BastionAccount } from "../shared/bastion";
import { ResizeHandle } from "./ResizeHandle";
import { useElementSize } from "./useElementSize";
import {
  DEFAULT_LAYOUT,
  readLayout,
  writeLayout,
  clamp,
  ratioBounds,
  workspacePaneWidths,
  type WorkspaceLayout,
} from "./layout";
import { readPreferences, writePreferences } from "./preferences";
function errorText(e: unknown) {
  return String(e instanceof Error ? e.message : e).replace(
    /^Error invoking remote method '[^']+': Error: /,
    "",
  );
}
export function App() {
  const [initialPreferences] = useState(readPreferences);
  const [layout, setLayout] = useState(readLayout);
  const [rootRef, rootSize] = useElementSize<HTMLDivElement>();
  const [stackRef, stackSize] = useElementSize<HTMLDivElement>();
  const [workRef, workSize] = useElementSize<HTMLDivElement>();
  const [columnsRef, columnsSize] = useElementSize<HTMLDivElement>();
  const [terminalRef, terminalSize] = useElementSize<HTMLDivElement>();
  const changeLayout = (key: keyof WorkspaceLayout, value: number | boolean) =>
    setLayout((old) => ({ ...old, [key]: value }));
  const [bastionCollapsed, setBastionCollapsed] = useState(
    initialPreferences.bastionCollapsed,
  );
  const [bastionTarget, setBastionTarget] = useState<{
    mode: "list" | "new" | "edit";
    id?: string;
  }>({ mode: "list" });
  const [collapsed, setCollapsed] = useState(initialPreferences.collapsed);
  const [documents, setDocuments] = useState<EditorDocument[]>([]);
  const codeStateCache = useRef<import("./CodeEditor").EditorStateCache>(
    new Map(),
  );
  useEffect(() => {
    const valid = new Set(
      documents.map((d) => `${d.workspaceId}:${d.sessionId}:${d.path}`),
    );
    for (const key of codeStateCache.current.keys())
      if (!valid.has(key)) codeStateCache.current.delete(key);
  }, [documents]);
  const [editorSelections, setEditorSelections] = useState<
    Record<string, { sourceId: string; path: string }>
  >({});
  const selectEditor = (workspaceId: string, sourceId: string, path: string) =>
    setEditorSelections((old) => ({
      ...old,
      [workspaceId]: { sourceId, path },
    }));
  const [editorCollapsed, setEditorCollapsed] = useState(false);
  const editorDockRef = useRef<HTMLDivElement>(null),
    editorKey = useRef("");
  const editorViews = useRef(
    new Map<
      string,
      { top: number; left: number; start: number; end: number }
    >(),
  );
  const captureEditor = () => {
    editorDockRef.current
      ?.querySelector(".code-editor-host")
      ?.dispatchEvent(new CustomEvent("forterm-editor-capture"));
    const t = editorDockRef.current?.querySelector("textarea");
    if (t && editorKey.current && !editorCollapsed)
      editorViews.current.set(editorKey.current, {
        top: t.scrollTop,
        left: t.scrollLeft,
        start: t.selectionStart,
        end: t.selectionEnd,
      });
  };
  const collapseEditor = () => {
    captureEditor();
    setEditorCollapsed((old) => !old);
  };
  const [bastionAccounts, setBastionAccounts] = useState<BastionAccount[]>([]),
    [showBastionAccounts, setShowBastionAccounts] = useState(false);
  const opening = useRef(false);
  const dirtyIds = useRef(new Set<string>());
  const [help, setHelp] = useState(false);
  const [horizontal, setHorizontal] = useState(layout.horizontal);
  const [splitId, setSplitId] = useState("");
  const [keepAlive, setKeepAlive] = useState<KeepAliveSettings>({
    ...DEFAULT_KEEP_ALIVE,
  });
  const [showKeepAlive, setShowKeepAlive] = useState(false);
  const [fileOperations, setFileOperations] = useState<
    import("../shared/types").FileOperation[]
  >([]);
  const [fileAssociations, setFileAssociations] = useState<
    Record<string, string>
  >({});
  const [fileSourceGroups, setFileSourceGroups] = useState<
    Record<string, FileSourceGroup>
  >({});
  const [sessions, setSessions] = useState<SessionInfo[]>([]),
    [profiles, setProfiles] = useState<Profile[]>([]),
    [reports, setReports] = useState<Report[]>([]),
    [active, setActive] = useState(""),
    [filter, setFilter] = useState("");
  const [mode, setMode] = useState<"work" | "diagnosis">("work"),
    [split, setSplit] = useState(false),
    [fontSize, setFontSize] = useState(initialPreferences.fontSize),
    [light, setLight] = useState(initialPreferences.light);
  const [form, setForm] = useState<{
      profile?: Profile;
      initialProtocol?: "sftp" | "ftp";
      associateWith?: string;
    } | null>(null),
    [prompt, setPrompt] = useState<Prompt | null>(null),
    [answers, setAnswers] = useState<string[]>([]),
    [transfers, setTransfers] = useState<Transfer[]>([]),
    [toast, setToast] = useState("");
  const showError = (e: unknown) => setToast(errorText(e));
  const selectionContext = useRef({
    sessions,
    fileAssociations,
    fileSourceGroups,
  });
  selectionContext.current = { sessions, fileAssociations, fileSourceGroups };
  const resolveSelection = (id: string) => {
    const context = selectionContext.current;
    if (!context.sessions.some((s) => s.id === id)) return id;
    const visible = visibleWorkspaceTabs(
      context.sessions,
      context.fileAssociations,
      context.fileSourceGroups,
    );
    if (visible.some((s) => s.id === id)) return id;
    const owner = workspaceForTab(
      id,
      visible,
      context.fileAssociations,
      context.fileSourceGroups,
    );
    const group = context.fileSourceGroups[owner];
    if (group?.sourceIds.includes(id) && group.activeId !== id)
      void window.forterm.selectFileSource(owner, id).catch(showError);
    return owner;
  };
  const select = (id: string) => {
    captureEditor();
    setActive(resolveSelection(id));
    setMode("work");
  };
  useEffect(() => {
    const off = window.forterm.onEvent((e) => {
      if (e.type === "bastion-accounts") setBastionAccounts(e.accounts);
      if (e.type === "file-operation")
        setFileOperations((old) => [
          ...old.filter((o) => o.id !== e.operation.id),
          e.operation,
        ]);
      if (e.type === "file-sources")
        setFileSourceGroups((old) => ({ ...old, [e.workspaceId]: e.group }));
      if (e.type === "file-association")
        setFileAssociations((old) => {
          const next = { ...old };
          if (e.fileSessionId) next[e.workspaceId] = e.fileSessionId;
          else delete next[e.workspaceId];
          return next;
        });
      if (e.type === "keepalive-settings") setKeepAlive(e.settings);
      if (e.type === "session-focus") select(e.id);
      if (e.type === "transfers-cleared")
        setTransfers((old) =>
          old.filter((t) => t.state === "running" || t.state === "queued"),
        );
      if (e.type === "session-removed")
        setDocuments((old) =>
          old.filter((d) => d.sessionId !== e.id && d.workspaceId !== e.id),
        );
      if (e.type === "session-removed")
        setSessions((old) => {
          const remaining = old.filter((s) => s.id !== e.id);
          setActive((id) => (id === e.id ? remaining.at(-1)?.id || "" : id));
          return remaining;
        });
      if (e.type === "session") {
        captureEditor();
        setSessions((old) => {
          const found = old.some((s) => s.id === e.session.id);
          if (!found) {
            setActive(e.session.id);
            setMode("work");
            if (e.session.protocol === "sftp" || e.session.protocol === "ftp")
              setLayout((old) => ({ ...old, filesVisible: true }));
          }
          return found
            ? old.map((s) => (s.id === e.session.id ? e.session : s))
            : [...old, e.session];
        });
      }
      if (e.type === "diagnostic") {
        setReports((old) => mergeReports(old, [e.report]));
        setMode("diagnosis");
      }
      if (e.type === "transfer")
        setTransfers((old) =>
          old.some((t) => t.id === e.transfer.id)
            ? old.map((t) => (t.id === e.transfer.id ? e.transfer : t))
            : [...old, e.transfer],
        );
      if (e.type === "prompt") setPrompt((current) => current || e.prompt);
      if (e.type === "prompt-closed") {
        setPrompt((current) => (current?.id === e.id ? null : current));
        void window.forterm
          .bootstrap()
          .then((b) => setPrompt((current) => current || b.prompts[0] || null));
      }
      if (e.type === "notice") setToast(e.message);
    });
    void window.forterm
      .bootstrap()
      .then((b) => {
        setFileOperations(b.fileOperations);
        setFileAssociations(b.fileAssociations);
        setFileSourceGroups(b.fileSourceGroups ?? {});
        setBastionAccounts(b.bastionAccounts);
        setSessions(b.sessions);
        setProfiles(b.profiles);
        setKeepAlive(b.keepAlive);
        setReports((old) => mergeReports(b.reports, old));
        setTransfers(b.transfers);
        setActive(b.sessions.at(-1)?.id || "");
        setPrompt(b.prompts[0] || null);
        if (b.diagnose || b.reports.length) setMode("diagnosis");
      })
      .catch(showError);
    return off;
  }, []);
  useEffect(() => {
    if (!toast) return;
    const timer = setTimeout(() => setToast(""), 6500);
    return () => clearTimeout(timer);
  }, [toast]);
  useEffect(() => {
    setAnswers(prompt?.fields.map(() => "") || []);
  }, [prompt?.id]);
  const visibleTabs = useMemo(
    () => visibleWorkspaceTabs(sessions, fileAssociations, fileSourceGroups),
    [sessions, fileAssociations, fileSourceGroups],
  );
  useLayoutEffect(() => {
    if (active && !visibleTabs.some((s) => s.id === active)) {
      captureEditor();
      setActive(resolveSelection(active));
    }
  }, [active, visibleTabs, fileAssociations, fileSourceGroups]);
  const current = sessions.find((s) => s.id === active);
  const groupFor = (workspaceId: string): FileSourceGroup =>
    fileSourceGroups[workspaceId] ?? {
      sourceIds: [
        ...new Set([
          workspaceId,
          ...(fileAssociations[workspaceId]
            ? [fileAssociations[workspaceId]]
            : []),
        ]),
      ],
      activeId: fileAssociations[workspaceId] ?? workspaceId,
    };
  const fileId = groupFor(active).activeId;
  const currentFiles = sessions.find((s) => s.id === fileId);
  const fileWorkspaces = sessions.filter(
    (s) =>
      s.protocol === "ssh" || (s.protocol === "sftp" && !!s.terminalStatus),
  );
  const changeSource = (workspaceId: string, id: string, attach = false) => {
    captureEditor();
    void (
      attach
        ? window.forterm.attachFileSource(workspaceId, id)
        : window.forterm.selectFileSource(workspaceId, id)
    ).catch(showError);
  };
  const splitCandidates = visibleTabs.filter(
    (s) =>
      s.id !== active &&
      (s.protocol === "ssh" || s.protocol === "local" || s.terminalStatus),
  );
  const second =
    split &&
    (current?.protocol === "ssh" ||
      current?.protocol === "local" ||
      current?.terminalStatus)
      ? splitCandidates.find((s) => s.id === splitId) || splitCandidates.at(-1)
      : undefined;
  const matching = profiles.filter((p) =>
    [p.title, p.host, p.username, p.group].some((v) =>
      v?.toLowerCase().includes(filter.trim().toLowerCase()),
    ),
  );
  const matchingBastions = bastionAccounts.filter((a) =>
    [a.name, a.portalUrl, a.username].some((v) =>
      v.toLowerCase().includes(filter.trim().toLowerCase()),
    ),
  );
  const groups = [...new Set(matching.map((p) => p.group))].sort((a, b) =>
    a.localeCompare(b, "zh-CN"),
  );
  const document =
    documents.find(
      (d) =>
        d.workspaceId === active &&
        d.sessionId === editorSelections[active]?.sourceId &&
        d.path === editorSelections[active]?.path,
    ) || documents.find((d) => d.workspaceId === active);
  editorKey.current = document
    ? `${active}:${document.sessionId}:${document.path}`
    : "";
  useLayoutEffect(() => {
    if (editorCollapsed) return;
    let second = 0;
    const first = requestAnimationFrame(() => {
      second = requestAnimationFrame(() => {
        editorDockRef.current
          ?.querySelector(".code-editor-host")
          ?.dispatchEvent(new CustomEvent("forterm-editor-restore"));
      });
    });
    return () => {
      cancelAnimationFrame(first);
      cancelAnimationFrame(second);
    };
  }, [layout.editorDock, editorCollapsed, document?.path, active, fileId]);
  const filesShown =
    !!current &&
    fileWorkspaces.some((s) => s.id === current.id) &&
    layout.filesVisible;
  const monitorShown =
    !!current &&
    ["ssh", "sftp"].includes(current.protocol) &&
    layout.monitorVisible;
  const panes = workspacePaneWidths(
    columnsSize.width,
    filesShown,
    monitorShown,
    layout.filesWidth,
    layout.monitorWidth,
  );
  const effectiveEditorDock =
    layout.editorDock !== "bottom" &&
    stackSize.width < (second && horizontal ? 760 : 480)
      ? "bottom"
      : layout.editorDock;
  const editorWidthBounds = {
    min: Math.min(240, stackSize.width * 0.45),
    max: Math.max(
      Math.min(240, stackSize.width * 0.45),
      Math.min(700, stackSize.width - 207),
    ),
  };
  const editorWidth = clamp(
    layout.editorWidth,
    editorWidthBounds.min,
    editorWidthBounds.max,
  );
  const sidebarBounds = {
    min: 180,
    max: Math.max(
      180,
      Math.min(480, rootSize.width - (filesShown && monitorShown ? 420 : 350)),
    ),
  };
  const sidebarWidth = clamp(
    layout.sidebar,
    sidebarBounds.min,
    sidebarBounds.max,
  );
  const editorMax = Math.max(
    0,
    stackSize.height -
      Math.min(
        second && !horizontal ? 200 : 160,
        Math.max(0, stackSize.height - 99),
      ) -
      39,
  );
  const editorBounds = { min: Math.min(160, editorMax), max: editorMax };
  const editorHeight = clamp(layout.editor, editorBounds.min, editorBounds.max);
  const terminalBounds = ratioBounds(
    horizontal ? terminalSize.width : terminalSize.height,
    horizontal ? 180 : 80,
    horizontal ? 180 : 80,
  );
  const terminalRatioKey = horizontal ? "horizontalRatio" : "verticalRatio";
  const terminalRatio = clamp(
    layout[terminalRatioKey],
    terminalBounds.min,
    terminalBounds.max,
  );
  const firstTerminal = sessions.find(
    (session) => session.id === active || session.id === second?.id,
  )?.id;
  const layoutSnapshot = useRef<WorkspaceLayout>({ ...layout, horizontal });
  layoutSnapshot.current = { ...layout, horizontal };
  useEffect(() => {
    const timer = setTimeout(() => writeLayout({ ...layout, horizontal }), 150);
    return () => clearTimeout(timer);
  }, [layout, horizontal]);
  const resetLayout = () => {
    setLayout({ ...DEFAULT_LAYOUT });
    setHorizontal(false);
  };
  const toggleNavigation = () => {
    captureEditor();
    const next = !layout.navigationVisible;
    changeLayout("navigationVisible", next);
    requestAnimationFrame(() =>
      window.document
        .querySelector<HTMLButtonElement>(
          next ? ".sidebar-collapse" : ".navigation-reveal",
        )
        ?.focus({ preventScroll: true }),
    );
  };
  useEffect(() => {
    writePreferences({
      light,
      fontSize,
      panel: layout.filesVisible,
      collapsed,
      bastionCollapsed,
    });
  }, [light, fontSize, layout.filesVisible, collapsed, bastionCollapsed]);
  useEffect(() => {
    const next = new Set(
      documents
        .filter((d) => d.isNew || d.text !== d.saved || d.busy)
        .flatMap((d) => [d.sessionId, d.workspaceId]),
    );
    for (const id of new Set([...next, ...dirtyIds.current])) {
      if (next.has(id) !== dirtyIds.current.has(id))
        void window.forterm.setDirty(id, next.has(id)).catch(showError);
    }
    dirtyIds.current = next;
  }, [documents]);
  useEffect(() => {
    const guard = (event: BeforeUnloadEvent) => {
      writeLayout(layoutSnapshot.current);
      if (dirtyIds.current.size) {
        event.preventDefault();
        event.returnValue = "";
      }
    };
    window.addEventListener("beforeunload", guard);
    return () => window.removeEventListener("beforeunload", guard);
  }, []);
  const openText = async (workspaceId: string, id: string, file: string) => {
    captureEditor();
    setEditorCollapsed(false);
    const existing = documents.find(
      (d) =>
        d.workspaceId === workspaceId && d.sessionId === id && d.path === file,
    );
    if (existing) {
      selectEditor(workspaceId, id, file);
      return;
    }
    if (opening.current) return;
    if (documents.length >= 12)
      return showError(new Error("最多打开 12 个编辑文件，请先关闭部分文件"));
    opening.current = true;
    try {
      const result = await window.forterm.readText(id, file);
      setDocuments((old) => [
        ...old,
        { ...result, workspaceId, sessionId: id, saved: result.text },
      ]);
      selectEditor(workspaceId, id, file);
    } catch (e) {
      showError(e);
    } finally {
      opening.current = false;
    }
  };
  const newText = (
    workspaceId: string,
    fileSessionId: string,
    path: string,
  ) => {
    captureEditor();
    setEditorCollapsed(false);
    const exists = documents.find(
      (d) =>
        d.workspaceId === workspaceId &&
        d.sessionId === fileSessionId &&
        d.path === path,
    );
    if (!exists)
      setDocuments((old) => [
        ...old,
        {
          workspaceId,
          sessionId: fileSessionId,
          path,
          text: "",
          saved: "",
          revision: "",
          mode: 0o600,
          isNew: true,
        },
      ]);
    selectEditor(workspaceId, fileSessionId, path);
  };
  useEffect(() => closeContextMenu(), [active, fileId]);
  useEffect(() => {
    const report = (e: Event) => setToast((e as CustomEvent).detail);
    window.addEventListener("forterm-menu-error", report);
    return () => window.removeEventListener("forterm-menu-error", report);
  }, []);
  const updateDocument = (
    doc: EditorDocument,
    patch: Partial<EditorDocument>,
  ) =>
    setDocuments((old) =>
      old.map((d) =>
        d.workspaceId === doc.workspaceId &&
        d.sessionId === doc.sessionId &&
        d.path === doc.path
          ? { ...d, ...patch }
          : d,
      ),
    );
  const saveDocument = async (doc: EditorDocument) => {
    updateDocument(doc, { busy: true, error: "" });
    try {
      const result = doc.isNew
        ? await window.forterm.createText(doc.sessionId, doc.path, doc.text)
        : await window.forterm.saveText(
            doc.sessionId,
            doc.path,
            doc.text,
            doc.revision,
          );
      updateDocument(doc, {
        ...result,
        isNew: false,
        saved: result.text,
        busy: false,
      });
      setToast("文件已保存");
    } catch (e) {
      updateDocument(doc, { busy: false, error: errorText(e) });
    }
  };
  const reloadDocument = async (doc: EditorDocument) => {
    if (
      (doc.isNew || doc.text !== doc.saved) &&
      !confirm("重新读取将丢弃此文件的未保存修改，继续？")
    )
      return;
    updateDocument(doc, { busy: true, error: "" });
    try {
      const result = await window.forterm.readText(doc.sessionId, doc.path);
      updateDocument(doc, {
        ...result,
        isNew: false,
        saved: result.text,
        busy: false,
      });
    } catch (e) {
      updateDocument(doc, { busy: false, error: errorText(e) });
    }
  };
  const closeDocument = (doc: EditorDocument) => {
    if (
      (doc.isNew || doc.text !== doc.saved) &&
      !confirm("此文件尚未保存，丢弃修改并关闭？")
    )
      return;
    setDocuments((old) => old.filter((d) => d !== doc));
  };
  const close = async (id: string) => {
    try {
      if (!(await window.forterm.close(id))) return false;
      setSessions((old) => old.filter((s) => s.id !== id));
      if (active === id)
        setActive(sessions.filter((s) => s.id !== id).at(-1)?.id || "");
      return true;
    } catch (e) {
      showError(e);
      return false;
    }
  };
  const answer = async (value: string[] | null) => {
    if (!prompt) return;
    try {
      await window.forterm.answer(prompt.id, value);
      setPrompt(null);
      const b = await window.forterm.bootstrap();
      setPrompt(b.prompts[0] || null);
    } catch (e) {
      showError(e);
    }
  };
  useEffect(() => {
    const keydown = (e: KeyboardEvent) => {
      if (!(e.ctrlKey || e.metaKey) || !e.shiftKey || e.altKey) return;
      if (
        form ||
        prompt ||
        showKeepAlive ||
        help ||
        showBastionAccounts ||
        window.document.querySelector('[role="menu"],.terminal-paste')
      )
        return;
      const key =
        e.code === "BracketLeft"
          ? "["
          : e.code === "BracketRight"
            ? "]"
            : e.key.toLowerCase();
      if (!["n", "l", "b", "w", "[", "]"].includes(key)) return;
      e.preventDefault();
      e.stopPropagation();
      if (key === "n") setForm({});
      if (key === "l")
        void window.forterm.local().then(select).catch(showError);
      if (key === "b") changeLayout("filesVisible", !layout.filesVisible);
      if (key === "w" && active) void close(active);
      if (key === "[" || key === "]") {
        const index = visibleTabs.findIndex((item) => item.id === active);
        const next =
          visibleTabs[
            (index + (key === "]" ? 1 : -1) + visibleTabs.length) %
              visibleTabs.length
          ];
        if (next) select(next.id);
      }
    };
    window.addEventListener("keydown", keydown, true);
    return () => window.removeEventListener("keydown", keydown, true);
  }, [
    active,
    visibleTabs,
    layout.filesVisible,
    form,
    prompt,
    showKeepAlive,
    help,
    showBastionAccounts,
  ]);
  return (
    <div
      ref={rootRef}
      onContextMenu={inputContextMenu}
      onPasteCapture={(event) => {
        const target = event.target as HTMLInputElement | HTMLTextAreaElement;
        if (
          target.closest(".terminal-canvas,.code-editor-host") ||
          !["INPUT", "TEXTAREA"].includes(target.tagName)
        )
          return;
        const text = event.clipboardData.getData("text/plain");
        const replaced =
          (target.selectionEnd ?? 0) - (target.selectionStart ?? 0);
        if (
          !text ||
          new TextEncoder().encode(text).length > 1024 * 1024 ||
          (target.maxLength > 0 &&
            target.value.length - replaced + text.length > target.maxLength)
        ) {
          event.preventDefault();
          setToast(
            !text ? "剪贴板没有文本" : "粘贴文本超过输入框或 1 MiB 长度上限",
          );
        }
      }}
      className={"app " + (light ? "light" : "")}
    >
      <aside
        id="connection-navigation"
        className={"sidebar " + (!layout.navigationVisible ? "hidden" : "")}
        style={{ width: sidebarWidth }}
      >
        <div className="brand">
          <div className="brand-mark">
            <TerminalSquare size={23} />
          </div>
          <div>
            Forterm<span>YOUR CONNECTION WORKSPACE</span>
          </div>
          <span className="version">{APP_VERSION}</span>
          <button
            className="sidebar-collapse"
            aria-expanded={true}
            aria-controls="connection-navigation"
            aria-label="收起连接栏"
            title="收起连接栏"
            onClick={toggleNavigation}
          >
            <PanelLeftClose size={16} />
          </button>
        </div>
        <div className="workspace-label">
          <span className="workspace-dot" />
          本地工作区
          <ChevronDown size={14} />
        </div>
        <button className="new-connection" onClick={() => setForm({})}>
          <Plus size={17} />
          新建连接<span>SSH</span>
        </button>
        <div className="sidebar-search">
          <Search size={15} />
          <input
            aria-label="搜索连接"
            placeholder="搜索连接…"
            value={filter}
            onChange={(e) => setFilter(e.target.value)}
          />
        </div>
        <div className="sidebar-section-label">
          连接 <span>{profiles.length}</span>
        </div>
        <div
          className="connection-tree"
          role="region"
          aria-label="保存的连接"
          tabIndex={0}
        >
          <div className="bastion-group-heading">
            <button
              className="group-title"
              aria-label="折叠堡垒机分组"
              aria-expanded={!bastionCollapsed || !!filter}
              onClick={() => setBastionCollapsed((old) => !old)}
            >
              <ChevronDown
                size={12}
                style={{
                  transform:
                    bastionCollapsed && !filter ? "rotate(-90deg)" : undefined,
                }}
              />
              <ShieldCheck size={14} />
              堡垒机<small>{matchingBastions.length}</small>
            </button>
            <button
              title="新增堡垒机"
              aria-label="新增堡垒机"
              onClick={() => {
                setBastionTarget({ mode: "new" });
                setShowBastionAccounts(true);
              }}
            >
              <Plus size={14} />
            </button>
          </div>
          {(!bastionCollapsed || !!filter) &&
            matchingBastions.map((a) => (
              <div className="saved-bastion" key={a.id}>
                <button
                  aria-label={`配置堡垒机 ${a.name}`}
                  title={`${a.name} · ${a.username} · ${a.portalUrl}`}
                  onClick={() => {
                    setBastionTarget({ mode: "edit", id: a.id });
                    setShowBastionAccounts(true);
                  }}
                >
                  <ShieldCheck size={16} />
                  <span>
                    <strong>{a.name}</strong>
                    <small>
                      {a.username} · {a.portalUrl}
                    </small>
                    <small>
                      {a.hasPassword ? "密码已保存" : "无保存密码"} ·{" "}
                      {a.hasTotpSecret ? "Key已保存" : "未配置Key"} · 待适配
                    </small>
                  </span>
                </button>
              </div>
            ))}
          <div className="sidebar-section-label">普通 SSH / 文件连接</div>
          {groups.map((g) => (
            <div key={g}>
              <button
                className="group-title"
                aria-expanded={!collapsed.includes(g) || !!filter}
                onClick={() =>
                  setCollapsed((old) =>
                    old.includes(g) ? old.filter((v) => v !== g) : [...old, g],
                  )
                }
              >
                <ChevronDown
                  size={12}
                  style={{
                    transform:
                      collapsed.includes(g) && !filter
                        ? "rotate(-90deg)"
                        : undefined,
                  }}
                />
                <Folder size={14} />
                {g}
                <small>{matching.filter((p) => p.group === g).length}</small>
              </button>
              {matching
                .filter(
                  (p) => p.group === g && (!collapsed.includes(g) || !!filter),
                )
                .map((p) => (
                  <div className="saved-connection" key={p.id}>
                    <button
                      onClick={() =>
                        void window.forterm
                          .connectProfile(p.id)
                          .then(select)
                          .catch(showError)
                      }
                    >
                      <Server size={16} />
                      <span>
                        {p.title || p.host}
                        <small>
                          {p.host}:{p.port}
                        </small>
                      </span>
                    </button>
                    <div className="profile-actions">
                      <button
                        aria-label="编辑连接"
                        onClick={() => setForm({ profile: p })}
                      >
                        <Pencil size={12} />
                      </button>
                      <button
                        aria-label="删除连接"
                        onClick={() => {
                          if (confirm("删除这个保存的连接？"))
                            void window.forterm
                              .deleteProfile(p.id)
                              .then(setProfiles)
                              .catch(showError);
                        }}
                      >
                        <Trash2 size={12} />
                      </button>
                    </div>
                  </div>
                ))}
            </div>
          ))}
          {!!(profiles.length + bastionAccounts.length) &&
            !matching.length &&
            !matchingBastions.length && (
              <p className="tree-empty">没有匹配的连接，试试名称、地址或分组</p>
            )}
          {!profiles.length && !bastionAccounts.length && (
            <div className="tree-empty">
              <Server size={22} />
              <p>还没有保存的连接</p>
              <small>
                添加常用服务器，或从
                <br />
                堡垒机控制器打开临时会话
              </small>
            </div>
          )}
        </div>
        <div
          className="sidebar-tools"
          role="region"
          aria-label="工作区工具"
          tabIndex={0}
        >
          <button
            onClick={() => {
              setBastionTarget({ mode: "list" });
              setShowBastionAccounts(true);
            }}
          >
            <ShieldCheck size={17} />
            堡垒机账户
          </button>
          <button onClick={() => setHelp(true)}>
            <Command size={17} />
            快捷键与操作
          </button>
          <button onClick={() => setShowKeepAlive(true)}>
            <Activity size={17} />
            会话心跳
            <span className="count">
              {keepAlive.enabled ? `${keepAlive.intervalSeconds}s` : "关闭"}
            </span>
          </button>
          <button
            className={mode === "diagnosis" ? "selected" : ""}
            onClick={() => setMode("diagnosis")}
          >
            <ScanLine size={17} />
            启动诊断
            {reports.length > 0 && (
              <span className="count">{reports.length}</span>
            )}
          </button>
          <button
            onClick={() =>
              void window.forterm.local().then(select).catch(showError)
            }
          >
            <TerminalSquare size={17} />
            本地终端
          </button>
          <div className="import-export">
            <button
              onClick={() =>
                void window.forterm
                  .importProfiles()
                  .then(setProfiles)
                  .catch(showError)
              }
            >
              <ArrowDownToLine size={14} />
              导入
            </button>
            <button
              onClick={() =>
                void window.forterm.exportProfiles().catch(showError)
              }
            >
              <ArrowUpFromLine size={14} />
              导出
            </button>
          </div>
        </div>
        <div className="sidebar-footer">
          <ShieldCheck size={15} />
          <span>凭据保存在本机</span>
          <button title="切换主题" onClick={() => setLight(!light)}>
            {light ? <Moon size={16} /> : <Sun size={16} />}
          </button>
        </div>
      </aside>
      {layout.navigationVisible && (
        <ResizeHandle
          label="连接栏宽度"
          axis="x"
          value={sidebarWidth}
          bounds={sidebarBounds}
          onChange={(n) => changeLayout("sidebar", n)}
          onReset={() => changeLayout("sidebar", DEFAULT_LAYOUT.sidebar)}
        />
      )}
      <main className="main">
        <header className="topbar">
          {!layout.navigationVisible && (
            <button
              className="navigation-reveal"
              aria-expanded={false}
              aria-controls="connection-navigation"
              aria-label="展开连接栏"
              title="展开连接栏"
              onClick={toggleNavigation}
            >
              <PanelLeftOpen size={16} />
              <span>展开连接栏</span>
            </button>
          )}
          <div className="breadcrumb">
            工作区<span>/</span>
            <strong>
              {mode === "diagnosis" ? "启动诊断" : current?.title || "概览"}
            </strong>
          </div>
          <div
            className="topbar-actions"
            role="toolbar"
            aria-label="工作区布局工具"
            tabIndex={0}
          >
            <button title="重置布局" onClick={resetLayout}>
              <Settings2 size={17} />
            </button>
            <button
              title="减小字体"
              onClick={() => setFontSize((n) => Math.max(10, n - 1))}
            >
              A−
            </button>
            <button
              title="增大字体"
              onClick={() => setFontSize((n) => Math.min(24, n + 1))}
            >
              A+
            </button>
            <i />
            <button
              title="终端分屏"
              disabled={
                visibleTabs.filter(
                  (s) =>
                    s.protocol === "ssh" ||
                    s.protocol === "local" ||
                    s.terminalStatus,
                ).length < 2 ||
                ((current?.protocol === "sftp" ||
                  current?.protocol === "ftp") &&
                  !current.terminalStatus)
              }
              className={split ? "active" : ""}
              onClick={() => setSplit(!split)}
            >
              <Columns2 size={17} />
            </button>
            {second && (
              <>
                <select
                  className="split-select"
                  aria-label="分屏会话"
                  value={second.id}
                  onChange={(e) => setSplitId(e.target.value)}
                >
                  {splitCandidates.map((s) => (
                    <option key={s.id} value={s.id}>
                      {s.title}
                    </option>
                  ))}
                </select>
                <button
                  title="切换分屏方向"
                  onClick={() => setHorizontal((v) => !v)}
                >
                  {horizontal ? "上下" : "左右"}
                </button>
              </>
            )}
            <button
              title="切换文件面板"
              aria-label="切换文件面板"
              className={layout.filesVisible ? "active" : ""}
              onClick={() => changeLayout("filesVisible", !layout.filesVisible)}
            >
              <Files size={18} />
            </button>
            <button
              title="切换监控面板"
              aria-label="切换监控面板"
              className={layout.monitorVisible ? "active" : ""}
              onClick={() =>
                changeLayout("monitorVisible", !layout.monitorVisible)
              }
            >
              <Activity size={18} />
            </button>
            <button
              title="左侧文件、右侧监控"
              onClick={() =>
                setLayout((old) => ({
                  ...old,
                  filesVisible: true,
                  monitorVisible: true,
                }))
              }
            >
              同时显示
            </button>
          </div>
        </header>
        {mode === "diagnosis" ? (
          <section className="diagnosis">
            <div className="eyebrow">LAUNCH INSPECTOR</div>
            <h1>看清控制器如何启动会话</h1>
            <p className="lead">
              将控制器的客户端路径指向 <code>forterm-diagnose.exe</code>
              ，然后从堡垒机打开一次会话。
            </p>
            <div className="diagnosis-note">
              <ShieldCheck size={19} />
              <span>
                此入口不连接服务器。报告隐藏用户名、密码、地址及未知参数值。
              </span>
            </div>
            {reports.length ? (
              <>
                {reports
                  .slice()
                  .reverse()
                  .map((r, i) => (
                    <article className="report-card" key={r.id ?? r.time + i}>
                      <header>
                        <div>
                          <strong>{r.entry}</strong>
                          <small>
                            {new Date(r.time).toLocaleString("zh-CN", {
                              year: "numeric",
                              month: "2-digit",
                              day: "2-digit",
                              hour: "2-digit",
                              minute: "2-digit",
                              second: "2-digit",
                              fractionalSecondDigits: 3,
                            })}
                          </small>
                        </div>
                        <span
                          className={"badge " + (r.supported ? "ok" : "warn")}
                        >
                          {r.supported ? "已识别" : "需要适配"}
                        </span>
                        <button
                          className="secondary"
                          onClick={() =>
                            void window.forterm
                              .exportReport(reports.length - 1 - i)
                              .catch(showError)
                          }
                        >
                          <ArrowDownToLine size={14} />
                          导出报告
                        </button>
                      </header>
                      <p>{r.summary}</p>
                      <p className="invocation-count">
                        收到 {r.invocations ?? 1} 次调用
                        {(r.invocations ?? 1) > 1 &&
                          ` · 已合并 ${(r.invocations ?? 1) - 1} 次相同连接的重复调用`}
                      </p>
                      <table>
                        <thead>
                          <tr>
                            <th>序号</th>
                            <th>类型</th>
                            <th>脱敏后的内容</th>
                          </tr>
                        </thead>
                        <tbody>
                          {r.arguments.map((a) => (
                            <tr key={a.index}>
                              <td>{a.index + 1}</td>
                              <td>{a.kind}</td>
                              <td>
                                <code>{a.value}</code>
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                      {!r.arguments.length && (
                        <p className="subtle">本次启动未携带参数。</p>
                      )}
                    </article>
                  ))}
              </>
            ) : (
              <div className="diagnostic-empty">
                <ScanLine size={38} />
                <h3>等待来自控制器的调用</h3>
                <p>点击控制器的连接按钮后，参数结构会显示在这里。</p>
                <div className="code-line">
                  forterm-diagnose.exe -url "ssh://…"
                </div>
              </div>
            )}
          </section>
        ) : (
          <>
            <nav className="tabs">
              {visibleTabs.map((s) => (
                <div
                  key={s.id}
                  data-session-id={s.id}
                  tabIndex={0}
                  onContextMenu={(e) => {
                    e.preventDefault();
                    openContextMenu(e.clientX, e.clientY, [
                      {
                        label: "关闭当前标签",
                        action: () => {
                          void close(s.id);
                        },
                      },
                      {
                        label: "关闭其他标签",
                        disabled: visibleTabs.length < 2,
                        action: async () => {
                          for (const other of visibleTabs.filter(
                            (item) => item.id !== s.id,
                          ))
                            if (!(await close(other.id))) break;
                        },
                      },
                    ]);
                  }}
                  onKeyDown={(e) =>
                    menuKey(e, e.currentTarget, [
                      {
                        label: "关闭当前标签",
                        action: () => {
                          void close(s.id);
                        },
                      },
                      {
                        label: "关闭其他标签",
                        disabled: visibleTabs.length < 2,
                        action: async () => {
                          for (const other of visibleTabs.filter(
                            (item) => item.id !== s.id,
                          ))
                            if (!(await close(other.id))) break;
                        },
                      },
                    ])
                  }
                  className={"tab " + (active === s.id ? "selected" : "")}
                >
                  <button onClick={() => select(s.id)}>
                    <span className={"status-dot " + s.status} />
                    {s.protocol === "sftp" || s.protocol === "ftp" ? (
                      <Files size={14} />
                    ) : (
                      <TerminalSquare size={14} />
                    )}
                    <span>{s.title}</span>
                  </button>
                  <button
                    aria-label="关闭会话"
                    onClick={() => void close(s.id)}
                  >
                    <X size={13} />
                  </button>
                </div>
              ))}
              <button
                className="tab-add"
                title="新建连接"
                onClick={() => setForm({})}
              >
                <Plus size={16} />
              </button>
            </nav>
            <div className="workspace-body" ref={columnsRef}>
              <aside
                className={
                  "workspace-files-pane " + (!filesShown ? "hidden" : "")
                }
                style={{ width: panes.fileWidth }}
                aria-label="左侧文件区"
              >
                {fileWorkspaces.map((workspace) => (
                  <WorkspaceFiles
                    key={workspace.id}
                    workspace={workspace}
                    sessions={sessions}
                    group={groupFor(workspace.id)}
                    visible={filesShown && workspace.id === active}
                    onSelect={(id) => changeSource(workspace.id, id)}
                    onAttach={(id) => changeSource(workspace.id, id, true)}
                    onDetach={(id) => {
                      captureEditor();
                      void window.forterm
                        .detachFileSource(workspace.id, id)
                        .catch(showError);
                    }}
                    onNew={() =>
                      setForm({
                        initialProtocol: "sftp",
                        associateWith: workspace.id,
                      })
                    }
                    onCollapse={() => changeLayout("filesVisible", false)}
                    onError={showError}
                    onEdit={(id, path) => void openText(workspace.id, id, path)}
                    onNewFile={(id, path) => newText(workspace.id, id, path)}
                  />
                ))}
              </aside>
              {filesShown && (
                <ResizeHandle
                  label="文件面板宽度"
                  axis="x"
                  value={panes.fileWidth}
                  bounds={panes.fileBounds}
                  onChange={(n) =>
                    setLayout((old) => ({
                      ...old,
                      filesWidth: n,
                      monitorWidth: monitorShown
                        ? Math.min(
                            old.monitorWidth,
                            Math.max(0, panes.budget - n),
                          )
                        : old.monitorWidth,
                    }))
                  }
                  onReset={() =>
                    changeLayout("filesWidth", DEFAULT_LAYOUT.filesWidth)
                  }
                />
              )}
              <div
                className={
                  "workspace-stack editor-dock-" +
                  effectiveEditorDock +
                  (!document
                    ? " no-editor"
                    : editorCollapsed
                      ? " editor-collapsed"
                      : "")
                }
                ref={stackRef}
                style={
                  {
                    "--editor-width": `${editorWidth}px`,
                    "--editor-height": `${editorHeight + 32}px`,
                  } as CSSProperties
                }
              >
                <div className="work-area" ref={workRef}>
                  <div
                    ref={terminalRef}
                    style={
                      {
                        "--first-ratio": `${terminalRatio}fr`,
                        "--second-ratio": `${1 - terminalRatio}fr`,
                      } as CSSProperties
                    }
                    className={
                      "terminal-area " +
                      (second
                        ? "split " + (horizontal ? "split-horizontal" : "")
                        : "")
                    }
                  >
                    {!current && (
                      <div className="welcome">
                        <div className="welcome-kicker">
                          <span />
                          READY TO CONNECT
                        </div>
                        <h1>
                          每一个连接，
                          <br />
                          <span>都井然有序。</span>
                        </h1>
                        <p>
                          终端、文件与服务器状态，在一个工作区里。
                          <br />
                          从常用连接开始，或接收堡垒机控制器的会话。
                        </p>
                        <div className="welcome-actions">
                          <button
                            className="primary"
                            onClick={() => setForm({})}
                          >
                            <Plus size={17} />
                            新建连接
                          </button>
                          <button
                            className="secondary"
                            onClick={() =>
                              void window.forterm
                                .local()
                                .then(select)
                                .catch(showError)
                            }
                          >
                            <TerminalSquare size={16} />
                            打开本地终端
                          </button>
                        </div>
                        <div className="welcome-cards">
                          <button onClick={() => setMode("diagnosis")}>
                            <ScanLine size={21} />
                            <strong>连接你的堡垒机</strong>
                            <span>通过脱敏诊断确认启动参数</span>
                            <ArrowUpRight size={16} />
                          </button>
                          <button
                            onClick={() =>
                              void window.forterm
                                .importProfiles()
                                .then(setProfiles)
                                .catch(showError)
                            }
                          >
                            <FileJson size={21} />
                            <strong>带上你的连接</strong>
                            <span>导入 Forterm 连接配置</span>
                            <ArrowUpRight size={16} />
                          </button>
                        </div>
                        <div className="welcome-foot">
                          <ShieldCheck size={14} />
                          控制器临时凭据仅在内存使用
                        </div>
                      </div>
                    )}
                    {sessions
                      .filter(
                        (s) =>
                          s.protocol === "ssh" ||
                          s.protocol === "local" ||
                          s.terminalStatus,
                      )
                      .map((s) => (
                        <TerminalView
                          key={s.id}
                          session={s}
                          pane={
                            second && s.id !== firstTerminal
                              ? "second"
                              : "first"
                          }
                          visible={
                            (s.id === active || s.id === second?.id) &&
                            (s.protocol !== "sftp" || !!s.terminalStatus)
                          }
                          light={light}
                          fontSize={fontSize}
                          focused={s.id === active}
                          onActivate={() => select(s.id)}
                        />
                      ))}
                    {second && (
                      <ResizeHandle
                        area="divider"
                        label="双终端分屏比例"
                        axis={horizontal ? "x" : "y"}
                        ratio
                        value={terminalRatio}
                        bounds={terminalBounds}
                        onChange={(n) => changeLayout(terminalRatioKey, n)}
                        onReset={() => changeLayout(terminalRatioKey, 0.5)}
                      />
                    )}
                    {(current?.protocol === "sftp" ||
                      current?.protocol === "ftp") &&
                      !current.terminalStatus && (
                        <div className="sftp-workspace">
                          <CompactToolbar
                            label="会话工具栏"
                            className="sftp-heading"
                          >
                            <Files size={18} />
                            <strong
                              className="toolbar-title"
                              title={current.title}
                            >
                              {current.title}
                            </strong>
                            <span
                              className="toolbar-meta"
                              title={current.message || current.status}
                            >
                              {current.status === "ready"
                                ? current.protocol === "ftp"
                                  ? "FTP 明文文件管理"
                                  : "SFTP 文件管理"
                                : current.message || "正在连接…"}
                            </span>
                            <button
                              className="secondary"
                              aria-label="此会话的资源监控"
                              title={
                                current.protocol === "ftp"
                                  ? "FTP 不支持 SSH 监控；可在终端关联此文件连接"
                                  : "此会话的资源监控"
                              }
                              disabled={current.protocol === "ftp"}
                              onClick={() => {
                                changeLayout("monitorVisible", true);
                              }}
                            >
                              <Activity size={15} />
                              监控
                            </button>
                            <button
                              className="secondary"
                              aria-label="在此会话打开终端"
                              title="在此会话打开终端"
                              disabled={
                                current.status !== "ready" ||
                                current.protocol === "ftp"
                              }
                              onClick={() =>
                                void window.forterm
                                  .openTerminal(current.id)
                                  .catch(showError)
                              }
                            >
                              <TerminalSquare size={15} />
                              终端
                            </button>
                          </CompactToolbar>
                          <FilePanel
                            onNewFile={(file) =>
                              newText(active, current.id, file)
                            }
                            onEdit={(file) =>
                              void openText(active, current.id, file)
                            }
                            key={current.id}
                            session={current}
                            onError={showError}
                          />
                        </div>
                      )}
                  </div>
                </div>
                {document && !editorCollapsed && (
                  <ResizeHandle
                    area="divider"
                    label={
                      effectiveEditorDock === "bottom"
                        ? "编辑器高度"
                        : "编辑器宽度"
                    }
                    axis={effectiveEditorDock === "bottom" ? "y" : "x"}
                    reverse={effectiveEditorDock !== "left"}
                    value={
                      effectiveEditorDock === "bottom"
                        ? editorHeight
                        : editorWidth
                    }
                    bounds={
                      effectiveEditorDock === "bottom"
                        ? editorBounds
                        : editorWidthBounds
                    }
                    onChange={(n) => {
                      captureEditor();
                      changeLayout(
                        effectiveEditorDock === "bottom"
                          ? "editor"
                          : "editorWidth",
                        n,
                      );
                    }}
                    onReset={() =>
                      changeLayout(
                        effectiveEditorDock === "bottom"
                          ? "editor"
                          : "editorWidth",
                        effectiveEditorDock === "bottom"
                          ? DEFAULT_LAYOUT.editor
                          : DEFAULT_LAYOUT.editorWidth,
                      )
                    }
                  />
                )}
                <div
                  className={"editor-dock " + (!document ? "hidden" : "")}
                  ref={editorDockRef}
                >
                  {documents.some((d) => d.workspaceId === active) && (
                    <nav className="editor-tabs" aria-label="编辑文件标签">
                      {documents
                        .filter((d) => d.workspaceId === active)
                        .map((d) => (
                          <button
                            key={`${d.sessionId}:${d.path}`}
                            data-file-source-id={d.sessionId}
                            data-document-path={d.path}
                            className={
                              document?.path === d.path &&
                              document.sessionId === d.sessionId
                                ? "selected"
                                : ""
                            }
                            onClick={() => {
                              captureEditor();
                              selectEditor(d.workspaceId, d.sessionId, d.path);
                              setEditorCollapsed(false);
                            }}
                            title={`${sessions.find((s) => s.id === d.sessionId)?.title ?? "已关闭连接"} · ${d.path}`}
                          >
                            {d.path.split("/").at(-1)}
                            <small className="editor-source-label">
                              {sessions.find((s) => s.id === d.sessionId)
                                ?.title ?? "已关闭"}
                            </small>
                            {d.isNew || d.text !== d.saved ? " · 未保存" : ""}
                          </button>
                        ))}
                      <button
                        className="editor-toggle"
                        title={editorCollapsed ? "展开编辑器" : "收起编辑器"}
                        onClick={collapseEditor}
                      >
                        <ChevronDown
                          size={14}
                          style={{
                            transform: editorCollapsed
                              ? "rotate(180deg)"
                              : undefined,
                          }}
                        />
                        {editorCollapsed ? "展开" : "收起"}
                      </button>
                    </nav>
                  )}
                  {document && (
                    <div
                      className={
                        "editor-region " + (editorCollapsed ? "hidden" : "")
                      }
                      style={{ flex: 1 }}
                    >
                      <RemoteEditor
                        document={document}
                        stateCache={codeStateCache.current}
                        connected={
                          sessions.find((s) => s.id === document.sessionId)
                            ?.status === "ready"
                        }
                        onChange={(text) => updateDocument(document, { text })}
                        onSave={() => void saveDocument(document)}
                        onReload={() => void reloadDocument(document)}
                        blocked={fileOperations.some(
                          (o) =>
                            o.sessionId === document.sessionId &&
                            o.state === "running",
                        )}
                        light={light}
                        dock={layout.editorDock}
                        adaptiveDock={effectiveEditorDock !== layout.editorDock}
                        sourceTitle={
                          sessions.find((s) => s.id === document.sessionId)
                            ?.title ?? "原文件连接已关闭"
                        }
                        onDock={(dock) => {
                          captureEditor();
                          setLayout((old) => ({ ...old, editorDock: dock }));
                        }}
                        onClose={() => closeDocument(document)}
                      />
                    </div>
                  )}
                </div>
              </div>
              {monitorShown && (
                <ResizeHandle
                  label="监控面板宽度"
                  axis="x"
                  reverse
                  value={panes.monitorWidth}
                  bounds={panes.monitorBounds}
                  onChange={(n) =>
                    setLayout((old) => ({
                      ...old,
                      monitorWidth: n,
                      filesWidth: filesShown
                        ? Math.min(
                            old.filesWidth,
                            Math.max(0, panes.budget - n),
                          )
                        : old.filesWidth,
                    }))
                  }
                  onReset={() =>
                    changeLayout("monitorWidth", DEFAULT_LAYOUT.monitorWidth)
                  }
                />
              )}
              <aside
                className={
                  "workspace-monitor-pane " + (!monitorShown ? "hidden" : "")
                }
                style={{ width: panes.monitorWidth }}
                aria-label="右侧监控区"
              >
                {sessions
                  .filter((s) => s.protocol === "ssh" || s.protocol === "sftp")
                  .map((s) => (
                    <div
                      className={
                        "workspace-monitor-view " +
                        (s.id !== active ? "hidden" : "")
                      }
                      key={s.id}
                    >
                      <MonitorPanel
                        session={s}
                        visible={monitorShown && s.id === active}
                        onCollapse={() => changeLayout("monitorVisible", false)}
                      />
                    </div>
                  ))}
              </aside>
            </div>
          </>
        )}
        {transfers.length > 0 && (
          <details className="transfers">
            <summary>
              文件传输{" "}
              <span>
                {transfers.filter((t) => t.state === "running").length} 进行中 ·{" "}
                {transfers.length} 总计
              </span>
            </summary>
            <button
              className="clear-transfers"
              onClick={() =>
                void window.forterm.clearTransfers().catch(showError)
              }
            >
              清理已结束记录
            </button>
            <div>
              {transfers
                .slice()
                .reverse()
                .map((t) => (
                  <div className="transfer" key={t.id}>
                    {t.direction === "upload" ? (
                      <ArrowUpFromLine size={14} />
                    ) : (
                      <ArrowDownToLine size={14} />
                    )}
                    <span>{t.name}</span>
                    <progress value={t.bytes} max={t.total || 1} />
                    <small>
                      {size(t.bytes)} / {size(t.total)} ·{" "}
                      {
                        {
                          queued: "排队中",
                          running: "传输中",
                          done: "完成",
                          error: "失败",
                          cancelled: "已取消",
                        }[t.state]
                      }
                    </small>
                    {t.state === "running" || t.state === "queued" ? (
                      <button
                        title="取消传输"
                        onClick={() =>
                          void window.forterm
                            .cancelTransfer(t.id)
                            .catch(showError)
                        }
                      >
                        <X size={14} />
                      </button>
                    ) : t.state === "done" ? (
                      <Check size={14} />
                    ) : (
                      <span className="transfer-error" title={t.message}>
                        {t.message || "传输未完成"}
                      </span>
                    )}
                  </div>
                ))}
            </div>
          </details>
        )}
        <footer className="statusbar">
          <div>
            <span className="workspace-dot" />
            {sessions.filter((s) => s.status === "ready").length} 个活动会话
            <span className="divider">│</span>
            {current?.temporary ? "堡垒机临时会话" : "本地工作区"}
          </div>
          <div>
            {current?.keepAlive && (
              <>
                <span>
                  {current.keepAlive.enabled
                    ? `心跳 ${current.keepAlive.intervalSeconds}s`
                    : "心跳已关闭"}
                </span>
                <span className="divider">│</span>
              </>
            )}
            UTF-8<span className="divider">│</span>Forterm {APP_VERSION}
          </div>
        </footer>
      </main>
      {showBastionAccounts && (
        <BastionAccountsDialog
          initial={bastionTarget}
          accounts={bastionAccounts}
          onClose={() => setShowBastionAccounts(false)}
          onError={showError}
        />
      )}
      {help && (
        <div className="overlay">
          <section className="modal">
            <header>
              <h2>快捷键与操作</h2>
              <button aria-label="关闭快捷键" onClick={() => setHelp(false)}>
                <X size={20} />
              </button>
            </header>
            <div className="shortcut-list">
              <p>
                <kbd>Ctrl / ⌘ + Shift + N</kbd>新建连接
              </p>
              <p>
                <kbd>Ctrl / ⌘ + Shift + L</kbd>本地终端
              </p>
              <p>
                <kbd>Ctrl / ⌘ + Shift + B</kbd>切换左侧文件面板
              </p>
              <p>
                <kbd>Ctrl / ⌘ + Shift + W</kbd>关闭当前会话
              </p>
              <p>
                <kbd>Ctrl / ⌘ + Shift + [ / ]</kbd>切换会话
              </p>
              <p>
                <kbd>Ctrl / ⌘ + S</kbd>编辑器内保存文件
              </p>
              <p className="subtle">
                分隔条可拖动调整；Tab 聚焦后用方向键调整，Shift 加速，Home/End
                到最小/最大，Enter
                或双击重置。顶部“重置布局”恢复所有尺寸。编辑器可收起而保留草稿。
                文件行上的铅笔打开在线编辑。终端与文件、监控复用同一会话。分屏需要两个终端会话。
              </p>
            </div>
          </section>
        </div>
      )}
      {form && (
        <ConnectionDialog
          profiles={profiles}
          initialProtocol={form.initialProtocol}
          fileOnly={!!form.associateWith}
          profile={form.profile}
          onClose={() => setForm(null)}
          onSaved={setProfiles}
          onConnected={(id) => {
            if (form.associateWith)
              void window.forterm
                .attachFileSource(form.associateWith, id)
                .then(() => {
                  changeLayout("filesVisible", true);
                  select(form.associateWith!);
                })
                .catch(showError);
            else select(id);
          }}
          onError={showError}
        />
      )}
      {showKeepAlive && (
        <KeepAliveDialog
          settings={keepAlive}
          onClose={() => setShowKeepAlive(false)}
          onSave={setKeepAlive}
          onError={showError}
        />
      )}
      {prompt && (
        <div className="overlay">
          <form
            className="modal prompt-modal"
            onSubmit={(e) => {
              e.preventDefault();
              void answer(answers);
            }}
          >
            <div className="prompt-icon">
              {prompt.kind === "host" ? (
                <ShieldCheck size={26} />
              ) : (
                <KeyRound size={26} />
              )}
            </div>
            <h2>{prompt.title}</h2>
            <pre>{prompt.text}</pre>
            {prompt.kind === "host" && (
              <p className="subtle">
                核对服务器指纹后再继续。
                {prompt.changed
                  ? "指纹与之前保存的记录不同，请向管理员确认。"
                  : ""}
              </p>
            )}
            {prompt.fields.map((f, i) => (
              <label key={i}>
                {f.label}
                <input
                  autoFocus={i === 0}
                  type={f.secret ? "password" : "text"}
                  value={answers[i] || ""}
                  onChange={(e) =>
                    setAnswers((a) =>
                      a.map((v, j) => (j === i ? e.target.value : v)),
                    )
                  }
                />
              </label>
            ))}
            <footer>
              <button
                type="button"
                className="secondary"
                onClick={() => void answer(null)}
              >
                取消
              </button>
              <button type="submit" className="primary">
                {prompt.kind === "host" ? "信任并连接" : "继续"}
              </button>
            </footer>
          </form>
        </div>
      )}
      <ContextMenuHost />
      {toast && (
        <div role="alert" className="toast">
          {toast}
          <button aria-label="关闭提示" onClick={() => setToast("")}>
            <X size={15} />
          </button>
        </div>
      )}
    </div>
  );
}
