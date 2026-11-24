import { useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { Terminal } from "@xterm/xterm";
import { FitAddon } from "@xterm/addon-fit";
import { SearchAddon } from "@xterm/addon-search";
import "@xterm/xterm/css/xterm.css";
import type { SessionInfo } from "../shared/types";
import { terminalThemes } from "./terminal-themes";
import { CompactToolbar } from "./CompactToolbar";
import { openContextMenu, menuKey, type MenuItem } from "./ContextMenu";
export function TerminalView({
  session,
  visible,
  fontSize,
  focused,
  onActivate,
  pane,
  light,
}: {
  session: SessionInfo;
  visible: boolean;
  fontSize: number;
  focused: boolean;
  onActivate: () => void;
  pane: "first" | "second";
  light: boolean;
}) {
  const container = useRef<HTMLDivElement>(null),
    terminal = useRef<Terminal | null>(null),
    search = useRef<SearchAddon | null>(null),
    fitRef = useRef<FitAddon | null>(null);
  const [query, setQuery] = useState(""),
    [notice, setNotice] = useState(""),
    [preview, setPreview] = useState<{ text: string; token: number } | null>(
      null,
    );
  const latest = useRef({ session, visible, focused, onActivate });
  latest.current = { session, visible, focused, onActivate };
  const epoch = useRef(0),
    mounted = useRef(true),
    actions = useRef({ copy: () => {}, paste: (_text?: string) => {} });
  const eligible = () => {
    const state = latest.current;
    return (
      mounted.current &&
      state.visible &&
      state.session.status === "ready" &&
      state.session.terminalStatus !== "closed" &&
      state.session.terminalStatus !== "error" &&
      document.visibilityState === "visible" &&
      (state.focused || !!container.current?.contains(document.activeElement))
    );
  };
  const report = (error: unknown) => {
    if (mounted.current)
      setNotice(
        String(error instanceof Error ? error.message : error).replace(
          /^Error invoking remote method '[^']+': Error: /,
          "",
        ),
      );
  };
  const copy = () => {
    const selected = terminal.current?.getSelection();
    if (!selected) {
      setNotice("请先选择终端文本，剪贴板保持不变");
      return;
    }
    void window.forterm.clipboardWrite(selected).catch(report);
  };
  const deliver = (text: string, token: number) => {
    if (token !== epoch.current || !eligible()) {
      setPreview(null);
      setNotice("终端状态或焦点已改变，已取消粘贴");
      return;
    }
    setPreview(null);
    terminal.current?.paste(text);
    terminal.current?.focus();
  };
  const paste = async (supplied?: string) => {
    if (!eligible()) {
      setNotice("此终端未就绪，未粘贴");
      return;
    }
    const token = ++epoch.current;
    try {
      const text =
        supplied === undefined
          ? await window.forterm.clipboardRead()
          : supplied;
      if (token !== epoch.current || !eligible()) return;
      if (!text) {
        setNotice("剪贴板没有文本");
        return;
      }
      if (new TextEncoder().encode(text).length > 1024 * 1024)
        throw new Error("粘贴文本超过 1 MiB");
      if (/[\r\n]/.test(text)) setPreview({ text, token });
      else deliver(text, token);
    } catch (e) {
      report(e);
    }
  };
  actions.current = {
    copy,
    paste: (text) => {
      void paste(text);
    },
  };
  useEffect(() => {
    mounted.current = true;
    const term = new Terminal({
      fontFamily: '"Cascadia Code", "SFMono-Regular", Consolas, monospace',
      fontSize,
      cursorBlink: true,
      scrollback: 10000,
      allowProposedApi: false,
      theme: terminalThemes[light ? "light" : "dark"],
    });
    const fit = new FitAddon(),
      finder = new SearchAddon();
    term.loadAddon(fit);
    term.loadAddon(finder);
    term.open(container.current!);
    terminal.current = term;
    search.current = finder;
    fitRef.current = fit;
    let attached = false,
      frame = 0;
    const pending: string[] = [];
    const off = window.forterm.onEvent((e) => {
      if (e.type === "data" && e.id === session.id) {
        if (attached) term.write(e.data);
        else pending.push(e.data);
      }
    });
    void window.forterm
      .attach(session.id)
      .then((buffer) => {
        if (!mounted.current) return;
        term.write(buffer);
        attached = true;
        for (const data of pending) term.write(data);
      })
      .catch(() => {
        if (mounted.current) term.writeln("\r\n[会话已结束]");
      });
    const data = term.onData((text) => window.forterm.input(session.id, text));
    term.attachCustomKeyEventHandler((event) => {
      if (event.type !== "keydown") return true;
      const key = event.key.toLowerCase();
      const copyKey =
        (event.ctrlKey && event.shiftKey && key === "c") ||
        (event.metaKey && key === "c") ||
        (event.ctrlKey &&
          !event.shiftKey &&
          key === "c" &&
          term.hasSelection());
      const pasteKey =
        ((event.ctrlKey || event.metaKey) && key === "v") ||
        (event.shiftKey && event.key === "Insert");
      if (copyKey || pasteKey) {
        event.preventDefault();
        event.stopPropagation();
        if (copyKey) actions.current.copy();
        else actions.current.paste();
        return false;
      }
      return true;
    });
    const nativePaste = (event: ClipboardEvent) => {
      event.preventDefault();
      event.stopImmediatePropagation();
      actions.current.paste(event.clipboardData?.getData("text/plain") ?? "");
    };
    container.current!.addEventListener("paste", nativePaste, true);
    const focus = (event: FocusEvent) => {
      const element = event.target as HTMLElement;
      if (
        element === document.body ||
        element === document.documentElement ||
        container.current?.contains(element) ||
        element.closest(
          `.terminal-paste[data-paste-session="${session.id}"]`,
        ) ||
        element.closest('[role="menu"]')
      )
        return;
      epoch.current++;
      setPreview(null);
    };
    const blur = () => {
      epoch.current++;
      setPreview(null);
    };
    window.addEventListener("focusin", focus, true);
    window.addEventListener("blur", blur);
    const resize = () => {
      cancelAnimationFrame(frame);
      frame = requestAnimationFrame(() => {
        if (
          !mounted.current ||
          !container.current?.clientWidth ||
          !container.current.clientHeight
        )
          return;
        const cols = term.cols,
          rows = term.rows;
        fit.fit();
        if (cols !== term.cols || rows !== term.rows)
          window.forterm.resize(session.id, term.cols, term.rows);
      });
    };
    const observer = new ResizeObserver(resize);
    observer.observe(container.current!);
    resize();
    return () => {
      mounted.current = false;
      epoch.current++;
      cancelAnimationFrame(frame);
      off();
      data.dispose();
      observer.disconnect();
      container.current?.removeEventListener("paste", nativePaste, true);
      window.removeEventListener("focusin", focus, true);
      window.removeEventListener("blur", blur);
      term.dispose();
    };
  }, [session.id]);
  useEffect(() => {
    if (terminal.current)
      terminal.current.options.theme = terminalThemes[light ? "light" : "dark"];
  }, [light]);
  useEffect(() => {
    const term = terminal.current;
    if (term) {
      term.options.fontSize = fontSize;
      if (container.current?.clientWidth) {
        fitRef.current?.fit();
        window.forterm.resize(session.id, term.cols, term.rows);
      }
    }
  }, [fontSize]);
  useEffect(() => {
    if (session.status === "ready" && visible && terminal.current) {
      fitRef.current?.fit();
      window.forterm.resize(
        session.id,
        terminal.current.cols,
        terminal.current.rows,
      );
    }
  }, [session.status, session.terminalStatus, visible]);
  useEffect(() => {
    if (visible && focused) terminal.current?.focus();
    else {
      epoch.current++;
      setPreview(null);
    }
  }, [visible, focused]);
  useEffect(() => {
    if (
      session.status !== "ready" ||
      session.terminalStatus === "closed" ||
      session.terminalStatus === "error"
    ) {
      epoch.current++;
      setPreview(null);
    }
  }, [session.status, session.terminalStatus]);
  useEffect(() => {
    if (!notice) return;
    const timer = setTimeout(() => setNotice(""), 6000);
    return () => clearTimeout(timer);
  }, [notice]);
  const items = (): MenuItem[] => [
    {
      label: "复制选中",
      disabled: !terminal.current?.hasSelection(),
      action: copy,
    },
    {
      label: "粘贴",
      disabled: session.status !== "ready",
      action: () => paste(),
    },
    { label: "全选", action: () => terminal.current?.selectAll() },
  ];
  return (
    <section
      data-session-id={session.id}
      style={{ gridArea: pane }}
      className={"terminal-view " + (!visible ? "hidden" : "")}
      onMouseDown={(event) => {
        if ((event.target as HTMLElement).closest(".terminal-canvas"))
          onActivate();
      }}
    >
      <CompactToolbar label="终端工具栏" className="terminal-top">
        <span className={"status-dot " + session.status} />
        <span className="toolbar-title" title={session.title}>
          {session.title}
        </span>
        <span
          className="toolbar-meta"
          title={session.temporary ? "控制器临时会话" : session.host}
        >
          {session.temporary ? "控制器临时会话" : session.host}
        </span>
        {(session.launchInvocations ?? 1) > 1 && (
          <span className="toolbar-meta">
            合并 {(session.launchInvocations ?? 1) - 1} 次
          </span>
        )}
        <input
          aria-label="搜索终端"
          placeholder="搜索输出…"
          value={query}
          onChange={(e) => {
            setQuery(e.target.value);
            search.current?.findNext(e.target.value);
          }}
          onKeyDown={(e) => {
            if (e.key === "Enter") search.current?.findNext(query);
          }}
        />
      </CompactToolbar>
      {notice && (
        <div className="terminal-message" role="status">
          {notice}
        </div>
      )}
      {session.protocol !== "local" && session.terminalStatus !== "ready" && (
        <div className="terminal-message">
          {session.terminalMessage || "正在打开终端通道…"}
          {session.terminalStatus !== "opening" &&
            session.status === "ready" && (
              <button
                className="secondary"
                onClick={() =>
                  void window.forterm.openTerminal(session.id).catch(report)
                }
              >
                重试终端通道
              </button>
            )}
        </div>
      )}
      <div
        className="terminal-canvas"
        ref={container}
        onFocusCapture={() => {
          if (!latest.current.focused) latest.current.onActivate();
        }}
        onContextMenu={(e) => {
          e.preventDefault();
          openContextMenu(e.clientX, e.clientY, items());
        }}
        onKeyDownCapture={(e) => menuKey(e, e.currentTarget, items())}
      />
      {(session.status === "error" || session.status === "closed") && (
        <div className="terminal-message">{session.message}</div>
      )}
      {preview &&
        createPortal(
          <div className="overlay">
            <section
              className="modal terminal-paste"
              data-paste-session={session.id}
              role="dialog"
              aria-label="确认终端粘贴"
            >
              <h2>确认多行 / 带换行的粘贴</h2>
              <p title={session.title}>目标终端：{session.title}</p>
              <p className="subtle">
                换行可能执行命令。保留原内容交给 xterm
                粘贴路径，不额外追加回车。
              </p>
              <pre>
                {preview.text.slice(0, 4096)}
                {preview.text.length > 4096 ? "\n（仅预览前 4096 字符）" : ""}
              </pre>
              <footer>
                <button
                  autoFocus
                  className="secondary"
                  onClick={() => {
                    epoch.current++;
                    setPreview(null);
                    terminal.current?.focus();
                  }}
                >
                  取消粘贴
                </button>
                <button
                  className="primary"
                  onClick={() => deliver(preview.text, preview.token)}
                >
                  确认粘贴
                </button>
              </footer>
            </section>
          </div>,
          document.body,
        )}
    </section>
  );
}
