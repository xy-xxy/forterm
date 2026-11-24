import { useLayoutEffect, useRef } from "react";
import { Files, Plus, X, PanelLeftClose } from "lucide-react";
import type { SessionInfo, FileSourceGroup } from "../shared/types";
import { CompactToolbar } from "./CompactToolbar";
import { FilePanel } from "./FilePanel";
const status = (s: SessionInfo) =>
  s.status === "ready"
    ? s.protocol === "ftp"
      ? "已连接"
      : "SSH就绪"
    : s.status === "connecting"
      ? "连接中"
      : s.status === "error"
        ? "连接失败"
        : "已断开";
export function WorkspaceFiles({
  workspace,
  sessions,
  group,
  visible,
  onSelect,
  onAttach,
  onDetach,
  onNew,
  onCollapse,
  onError,
  onEdit,
  onNewFile,
}: {
  workspace: SessionInfo;
  sessions: SessionInfo[];
  group: FileSourceGroup;
  visible: boolean;
  onSelect: (id: string) => void;
  onAttach: (id: string) => void;
  onDetach: (id: string) => void;
  onNew: () => void;
  onCollapse: () => void;
  onError: (e: unknown) => void;
  onEdit: (id: string, path: string) => void;
  onNewFile: (id: string, path: string) => void;
}) {
  const root = useRef<HTMLElement>(null),
    selected = useRef(group.activeId);
  selected.current = group.activeId;
  useLayoutEffect(() => {
    if (!visible) return;
    let frame = 0;
    const scrollSelected = () => {
      cancelAnimationFrame(frame);
      frame = requestAnimationFrame(() => {
        const button = root.current?.querySelector<HTMLElement>(
          `.file-source-tag[data-file-source-id="${selected.current}"] [role="tab"]`,
        );
        const viewport = button?.closest<HTMLElement>(
          ".compact-toolbar-viewport",
        );
        if (!button || !viewport || !viewport.clientWidth) return;
        const b = button.getBoundingClientRect(),
          v = viewport.getBoundingClientRect();
        if (b.width > v.width)
          viewport.scrollLeft += (b.left + b.right - v.left - v.right) / 2;
        else if (b.left < v.left) viewport.scrollLeft += b.left - v.left;
        else if (b.right > v.right) viewport.scrollLeft += b.right - v.right;
      });
    };
    scrollSelected();
    const observer = new ResizeObserver(scrollSelected);
    if (root.current) observer.observe(root.current);
    return () => {
      cancelAnimationFrame(frame);
      observer.disconnect();
    };
  }, [visible, group.activeId, group.sourceIds.join("|")]);
  const options = sessions.filter(
    (s) =>
      (s.protocol === "sftp" || s.protocol === "ftp") && s.id !== workspace.id,
  );
  return (
    <section
      ref={root}
      className={"workspace-files " + (!visible ? "hidden" : "")}
      aria-label={`文件区 ${workspace.title}`}
      data-workspace-id={workspace.id}
    >
      <CompactToolbar label="文件区工具栏">
        <Files size={15} />
        <strong className="toolbar-title">文件</strong>
        <select
          aria-label="关联文件连接"
          value={group.activeId}
          onChange={(e) => onAttach(e.target.value)}
          title="明确选择关联文件连接，不按IP匹配"
        >
          <option value={workspace.id}>本终端 SFTP</option>
          {options.map((s) => (
            <option
              key={s.id}
              value={s.id}
              disabled={["closed", "error"].includes(s.status)}
            >
              {s.protocol.toUpperCase()} · {s.title} · {status(s)}
            </option>
          ))}
        </select>
        <button
          title="新建独立文件连接"
          aria-label="新建独立文件连接"
          onClick={onNew}
        >
          <Plus size={15} />
        </button>
        <button title="收起文件区" aria-label="收起文件区" onClick={onCollapse}>
          <PanelLeftClose size={15} />
        </button>
      </CompactToolbar>
      <CompactToolbar label="文件会话标签" className="file-source-tabs">
        <div role="tablist" aria-label="关联文件会话">
          {group.sourceIds.map((id) => {
            const s = sessions.find((s) => s.id === id);
            return (
              <div
                className="file-source-tag"
                key={id}
                data-file-source-id={id}
              >
                <button
                  role="tab"
                  aria-selected={id === group.activeId}
                  tabIndex={id === group.activeId ? 0 : -1}
                  onKeyDown={(event) => {
                    if (
                      !["ArrowLeft", "ArrowRight", "Home", "End"].includes(
                        event.key,
                      )
                    )
                      return;
                    event.preventDefault();
                    event.stopPropagation();
                    const index = group.sourceIds.indexOf(id),
                      last = group.sourceIds.length - 1;
                    const next =
                      event.key === "Home"
                        ? 0
                        : event.key === "End"
                          ? last
                          : (index +
                              (event.key === "ArrowRight" ? 1 : -1) +
                              group.sourceIds.length) %
                            group.sourceIds.length;
                    const nextId = group.sourceIds[next];
                    onSelect(nextId);
                    event.currentTarget
                      .closest('[role="tablist"]')
                      ?.querySelector<HTMLElement>(
                        `[data-file-source-id="${nextId}"] [role="tab"]`,
                      )
                      ?.focus();
                  }}
                  title={
                    s
                      ? `${s.title} · ${s.protocol === "ftp" ? "FTP" : "SFTP"} · ${status(s)}`
                      : "原文件连接已关闭"
                  }
                  onClick={() => onSelect(id)}
                >
                  <span className={`status-dot ${s?.status ?? "closed"}`} />
                  <span className="file-source-title">
                    {id === workspace.id ? "本终端" : (s?.title ?? "已关闭")}
                  </span>
                  <small>
                    {s?.protocol === "ftp" ? "FTP" : "SFTP"} ·{" "}
                    {s ? status(s) : "已关闭"}
                  </small>
                </button>
                {id !== workspace.id && (
                  <button
                    aria-label={`解除文件关联 ${s?.title ?? "已关闭"}`}
                    onClick={() => onDetach(id)}
                    title="仅解除关联，不关闭底层连接"
                  >
                    <X size={12} />
                  </button>
                )}
              </div>
            );
          })}
        </div>
      </CompactToolbar>
      <div className="file-source-views">
        {group.sourceIds.map((id) => {
          const s = sessions.find((s) => s.id === id);
          return (
            <div
              key={id}
              className={
                "file-source-view " + (id !== group.activeId ? "hidden" : "")
              }
              data-file-source-id={id}
            >
              {s ? (
                <FilePanel
                  session={s}
                  visible={visible && id === group.activeId}
                  onError={onError}
                  onEdit={(path) => onEdit(id, path)}
                  onNewFile={(path) => onNewFile(id, path)}
                />
              ) : (
                <p>原文件连接已关闭，请重新关联。</p>
              )}
            </div>
          );
        })}
      </div>
    </section>
  );
}
