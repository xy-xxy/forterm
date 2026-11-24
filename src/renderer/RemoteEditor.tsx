import { CodeEditor, languageNames, type EditorLanguage } from "./CodeEditor";
import { textFormat, convertLineEndings } from "./text-format";
import { CompactToolbar } from "./CompactToolbar";
import { useMemo, useState } from "react";
import { Save, RefreshCw, X, FilePenLine } from "lucide-react";
import type { RemoteText } from "../shared/types";
export interface EditorDocument extends RemoteText {
  sessionId: string;
  workspaceId: string;
  isNew?: boolean;
  saved: string;
  busy?: boolean;
  error?: string;
}
export function RemoteEditor({
  document: doc,
  connected,
  onChange,
  onSave,
  onReload,
  onClose,
  dock,
  onDock,
  light,
  blocked = false,
  stateCache,
  sourceTitle,
  adaptiveDock = false,
}: {
  document: EditorDocument;
  sourceTitle?: string;
  adaptiveDock?: boolean;
  connected: boolean;
  onChange: (text: string) => void;
  onSave: () => void;
  onReload: () => void;
  onClose: () => void;
  dock: "right" | "left" | "bottom";
  light: boolean;
  blocked?: boolean;
  stateCache: import("./CodeEditor").EditorStateCache;
  onDock: (dock: "right" | "left" | "bottom") => void;
}) {
  const [language, setLanguage] = useState<EditorLanguage>("auto"),
    [tabSize, setTabSize] = useState(4),
    [useTabs, setUseTabs] = useState(false);
  const [whitespace, setWhitespace] = useState(() => {
    try {
      return localStorage.getItem("forterm.editor.whitespace") === "true";
    } catch {
      return false;
    }
  });
  const [position, setPosition] = useState({ line: 1, column: 1, count: 1 });
  const format = useMemo(() => textFormat(doc.text), [doc.text]);
  const dirty = !!doc.isNew || doc.text !== doc.saved;
  const bytes = useMemo(
    () => new TextEncoder().encode(doc.text).length,
    [doc.text],
  );
  const tooLarge = bytes > 1024 * 1024;
  return (
    <section className="remote-editor" aria-label="在线文本编辑器">
      <CompactToolbar label="编辑器工具栏">
        <FilePenLine size={16} />
        <strong className="toolbar-title" title={doc.path}>
          {doc.path}
        </strong>
        <span className="editor-state">
          {doc.isNew ? "新文件未创建" : dirty ? "未保存" : "已保存"}
        </span>
        <span className="toolbar-meta">UTF-8</span>
        {sourceTitle && (
          <span className="editor-source-label" title={sourceTitle}>
            来源：{sourceTitle}
          </span>
        )}
        {adaptiveDock && (
          <span
            className="toolbar-meta"
            title="窄窗口暂用底部分屏，放大后恢复所选停靠"
          >
            窄窗口底部分屏
          </span>
        )}
        <select
          aria-label="编辑语言"
          title="编辑语言"
          value={language}
          onChange={(e) => setLanguage(e.target.value as EditorLanguage)}
        >
          {Object.entries(languageNames).map(([value, label]) => (
            <option key={value} value={value}>
              {label}
            </option>
          ))}
        </select>
        <label className="editor-render-toggle">
          <input
            aria-label="显示空白与换行符"
            type="checkbox"
            checked={whitespace}
            onChange={(e) => {
              setWhitespace(e.target.checked);
              try {
                localStorage.setItem(
                  "forterm.editor.whitespace",
                  String(e.target.checked),
                );
              } catch {}
            }}
          />
          空白 / 换行
        </label>
        <select
          aria-label="Tab 宽度"
          title="Tab 显示宽度"
          value={tabSize}
          onChange={(e) => setTabSize(Number(e.target.value))}
        >
          {[2, 4, 8].map((n) => (
            <option value={n} key={n}>
              {n} 列
            </option>
          ))}
        </select>
        <select
          aria-label="缩进方式"
          title="新输入的缩进，不转换原内容"
          value={useTabs ? "tab" : "spaces"}
          onChange={(e) => setUseTabs(e.target.value === "tab")}
        >
          <option value="spaces">空格缩进</option>
          <option value="tab">Tab 缩进</option>
        </select>
        <select
          aria-label="编辑器停靠位置"
          title="编辑器停靠位置"
          className="dock-select"
          value={dock}
          onChange={(e) => onDock(e.target.value as typeof dock)}
        >
          <option value="right">右侧</option>
          <option value="left">左侧</option>
          <option value="bottom">底部</option>
        </select>
        <button
          aria-label="重新读取远程文件"
          className="secondary"
          disabled={doc.busy || !connected}
          onClick={onReload}
          title="重新读取远程文件"
        >
          <RefreshCw size={14} />
          重读
        </button>
        <button
          aria-label="保存文件"
          className="primary"
          disabled={doc.busy || !connected || !dirty || tooLarge}
          onClick={onSave}
        >
          <Save size={14} />
          {doc.busy ? "处理中…" : "保存"}
        </button>
        <button aria-label="关闭编辑器" disabled={doc.busy} onClick={onClose}>
          <X size={17} />
        </button>
      </CompactToolbar>
      {!connected && (
        <div className="editor-warning" role="status">
          连接已断开，草稿仍保留在此窗口。可复制内容后重新授权连接。
        </div>
      )}
      {doc.error && (
        <div className="editor-warning" role="status">
          {doc.error}
        </div>
      )}
      {tooLarge && (
        <div className="editor-warning" role="status">
          内容超过 1 MiB，请缩减后保存。
        </div>
      )}
      {format.mixed && (
        <div className="editor-warning" role="status">
          检测到混合换行，当前只读并保留原内容。选择转换后才能编辑。
          <button
            className="secondary"
            onClick={() => {
              if (confirm("明确转换全部换行为 LF？此操作会产生未保存修改。"))
                onChange(convertLineEndings(doc.text, "\n"));
            }}
          >
            统一为 Unix 换行后编辑
          </button>
          <button
            className="secondary"
            onClick={() => {
              if (confirm("明确转换全部换行为 CRLF？此操作会产生未保存修改。"))
                onChange(convertLineEndings(doc.text, "\r\n"));
            }}
          >
            统一为 Windows 换行后编辑
          </button>
        </div>
      )}
      <CodeEditor
        stateCache={stateCache}
        id={`${doc.workspaceId}:${doc.sessionId}:${doc.path}`}
        path={doc.path}
        text={doc.text}
        readOnly={!!doc.busy || blocked}
        light={light}
        language={language}
        tabSize={tabSize}
        useTabs={useTabs}
        whitespace={whitespace}
        onChange={onChange}
        onSave={() => {
          if (connected && dirty && !doc.busy && !tooLarge) onSave();
        }}
        onPosition={(line, column, count) =>
          setPosition((old) =>
            old.line === line && old.column === column && old.count === count
              ? old
              : { line, column, count },
          )
        }
      />
      <footer>
        <span title="仅显式保存时写入远程 · 保存前检查冲突 · 最大 1 MiB">
          {position.count} 行 · 行 {position.line}，列 {position.column}
          {whitespace
            ? ` · ${format.mixed ? "混合换行" : format.eol === "\r\n" ? "CRLF" : "LF"}`
            : ""}
        </span>
        <span>{bytes.toLocaleString()} 字节</span>
      </footer>
    </section>
  );
}
