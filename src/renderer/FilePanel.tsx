import { useEffect, useRef, useState } from "react";
import {
  Folder,
  File,
  RefreshCw,
  ArrowUp,
  Upload,
  Download,
  FolderPlus,
  FilePlus,
  Pencil,
  Trash2,
  FilePenLine,
} from "lucide-react";
import type { RemoteFile, SessionInfo, FileOperation } from "../shared/types";
import { CompactToolbar } from "./CompactToolbar";
import { PermissionsDialog } from "./PermissionsDialog";
import {
  openContextMenu,
  menuKey,
  closeContextMenu,
  type MenuItem,
} from "./ContextMenu";
const join = (a: string, b: string) => (a.endsWith("/") ? a : a + "/") + b;
export const size = (n: number) =>
  n < 1024
    ? n + " B"
    : n < 1024 ** 2
      ? (n / 1024).toFixed(1) + " KB"
      : n < 1024 ** 3
        ? (n / 1024 ** 2).toFixed(1) + " MB"
        : (n / 1024 ** 3).toFixed(1) + " GB";
const errorText = (e: unknown) =>
  String(e instanceof Error ? e.message : e)
    .replace(/^Error invoking remote method '[^']+': Error: /, "")
    .replace(/^Error: /, "");
export function FilePanel({
  session,
  onError,
  onEdit,
  onNewFile,
  visible: panelVisible = true,
}: {
  session: SessionInfo;
  visible?: boolean;
  onError: (e: unknown) => void;
  onEdit: (path: string) => void;
  onNewFile: (path: string) => void;
}) {
  const [path, setPath] = useState("."),
    [input, setInput] = useState("."),
    [files, setFiles] = useState<RemoteFile[]>([]),
    [filter, setFilter] = useState(""),
    [sort, setSort] = useState("name"),
    [showHidden, setShowHidden] = useState(true),
    [busy, setBusy] = useState(false),
    [listed, setListed] = useState(false),
    [error, setError] = useState(""),
    [failed, setFailed] = useState("."),
    [enabled, setEnabled] = useState(
      session.protocol === "sftp" || session.protocol === "ftp",
    ),
    [modifying, setModifying] = useState(false);
  const [edit, setEdit] = useState<{
      type: "mkdir" | "rename" | "newfile";
      name: string;
      parent: string;
    } | null>(null),
    [value, setValue] = useState(""),
    [permissions, setPermissions] = useState<string | null>(null),
    [operation, setOperation] = useState<FileOperation | null>(null);
  const mounted = useRef(true),
    token = useRef<string | null>(null),
    pathRef = useRef("."),
    pending = useRef(false),
    refreshWanted = useRef(false),
    timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined),
    jobs = useRef(new Map<string, string>()),
    ready = useRef(session.status === "ready");
  const visibleRef = useRef(panelVisible);
  visibleRef.current = panelVisible;
  ready.current = session.status === "ready";
  const load = async (p = pathRef.current, automatic = false) => {
    if (token.current)
      void window.forterm
        .cancelFileRequest(session.id, token.current)
        .catch(() => {});
    const request = crypto.randomUUID();
    token.current = request;
    pending.current = true;
    setBusy(true);
    try {
      const result = await window.forterm.list(session.id, p, request);
      if (!mounted.current || token.current !== request) return;
      pathRef.current = result.path;
      setPath(result.path);
      if (!automatic) setInput(result.path);
      setFiles(result.files);
      setListed(true);
      setError("");
    } catch (e) {
      if (
        mounted.current &&
        token.current === request &&
        !/已取消/.test(errorText(e))
      ) {
        setError(errorText(e));
        setFailed(p);
      }
    } finally {
      if (mounted.current && token.current === request) {
        pending.current = false;
        token.current = null;
        setBusy(false);
      }
    }
  };
  const refresh = () => {
    refreshWanted.current = true;
    clearTimeout(timer.current);
    const check = () => {
      if (!mounted.current || !refreshWanted.current || !visibleRef.current)
        return;
      if (
        pending.current ||
        [...jobs.current.values()].some(
          (state) => state === "queued" || state === "running",
        )
      ) {
        timer.current = setTimeout(check, 300);
        return;
      }
      if (!ready.current) return;
      refreshWanted.current = false;
      void load(pathRef.current, true);
    };
    timer.current = setTimeout(check, 300);
  };
  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
      clearTimeout(timer.current);
      if (token.current)
        void window.forterm
          .cancelFileRequest(session.id, token.current)
          .catch(() => {});
      closeContextMenu();
    };
  }, [session.id]);
  useEffect(() => {
    if (enabled && session.status === "ready" && panelVisible) {
      if (!listed) void load(pathRef.current);
      else if (refreshWanted.current) refresh();
    }
  }, [enabled, session.id, session.status, panelVisible]);
  useEffect(() => {
    void window.forterm
      .bootstrap()
      .then((b) => {
        for (const t of b.transfers)
          if (t.sessionId === session.id)
            if (!jobs.current.has(t.id)) jobs.current.set(t.id, t.state);
        setOperation(
          b.fileOperations?.filter((o) => o.sessionId === session.id).at(-1) ??
            null,
        );
      })
      .catch(() => {});
    return window.forterm.onEvent((e) => {
      if (e.type === "transfer" && e.transfer.sessionId === session.id) {
        jobs.current.set(e.transfer.id, e.transfer.state);
        if (
          !["running", "queued"].includes(e.transfer.state) &&
          e.transfer.direction === "upload"
        )
          refresh();
      }
      if (e.type === "files-changed" && e.sessionId === session.id) refresh();
      if (e.type === "file-operation" && e.operation.sessionId === session.id) {
        setOperation(e.operation);
        if (e.operation.state !== "running") refresh();
      }
    });
  }, [session.id]);
  const canRead = session.status === "ready" && !busy && !modifying;
  const available = canRead && listed && operation?.state !== "running";
  const canOpen =
    session.status === "ready" &&
    listed &&
    !modifying &&
    operation?.state !== "running";
  const visible = files
    .filter(
      (f) =>
        (showHidden || !f.name.startsWith(".")) &&
        f.name.toLowerCase().includes(filter.toLowerCase()),
    )
    .sort(
      (a, b) =>
        Number(b.directory) - Number(a.directory) ||
        (sort === "size"
          ? b.size - a.size
          : sort === "modified"
            ? b.modified - a.modified
            : 0) ||
        a.name.localeCompare(b.name, "zh-CN", { numeric: true }),
    );
  const startEdit = (
    type: "mkdir" | "rename" | "newfile",
    name = "",
    parent = pathRef.current,
  ) => {
    setEdit({ type, name, parent });
    setValue(name);
  };
  const modify = async () => {
    if (!edit || modifying) return;
    if (!value || /[\/\\\x00-\x1f]/.test(value) || [".", ".."].includes(value))
      return onError(new Error("请输入有效的单个文件名"));
    setModifying(true);
    try {
      if (edit.type === "newfile") {
        if (files.some((f) => f.name === value))
          throw new Error("同名文件已存在，请使用其他名称");
        onNewFile(join(edit.parent, value));
      } else if (edit.type === "mkdir")
        await window.forterm.mkdir(session.id, join(edit.parent, value));
      else
        await window.forterm.rename(
          session.id,
          join(edit.parent, edit.name),
          join(edit.parent, value),
        );
      setEdit(null);
      if (edit.type !== "newfile") refresh();
    } catch (e) {
      onError(e);
    } finally {
      setModifying(false);
    }
  };
  const remove = (target: string, directory: boolean) =>
    window.forterm
      .remove(session.id, target, directory)
      .then(() => refresh())
      .catch(onError);
  const directoryMenu = (target: string): MenuItem[] => [
    {
      label: "新建文件",
      disabled: !available || session.protocol === "ftp",
      title:
        session.protocol === "ftp"
          ? "FTP 不支持指定文件名的可靠排他创建，请在本地新建后上传"
          : undefined,
      action: () => startEdit("newfile", "", target),
    },
    {
      label: "新建文件夹",
      disabled: !available,
      action: () => startEdit("mkdir", "", target),
    },
    {
      label: "上传文件",
      disabled: !available,
      action: () => window.forterm.upload(session.id, target).catch(onError),
    },
    { label: "刷新", disabled: !canRead, action: () => load(target) },
    {
      label: "复制路径",
      action: () => window.forterm.clipboardWrite(target).catch(onError),
    },
  ];
  const rowMenu = (f: RemoteFile): MenuItem[] => {
    const target = join(path, f.name),
      parent = path;
    return [
      {
        label: f.directory ? "进入目录" : "在线编辑",
        disabled: !canOpen || (!f.directory && session.protocol === "ftp"),
        action: () => {
          if (!mounted.current) return;
          if (f.directory) void load(target);
          else onEdit(target);
        },
      },
      {
        label: "下载",
        disabled: !available || f.directory,
        action: () =>
          window.forterm.download(session.id, target).catch(onError),
      },
      ...(f.directory
        ? directoryMenu(target)
        : [
            {
              label: "复制路径",
              action: () =>
                window.forterm.clipboardWrite(target).catch(onError),
            },
          ]),
      {
        label: "重命名",
        disabled: !available,
        action: () => {
          if (mounted.current) startEdit("rename", f.name, parent);
        },
      },
      {
        label: "权限",
        disabled: !available,
        action: () => {
          if (mounted.current) setPermissions(target);
        },
      },
      {
        label: f.directory ? "删除目录（含子项）" : "删除文件",
        disabled: !available,
        action: () => remove(target, f.directory),
      },
    ];
  };
  if (!enabled)
    return (
      <div className="panel-empty">
        <Folder size={27} />
        <h3>浏览远程文件</h3>
        <p>
          按需打开当前 SSH 的 SFTP，或从上方明确关联独立 FTP/SFTP 文件连接。
        </p>
        <button
          className="secondary"
          disabled={session.status !== "ready"}
          onClick={() => setEnabled(true)}
        >
          打开文件浏览
        </button>
      </div>
    );
  return (
    <div className="file-panel">
      <CompactToolbar label="文件路径筛选与操作" className="file-command-bar">
        <form
          className="toolbar-path"
          onSubmit={(e) => {
            e.preventDefault();
            if (canRead && input.trim()) void load(input);
          }}
        >
          <input
            className="path-input"
            aria-label="远程目录"
            title={input}
            disabled={session.status !== "ready"}
            value={input}
            onChange={(e) => setInput(e.target.value)}
          />
        </form>
        <button
          title="上级目录"
          disabled={!available}
          onClick={() =>
            void load(path.split("/").slice(0, -1).join("/") || "/")
          }
        >
          <ArrowUp size={16} />
        </button>
        <button title="刷新" disabled={!canRead} onClick={() => void load()}>
          <RefreshCw size={16} className={busy ? "spin" : ""} />
        </button>
        <button
          title="上传文件"
          disabled={!available}
          onClick={() =>
            void window.forterm.upload(session.id, path).catch(onError)
          }
        >
          <Upload size={16} />
        </button>
        <button
          title="新建文件"
          disabled={!available || session.protocol === "ftp"}
          onClick={() => startEdit("newfile")}
        >
          <FilePlus size={16} />
        </button>
        <button
          title="新建目录"
          disabled={!available}
          onClick={() => startEdit("mkdir")}
        >
          <FolderPlus size={16} />
        </button>
        <input
          className="toolbar-filter"
          aria-label="筛选文件"
          placeholder="筛选当前目录…"
          value={filter}
          onChange={(e) => setFilter(e.target.value)}
        />
        <select
          className="toolbar-sort"
          aria-label="文件排序"
          value={sort}
          onChange={(e) => setSort(e.target.value)}
        >
          <option value="name">名称</option>
          <option value="size">大小 ↓</option>
          <option value="modified">修改时间 ↓</option>
        </select>
      </CompactToolbar>
      <label className="hidden-files">
        <input
          type="checkbox"
          checked={showHidden}
          onChange={(e) => setShowHidden(e.target.checked)}
        />
        显示隐藏文件
        <span>
          {visible.length} / {files.length} 项
        </span>
      </label>
      {busy && (
        <div className="file-notice" role="status">
          正在读取目录（传输时在文件间隙执行）
          <button
            onClick={() => {
              if (token.current)
                void window.forterm.cancelFileRequest(
                  session.id,
                  token.current,
                );
            }}
          >
            取消读取
          </button>
        </div>
      )}
      {error && (
        <div className="file-notice error" role="alert">
          <span>
            {error}。{listed ? `仍显示最后可用目录 ${path}` : "尚无可用列表"}
          </span>
          <button disabled={!canRead} onClick={() => void load(failed)}>
            重试读取
          </button>
        </div>
      )}
      {operation && (
        <div className="file-notice" role="status">
          <span>{operation.message}</span>
          {operation.state === "running" && (
            <button
              onClick={() =>
                void window.forterm.cancelFileOperation(operation.id)
              }
            >
              取消操作
            </button>
          )}
        </div>
      )}
      <div
        className="file-list"
        tabIndex={0}
        aria-label="远程文件列表"
        onContextMenu={(e) => {
          if ((e.target as HTMLElement).closest(".file-row")) return;
          e.preventDefault();
          openContextMenu(e.clientX, e.clientY, directoryMenu(path));
        }}
        onKeyDown={(e) => {
          if (e.target === e.currentTarget)
            menuKey(e, e.currentTarget, directoryMenu(path));
        }}
      >
        {visible.map((f) => (
          <div
            className="file-row"
            tabIndex={0}
            key={f.name}
            onContextMenu={(e) => {
              e.preventDefault();
              e.stopPropagation();
              openContextMenu(e.clientX, e.clientY, rowMenu(f));
            }}
            onKeyDown={(e) => menuKey(e, e.currentTarget, rowMenu(f))}
            onDoubleClick={() => {
              if (!canOpen) return;
              if (f.directory) void load(join(path, f.name));
              else if (session.protocol === "ftp")
                onError(
                  new Error(
                    "FTP 暂不支持安全在线编辑，请用下载按钮明确下载后修改",
                  ),
                );
              else onEdit(join(path, f.name));
            }}
          >
            {f.directory ? (
              <Folder size={17} className="folder-icon" />
            ) : (
              <File size={17} />
            )}
            <div className="file-name" title={f.name}>
              {f.name}
              <small>
                {f.directory ? "目录" : size(f.size)} ·{" "}
                {new Date(f.modified * 1000).toLocaleDateString("zh-CN")}
              </small>
            </div>
            <div className="file-actions">
              {!f.directory && (
                <>
                  <button
                    title={
                      session.protocol === "ftp"
                        ? "FTP 暂不支持安全在线编辑"
                        : "在线编辑"
                    }
                    aria-label="在线编辑"
                    disabled={!canOpen || session.protocol === "ftp"}
                    onClick={() => onEdit(join(path, f.name))}
                  >
                    <FilePenLine size={13} />
                  </button>
                  <button
                    title="下载"
                    disabled={!available}
                    onClick={() =>
                      void window.forterm
                        .download(session.id, join(path, f.name))
                        .catch(onError)
                    }
                  >
                    <Download size={13} />
                  </button>
                </>
              )}
              <button
                title="重命名"
                disabled={!available}
                onClick={() => startEdit("rename", f.name)}
              >
                <Pencil size={13} />
              </button>
              <button
                title="删除"
                disabled={!available}
                onClick={() => void remove(join(path, f.name), f.directory)}
              >
                <Trash2 size={13} />
              </button>
            </div>
          </div>
        ))}
        {!visible.length && !busy && (
          <p className="subtle empty-label">
            {files.length
              ? "没有匹配的文件"
              : listed
                ? "目录为空"
                : "请重试读取目录"}
          </p>
        )}
      </div>
      {edit && (
        <div className="inline-editor">
          <small title={`${session.title} · ${edit.parent}`}>
            文件连接：{session.title} · 目录：{edit.parent}
          </small>
          <label>
            {edit.type === "mkdir"
              ? "新建目录"
              : edit.type === "newfile"
                ? "新建文件"
                : "重命名"}
            <input
              autoFocus
              aria-label="远程文件名称"
              value={value}
              onChange={(e) => setValue(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === "Enter") void modify();
                if (e.key === "Escape") setEdit(null);
              }}
            />
          </label>
          <div>
            <button
              className="secondary"
              disabled={modifying}
              onClick={() => setEdit(null)}
            >
              取消
            </button>
            <button
              className="primary"
              disabled={modifying || !available}
              onClick={() => void modify()}
            >
              确认名称
            </button>
          </div>
        </div>
      )}
      {permissions && (
        <PermissionsDialog
          sessionId={session.id}
          path={permissions}
          onClose={() => setPermissions(null)}
          onDone={refresh}
          onError={onError}
        />
      )}
    </div>
  );
}
