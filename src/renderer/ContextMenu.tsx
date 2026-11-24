import { useEffect, useLayoutEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
export interface MenuItem {
  label: string;
  disabled?: boolean;
  title?: string;
  action: () => void | Promise<void>;
}
export function openContextMenu(x: number, y: number, items: MenuItem[]) {
  window.dispatchEvent(
    new CustomEvent("forterm-context-menu", { detail: { x, y, items } }),
  );
}
export function closeContextMenu() {
  window.dispatchEvent(new Event("forterm-context-close"));
}
export function ContextMenuHost() {
  const [menu, setMenu] = useState<{
      x: number;
      y: number;
      items: MenuItem[];
    } | null>(null),
    [position, setPosition] = useState({ x: 0, y: 0 });
  const root = useRef<HTMLDivElement>(null),
    previous = useRef<HTMLElement | null>(null),
    index = useRef(0);
  const close = (restore = false) => {
    setMenu(null);
    if (restore) previous.current?.focus({ preventScroll: true });
  };
  useEffect(() => {
    const open = (e: Event) => {
      const detail = (e as CustomEvent).detail;
      previous.current = document.activeElement as HTMLElement;
      index.current = Math.max(
        0,
        detail.items.findIndex((i: MenuItem) => !i.disabled),
      );
      setPosition({ x: detail.x, y: detail.y });
      setMenu(detail);
    };
    const cancel = () => close();
    const outside = (e: PointerEvent) => {
      if (!root.current?.contains(e.target as Node)) close();
    };
    window.addEventListener("forterm-context-menu", open);
    window.addEventListener("forterm-context-close", cancel);
    window.addEventListener("pointerdown", outside, true);
    window.addEventListener("resize", cancel);
    window.addEventListener("blur", cancel);
    return () => {
      window.removeEventListener("forterm-context-menu", open);
      window.removeEventListener("forterm-context-close", cancel);
      window.removeEventListener("pointerdown", outside, true);
      window.removeEventListener("resize", cancel);
      window.removeEventListener("blur", cancel);
    };
  }, []);
  useLayoutEffect(() => {
    if (!menu || !root.current) return;
    const rect = root.current.getBoundingClientRect();
    setPosition({
      x: Math.max(8, Math.min(menu.x, innerWidth - rect.width - 8)),
      y: Math.max(8, Math.min(menu.y, innerHeight - rect.height - 8)),
    });
    root.current
      .querySelectorAll<HTMLButtonElement>("button")
      [index.current]?.focus();
  }, [menu]);
  if (!menu) return null;
  return createPortal(
    <div
      ref={root}
      className={
        "app-context-menu " +
        (document.querySelector(".app.light") ? "light-menu" : "")
      }
      role="menu"
      aria-label="操作菜单"
      style={{ left: position.x, top: position.y }}
      onContextMenu={(e) => e.preventDefault()}
      onKeyDown={(e) => {
        const buttons = [
          ...root.current!.querySelectorAll<HTMLButtonElement>("button"),
        ].filter((b) => !b.disabled);
        const current = buttons.indexOf(
          document.activeElement as HTMLButtonElement,
        );
        if (e.key === "Escape") {
          e.preventDefault();
          close(true);
          return;
        }
        if (e.key === "Tab") {
          close(true);
          return;
        }
        if (["ArrowDown", "ArrowUp", "Home", "End"].includes(e.key)) {
          e.preventDefault();
          const next =
            e.key === "Home"
              ? 0
              : e.key === "End"
                ? buttons.length - 1
                : (current +
                    (e.key === "ArrowDown" ? 1 : -1) +
                    buttons.length) %
                  buttons.length;
          buttons[next]?.focus();
          buttons[next]?.scrollIntoView({ block: "nearest" });
        }
      }}
    >
      {menu.items.map((item, i) => (
        <button
          role="menuitem"
          key={i}
          disabled={item.disabled}
          title={item.title}
          onClick={() => {
            close();
            void item.action();
          }}
        >
          {item.label}
        </button>
      ))}
    </div>,
    document.body,
  );
}
export function menuKey(
  event: React.KeyboardEvent,
  element: HTMLElement,
  items: MenuItem[],
) {
  if (event.key === "ContextMenu" || (event.shiftKey && event.key === "F10")) {
    event.preventDefault();
    event.stopPropagation();
    const r = element.getBoundingClientRect();
    openContextMenu(r.left + 12, r.top + Math.min(24, r.height), items);
  }
}

export function inputContextMenu(event: React.MouseEvent) {
  const element = event.target as HTMLInputElement | HTMLTextAreaElement;
  if (
    !["INPUT", "TEXTAREA"].includes(element.tagName) ||
    element.closest(".terminal-canvas,.code-editor-host")
  )
    return;
  event.preventDefault();
  event.stopPropagation();
  const from = element.selectionStart ?? 0,
    to = element.selectionEnd ?? 0;
  const command =
    (action: "copy" | "cut" | "paste" | "selectAll" | "undo" | "redo") =>
    () => {
      if (!element.isConnected) return;
      element.focus({ preventScroll: true });
      try {
        element.setSelectionRange(from, to);
      } catch {}
      return window.forterm.editCommand(action);
    };
  openContextMenu(event.clientX, event.clientY, [
    { label: "复制", disabled: from === to, action: command("copy") },
    {
      label: "剪切",
      disabled: element.readOnly || from === to,
      action: command("cut"),
    },
    {
      label: "粘贴",
      disabled: element.readOnly || element.disabled,
      action: command("paste"),
    },
    { label: "全选", action: command("selectAll") },
    { label: "撤销", disabled: element.readOnly, action: command("undo") },
    { label: "重做", disabled: element.readOnly, action: command("redo") },
  ]);
}
