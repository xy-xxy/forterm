import { openContextMenu, menuKey, type MenuItem } from "./ContextMenu";
import { useLayoutEffect, useRef } from "react";
import {
  EditorState,
  Compartment,
  countColumn,
  type Extension,
} from "@codemirror/state";
import {
  EditorView,
  lineNumbers,
  highlightActiveLine,
  highlightActiveLineGutter,
  drawSelection,
  keymap,
  highlightWhitespace,
  ViewPlugin,
  Decoration,
  WidgetType,
  type DecorationSet,
} from "@codemirror/view";
import {
  history,
  defaultKeymap,
  historyKeymap,
  indentWithTab,
  undo,
  redo,
  historyField,
} from "@codemirror/commands";
import {
  syntaxHighlighting,
  HighlightStyle,
  indentUnit,
  StreamLanguage,
} from "@codemirror/language";
import { tags } from "@lezer/highlight";
import { json } from "@codemirror/lang-json";
import { yaml } from "@codemirror/lang-yaml";
import { xml } from "@codemirror/lang-xml";
import { javascript } from "@codemirror/lang-javascript";
import { shell } from "@codemirror/legacy-modes/mode/shell";
import { properties } from "@codemirror/legacy-modes/mode/properties";
import { textFormat } from "./text-format";
export const languageNames = {
  auto: "自动",
  text: "纯文本",
  yaml: "YAML",
  json: "JSON",
  xml: "XML",
  shell: "Shell",
  properties: "INI / properties",
  javascript: "JavaScript",
  typescript: "TypeScript",
};
export type EditorLanguage = keyof typeof languageNames;
function languageFor(name: string, path: string): Extension {
  if (name === "auto") {
    const file = path.toLowerCase();
    name = /\.ya?ml$/.test(file)
      ? "yaml"
      : /\.json$/.test(file)
        ? "json"
        : /\.(xml|html|svg)$/.test(file)
          ? "xml"
          : /\.(sh|bash|zsh)$/.test(file)
            ? "shell"
            : /\.(ini|properties|conf|cfg)$/.test(file)
              ? "properties"
              : /\.(ts|tsx)$/.test(file)
                ? "typescript"
                : /\.(js|jsx|mjs|cjs)$/.test(file)
                  ? "javascript"
                  : "text";
  }
  return name === "yaml"
    ? yaml()
    : name === "json"
      ? json()
      : name === "xml"
        ? xml()
        : name === "shell"
          ? StreamLanguage.define(shell)
          : name === "properties"
            ? StreamLanguage.define(properties)
            : name === "typescript"
              ? javascript({ typescript: true, jsx: true })
              : name === "javascript"
                ? javascript({ jsx: true })
                : [];
}
class EndMarker extends WidgetType {
  constructor(readonly marker: string) {
    super();
  }
  eq(other: EndMarker) {
    return other.marker === this.marker;
  }
  toDOM() {
    const span = document.createElement("span");
    span.className = "cm-eol-marker";
    span.textContent = this.marker;
    span.setAttribute("aria-hidden", "true");
    return span;
  }
}
function endMarkers(endings: string[], fallback: string) {
  return ViewPlugin.fromClass(
    class {
      decorations: DecorationSet;
      constructor(view: EditorView) {
        this.decorations = this.build(view);
      }
      update(update: any) {
        if (update.docChanged || update.viewportChanged)
          this.decorations = this.build(update.view);
      }
      build(view: EditorView) {
        const widgets = [];
        for (const { from, to } of view.visibleRanges) {
          for (let pos = from; pos <= to; ) {
            const line = view.state.doc.lineAt(pos);
            if (line.number < view.state.doc.lines)
              widgets.push(
                Decoration.widget({
                  widget: new EndMarker(
                    (endings[line.number - 1] ?? fallback) === "\r\n"
                      ? "CRLF"
                      : (endings[line.number - 1] ?? fallback) === "\r"
                        ? "CR"
                        : "LF",
                  ),
                  side: 1,
                }).range(line.to),
              );
            if (line.to >= to) break;
            pos = line.to + 1;
          }
        }
        return Decoration.set(widgets);
      }
    },
    { decorations: (instance) => instance.decorations },
  );
}
export type EditorStateCache = Map<
  string,
  {
    state: EditorState;
    raw: string;
    top: number;
    left: number;
    format?: { bom: boolean; eol: string };
  }
>;
export function CodeEditor({
  id,
  text,
  path,
  readOnly,
  light,
  language,
  tabSize,
  useTabs,
  whitespace,
  onChange,
  onSave,
  onPosition,
  stateCache,
}: {
  id: string;
  text: string;
  path: string;
  readOnly: boolean;
  light: boolean;
  language: EditorLanguage;
  tabSize: number;
  useTabs: boolean;
  whitespace: boolean;
  onChange: (text: string) => void;
  onSave: () => void;
  onPosition: (line: number, column: number, count: number) => void;
  stateCache: EditorStateCache;
}) {
  const host = useRef<HTMLDivElement>(null),
    view = useRef<EditorView | null>(null);
  const cache = useRef(stateCache);
  const baseExtensions = useRef<Extension[]>([]),
    formats = useRef(new Map<string, { bom: boolean; eol: string }>());
  const configured = useRef("");
  const currentId = useRef(id),
    currentRaw = useRef(text),
    programmatic = useRef(false);
  const callback = useRef({ onChange, onSave, onPosition });
  callback.current = { onChange, onSave, onPosition };
  const compartments = useRef({ settings: new Compartment() });
  const settings = () => {
    const format = textFormat(currentRaw.current);
    return [
      EditorState.readOnly.of(readOnly || format.mixed),
      EditorView.editable.of(!readOnly && !format.mixed),
      EditorState.tabSize.of(tabSize),
      indentUnit.of(useTabs ? "\t" : " ".repeat(tabSize)),
      languageFor(language, path),
      EditorView.theme(
        {
          "&": {
            height: "100%",
            fontSize: "13px",
            color: light ? "#23394b" : "#d5e2ef",
            backgroundColor: light ? "#fff" : "#101721",
          },
          ".cm-scroller": {
            overflow: "auto",
            fontFamily: '"Cascadia Code", Consolas, monospace',
          },
          ".cm-gutters": {
            backgroundColor: light ? "#edf2f5" : "#172231",
            color: light ? "#65778a" : "#8597a9",
            borderRight: "1px solid " + (light ? "#d2dce6" : "#334359"),
          },
          ".cm-activeLineGutter": {
            backgroundColor: light ? "#dcecf5" : "#243d4a",
            color: light ? "#29485d" : "#c7e2f1",
          },
          ".cm-selectionBackground": {
            backgroundColor: light
              ? "#bad9ed !important"
              : "#305565 !important",
          },
          ".cm-activeLine": {
            backgroundColor: light ? "#edf6fa" : "#ffffff07",
          },
          ".cm-line": {
            backgroundImage: `repeating-linear-gradient(to right, transparent 0, transparent calc(${tabSize}ch - 1px), ${light ? "#d8e1e960" : "#45607755"} calc(${tabSize}ch - 1px), ${light ? "#d8e1e960" : "#45607755"} ${tabSize}ch)`,
          },
          ".cm-content": { caretColor: light ? "#255647" : "#9be2c7" },
        },
        { dark: !light },
      ),
      syntaxHighlighting(
        HighlightStyle.define([
          { tag: tags.keyword, color: light ? "#8c357c" : "#d3a4ee" },
          { tag: tags.string, color: light ? "#306f35" : "#a6d6a6" },
          { tag: tags.number, color: light ? "#a64e00" : "#e2b678" },
          {
            tag: tags.comment,
            color: light ? "#657d79" : "#819c94",
            fontStyle: "italic",
          },
          { tag: tags.propertyName, color: light ? "#246c9e" : "#8fcbe9" },
          { tag: tags.bool, color: light ? "#9e3968" : "#e0a4c2" },
          { tag: tags.tagName, color: light ? "#296c88" : "#89c9dc" },
        ]),
      ),
      whitespace
        ? [highlightWhitespace(), endMarkers(format.endings, format.eol)]
        : [],
    ];
  };
  const snapshot = () => {
    const v = view.current;
    if (
      v &&
      host.current?.getClientRects().length &&
      v.scrollDOM.clientHeight > 0
    )
      cache.current.set(currentId.current, {
        state: v.state,
        raw: currentRaw.current,
        top: v.scrollDOM.scrollTop,
        left: v.scrollDOM.scrollLeft,
        format: formats.current.get(currentId.current),
      });
  };
  useLayoutEffect(() => {
    const initial = textFormat(text);
    currentRaw.current = text;
    formats.current.set(id, { bom: initial.bom, eol: initial.eol });
    const extensions: Extension[] = [
      lineNumbers(),
      highlightActiveLine(),
      highlightActiveLineGutter(),
      drawSelection(),
      history(),
      EditorView.domEventHandlers({
        paste(event, editor) {
          if (editor.state.readOnly) return false;
          const text = event.clipboardData?.getData("text/plain") ?? "";
          if (!text || new TextEncoder().encode(text).length > 1024 * 1024) {
            event.preventDefault();
            window.dispatchEvent(
              new CustomEvent("forterm-menu-error", {
                detail: !text ? "剪贴板没有文本" : "粘贴文本超过 1 MiB",
              }),
            );
            return true;
          }
          return false;
        },
      }),
      EditorState.transactionFilter.of((transaction) => {
        if (
          !programmatic.current &&
          transaction.docChanged &&
          transaction.newDoc.sliceString(0, 1) === "\ufeff"
        ) {
          // Clipboard BOM is not an encoding change. Keep the original file's
          // BOM policy fixed across history and cached documents.
          return [
            transaction,
            { changes: { from: 0, to: 1, insert: "" }, sequential: true },
          ];
        }
        return transaction;
      }),
      keymap.of([
        {
          key: "Mod-s",
          run: () => {
            callback.current.onSave();
            return true;
          },
        },
        indentWithTab,
        ...defaultKeymap,
        ...historyKeymap,
      ]),
      compartments.current.settings.of(settings()),
      EditorView.contentAttributes.of({
        "aria-label": "远程文件内容",
        role: "textbox",
        "aria-multiline": "true",
        spellcheck: "false",
      }),
      EditorView.updateListener.of((update) => {
        const line = update.state.doc.lineAt(update.state.selection.main.head);
        callback.current.onPosition(
          line.number,
          countColumn(
            line.text.slice(0, update.state.selection.main.head - line.from),
            update.state.tabSize,
          ) + 1,
          update.state.doc.lines,
        );
        if (update.docChanged && !programmatic.current) {
          const format =
            formats.current.get(currentId.current) ??
            textFormat(currentRaw.current);
          const raw =
            (format.bom ? "\ufeff" : "") +
            update.state.sliceDoc().replace(/\n/g, format.eol);
          currentRaw.current = raw;
          callback.current.onChange(raw);
        }
      }),
    ];
    baseExtensions.current = extensions;
    const saved = cache.current.get(id);
    const state =
      saved && saved.raw === text
        ? EditorState.fromJSON(
            saved.state.toJSON({ history: historyField }),
            { extensions },
            { history: historyField },
          )
        : EditorState.create({ doc: initial.normalized, extensions });
    if (saved?.format) formats.current.set(id, saved.format);
    const v = new EditorView({ state, parent: host.current! });
    view.current = v;
    if (saved) {
      v.scrollDOM.scrollTop = saved.top;
      v.scrollDOM.scrollLeft = saved.left;
    }
    const capture = () => snapshot();
    const restore = () => {
      const saved = cache.current.get(currentId.current);
      if (saved) {
        v.scrollDOM.scrollTop = saved.top;
        v.scrollDOM.scrollLeft = saved.left;
      }
    };
    host.current!.addEventListener("forterm-editor-capture", capture);
    host.current!.addEventListener("forterm-editor-restore", restore);
    return () => {
      snapshot();
      host.current?.removeEventListener("forterm-editor-capture", capture);
      host.current?.removeEventListener("forterm-editor-restore", restore);
      v.destroy();
      view.current = null;
    };
  }, []);
  useLayoutEffect(() => {
    const v = view.current;
    if (!v) return;
    if (currentId.current !== id) {
      snapshot();
      currentId.current = id;
      const cached = cache.current.get(id);
      currentRaw.current = text;
      if (cached && cached.raw === text) {
        v.setState(
          EditorState.fromJSON(
            cached.state.toJSON({ history: historyField }),
            { extensions: baseExtensions.current },
            { history: historyField },
          ),
        );
        if (cached.format) formats.current.set(id, cached.format);
        v.scrollDOM.scrollTop = cached.top;
        v.scrollDOM.scrollLeft = cached.left;
      } else {
        const format = textFormat(text);
        formats.current.set(id, { bom: format.bom, eol: format.eol });
        v.setState(
          EditorState.create({
            doc: format.normalized,
            extensions: baseExtensions.current,
          }),
        );
      }
      configured.current = "";
    } else if (currentRaw.current !== text) {
      currentRaw.current = text;
      const format = textFormat(text);
      formats.current.set(id, { bom: format.bom, eol: format.eol });
      programmatic.current = true;
      v.dispatch({
        changes: {
          from: 0,
          to: v.state.doc.length,
          insert: textFormat(text).normalized,
        },
      });
      programmatic.current = false;
    }
    const key = JSON.stringify([
      id,
      path,
      readOnly,
      light,
      language,
      tabSize,
      useTabs,
      whitespace,
      textFormat(text).mixed,
      textFormat(text).eol,
    ]);
    if (configured.current !== key) {
      configured.current = key;
      v.dispatch({
        effects: compartments.current.settings.reconfigure(settings()),
      });
    }
    const line = v.state.doc.lineAt(v.state.selection.main.head);
    callback.current.onPosition(
      line.number,
      countColumn(
        line.text.slice(0, v.state.selection.main.head - line.from),
        tabSize,
      ) + 1,
      v.state.doc.lines,
    );
  }, [id, text, path, readOnly, light, language, tabSize, useTabs, whitespace]);
  const editorMenu = (): MenuItem[] => {
    const v = view.current;
    if (!v) return [];
    const snapshot = v.state,
      selection = snapshot.selection.main,
      key = currentId.current,
      readonly = snapshot.readOnly;
    const unchanged = () =>
      view.current === v &&
      currentId.current === key &&
      v.state.doc === snapshot.doc;
    const selected = () => snapshot.sliceDoc(selection.from, selection.to);
    return [
      {
        label: "复制",
        disabled: selection.empty,
        action: () => window.forterm.clipboardWrite(selected()),
      },
      {
        label: "剪切",
        disabled: readonly || selection.empty,
        action: async () => {
          await window.forterm.clipboardWrite(selected());
          if (unchanged())
            v.dispatch({
              changes: { from: selection.from, to: selection.to, insert: "" },
              selection: { anchor: selection.from },
              userEvent: "delete.cut",
            });
        },
      },
      {
        label: "粘贴",
        disabled: readonly,
        action: async () => {
          const value = await window.forterm.clipboardRead();
          if (!value) throw new Error("剪贴板没有文本");
          if (!unchanged()) throw new Error("编辑目标已改变，已取消粘贴");
          v.dispatch({
            ...v.state.replaceSelection(value),
            userEvent: "input.paste",
          });
          v.focus();
        },
      },
      {
        label: "全选",
        action: () => {
          if (unchanged())
            v.dispatch({ selection: { anchor: 0, head: v.state.doc.length } });
        },
      },
      {
        label: "撤销",
        disabled: readonly,
        action: () => {
          if (unchanged()) undo(v);
        },
      },
      {
        label: "重做",
        disabled: readonly,
        action: () => {
          if (unchanged()) redo(v);
        },
      },
      {
        label: "保存",
        disabled: readonly,
        action: () => {
          if (currentId.current === key) callback.current.onSave();
        },
      },
    ];
  };
  return (
    <div
      className="code-editor-host"
      ref={host}
      data-editor-file={id}
      onContextMenu={(event) => {
        event.preventDefault();
        event.stopPropagation();
        openContextMenu(event.clientX, event.clientY, editorMenu());
      }}
      onKeyDownCapture={(event) =>
        menuKey(event, event.currentTarget, editorMenu())
      }
    />
  );
}
