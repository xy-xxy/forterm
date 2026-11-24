import { FileWorkspaces } from "./file-workspaces";
import { BastionService } from "./bastion-service";
import { FileOperations, safeOperationPath } from "./file-operations";
import {
  app,
  clipboard,
  BrowserWindow,
  ipcMain,
  dialog,
  safeStorage,
  Menu,
  shell,
} from "electron";
import { readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import { z } from "zod";
import { parseLaunch } from "./launch";
import { LaunchGate } from "./launch-gate";
import { mergeReports } from "../shared/reports";
import { Store } from "./store";
import { Sessions } from "./sessions";
import { Prompts } from "./prompts";
import {
  createRemoteText,
  readRemoteText,
  saveRemoteText,
  TEXT_LIMIT,
} from "./remote-text";
import { parseTotp } from "./totp";
import { Transfers } from "./transfers";
import {
  connectionSchema,
  identifier,
  profileSchema,
  remotePath,
  keepAliveSchema,
} from "./schema";
import type { AppEvent, Report, Launch } from "../shared/types";

function applicationArgs(commandLine: string[]) {
  if (app.isPackaged)
    return commandLine
      .slice(1)
      .filter(
        (s) => !/^--(?:inspect(?:-brk)?|remote-debugging-port)=\d+$/.test(s),
      );
  // Development/test launchers may insert Electron switches before the app path.
  const index = commandLine.findIndex(
    (s, i) =>
      i > 0 && !s.startsWith("-") && path.resolve(s) === app.getAppPath(),
  );
  return commandLine.slice(index >= 0 ? index + 1 : 2);
}
const argv = applicationArgs(process.argv);
const initial = parseLaunch(
  argv,
  path.basename(process.execPath, ".exe").toLowerCase(),
);
if (process.env.FORTERM_TEST_DATA)
  app.setPath("userData", process.env.FORTERM_TEST_DATA);
// Chromium can reorder or add switches in second-instance commandLine. Preserve
// the exact application argv in Electron's in-memory additionalData channel.
const primary = app.requestSingleInstanceLock({ fortermArgs: argv });
const launchGate = new LaunchGate();
let windows: BrowserWindow[] = [];
const dirtyEditors = new Map<number, Set<string>>();
let reports: Report[] = [];
let ready = false;
const queued: Launch[] = [];
let sessions: Sessions;
let prompts: Prompts;
let store: Store;
let transfers: Transfers;
let operations: FileOperations;
let bastion: BastionService | undefined;
const fileAssociations = new Map<string, string>();
const fileWorkspaces = new FileWorkspaces(fileAssociations);
function emit(e: AppEvent) {
  if (e.type === "session") bastion?.observeSession(e.session);
  if (e.type === "session-removed") bastion?.sessionRemoved(e.id);
  if (e.type === "session-removed") launchGate.forgetSession(e.id);
  for (const w of windows)
    if (!w.isDestroyed()) w.webContents.send("forterm:event", e);
}
function createWindow() {
  const win = new BrowserWindow({
    width: 1440,
    height: 940,
    minWidth: 1050,
    minHeight: 650,
    backgroundColor: "#10141d",
    title: "Forterm",
    show: false,
    webPreferences: {
      preload: path.join(__dirname, "../preload/index.cjs"),
      nodeIntegration: false,
      contextIsolation: true,
      sandbox: true,
      webSecurity: true,
    },
  });
  windows.push(win);
  win.once("ready-to-show", () => win.show());
  const reportVisibility = () => {
    if (!win.isDestroyed())
      win.webContents.send("forterm:event", {
        type: "window-visibility",
        visible: win.isVisible() && !win.isMinimized() && win.isFocused(),
      });
  };
  win.on("hide", reportVisibility);
  win.on("show", reportVisibility);
  win.on("minimize", reportVisibility);
  win.on("restore", reportVisibility);
  win.on("blur", reportVisibility);
  win.on("focus", reportVisibility);
  let approvedClose = false;
  let closingPrompt = false;
  win.on("close", (event) => {
    if (approvedClose || !dirtyEditors.get(win.id)?.size) return;
    event.preventDefault();
    if (closingPrompt) return;
    closingPrompt = true;
    void dialog
      .showMessageBox(win, {
        type: "warning",
        message: "有未保存的远程文件草稿",
        detail: "关闭窗口会丢弃此窗口中的草稿。请先保存或复制需要保留的内容。",
        buttons: ["继续编辑", "丢弃并关闭"],
        defaultId: 0,
        cancelId: 0,
      })
      .then(({ response }) => {
        closingPrompt = false;
        if (response === 1) {
          approvedClose = true;
          win.close();
        }
      });
  });
  win.webContents.on("will-prevent-unload", (event) => {
    if (approvedClose || !dirtyEditors.get(win.id)?.size)
      event.preventDefault();
    else
      win.webContents.send("forterm:event", {
        type: "notice",
        message: "有未保存的文件，已阻止重新加载。请先保存或关闭编辑文件。",
      });
  });
  win.on("closed", () => {
    dirtyEditors.delete(win.id);
    windows = windows.filter((w) => w !== win);
  });
  win.webContents.setWindowOpenHandler(() => ({ action: "deny" }));
  win.webContents.on("will-navigate", (event) => event.preventDefault());
  win.webContents.session.setPermissionRequestHandler(
    (_web, _permission, callback) => callback(false),
  );
  if (!app.isPackaged && process.env.FORTERM_DEV_URL)
    void win.loadURL(process.env.FORTERM_DEV_URL);
  else void win.loadFile(path.join(__dirname, "../renderer/index.html"));
  return win;
}
async function launch(l: Launch) {
  // Claim the request synchronously, before any asynchronous connection work.
  const { duplicate, record } = launchGate.accept(l);
  if (l.diagnose || l.error) {
    reports = mergeReports(reports, [record.report]);
    emit({ type: "diagnostic", report: record.report });
  }
  let win = duplicate
    ? (windows.find((w) => w.id === record.windowId) ?? windows.at(-1))
    : !windows.length || l.newWindow
      ? createWindow()
      : windows.at(-1);
  if (!win) win = createWindow();
  record.windowId = win.id;
  if (win.isMinimized()) win.restore();
  win.show();
  win.focus();
  if (duplicate) {
    if (record.sessionId) {
      sessions.markExternalLaunch(
        record.sessionId,
        record.report.invocations ?? 1,
      );
      emit({ type: "session-focus", id: record.sessionId });
    }
    return;
  }
  if (!l.diagnose && !l.error && l.connection) {
    record.sessionId = await sessions.connect(l.connection);
    sessions.markExternalLaunch(
      record.sessionId,
      record.report.invocations ?? 1,
    );
  }
}
function owner(event: Electron.IpcMainInvokeEvent | Electron.IpcMainEvent) {
  const w = windows.find((w) => w.webContents === event.sender);
  if (!w || event.senderFrame !== event.sender.mainFrame)
    throw new Error("无效窗口");
  return w;
}
function handle(
  name: string,
  fn: (w: BrowserWindow, ...args: any[]) => unknown,
) {
  ipcMain.handle("forterm:" + name, async (event, ...args) => {
    const w = owner(event);
    try {
      return await fn(w, ...args);
    } catch (e) {
      // Only static application errors are shown. Do not expose library/server messages or argv.
      if (e instanceof z.ZodError) throw new Error("输入格式无效");
      const message = e instanceof Error ? e.message : "";
      if (/[\u4e00-\u9fff]/.test(message) && !message.includes("://"))
        throw new Error(message.slice(0, 300));
      throw new Error("操作失败，请检查会话状态、权限和输入");
    }
  });
}
if (!primary) {
  app.quit();
} else {
  app.on("second-instance", (_event, commandLine, _cwd, additionalData) => {
    const incoming = (additionalData as { fortermArgs?: unknown })?.fortermArgs;
    const valid =
      Array.isArray(incoming) &&
      incoming.length <= 128 &&
      incoming.every((s) => typeof s === "string" && s.length <= 32768);
    const l = parseLaunch(
      valid ? (incoming as string[]) : applicationArgs(commandLine),
    );
    if (ready) void launch(l);
    else queued.push(l);
  });
  app
    .whenReady()
    .then(async () => {
      Menu.setApplicationMenu(null);
      store = new Store(app.getPath("userData"), {
        encrypt: async (s) => {
          if (!(await safeStorage.isAsyncEncryptionAvailable()))
            throw new Error("系统凭据加密不可用，无法保存密码");
          return (await safeStorage.encryptStringAsync(s)).toString("base64");
        },
        decrypt: async (s) =>
          (await safeStorage.decryptStringAsync(Buffer.from(s, "base64")))
            .result,
      });
      prompts = new Prompts(emit);
      sessions = new Sessions(emit, store, prompts);
      transfers = new Transfers(sessions, emit);
      operations = new FileOperations(sessions, emit, (id, path) =>
        transfers.activeInPath(id, path),
      );
      // No vendor provider is registered before the actual contract is verified.
      // Synthetic HTTP adapters are test-only imports, absent from the production bundle.
      bastion = new BastionService(
        store,
        sessions,
        () => undefined,
        (snapshot) => emit({ type: "bastion-access", snapshot }),
      );
      handle("bastionAccess", () => bastion!.snapshot());
      handle("bastionLogin", (_w, id, input) =>
        bastion!.login(
          identifier.parse(id),
          z
            .object({ password: z.string().min(1).max(16384).optional() })
            .strict()
            .parse(input),
        ),
      );
      handle("bastionRespond", (w, id, ref, input) => {
        const answer = z
          .object({
            code: z
              .string()
              .regex(/^(?:\d{6}|\d{8})$/)
              .optional(),
            useStoredTotp: z.boolean().optional(),
          })
          .strict()
          .parse(input);
        visibleCodeWindow(w);
        // Explicit IPC action only. No timer or background auto-submission of OTP.
        return bastion!.respond(
          identifier.parse(id),
          identifier.parse(ref),
          answer,
          () => visibleCodeWindow(w),
        );
      });
      handle("bastionAssets", (_w, id, search, cursor) =>
        bastion!.assets(
          identifier.parse(id),
          z.string().max(256).parse(search),
          cursor === undefined ? undefined : identifier.parse(cursor),
        ),
      );
      handle("bastionOpen", (_w, id, asset, account, protocol) =>
        bastion!.open(
          identifier.parse(id),
          identifier.parse(asset),
          identifier.parse(account),
          z.enum(["ssh", "sftp"]).parse(protocol),
        ),
      );
      handle("bastionRefreshOperation", (_w, id) =>
        bastion!.refresh(identifier.parse(id)),
      );
      handle("bastionCancelOperation", (_w, id) =>
        bastion!.cancel(identifier.parse(id)),
      );
      handle("bastionLogout", (_w, id) =>
        bastion!.logout(identifier.parse(id)),
      );
      handle("bootstrap", () => ({
        bastionAccess: bastion!.snapshot(),
        fileOperations: operations.list(),
        fileAssociations: Object.fromEntries(fileAssociations),
        fileSourceGroups: fileWorkspaces.list(),
        bastionAccounts: store.bastionAccounts(),
        sessions: sessions.list(),
        profiles: store.profiles(),
        reports,
        prompts: prompts.list(),
        diagnose: initial.diagnose,
        transfers: transfers.list(),
        keepAlive: store.keepAlive(),
      }));
      const emitAccounts = (accounts: ReturnType<Store["bastionAccounts"]>) => {
        bastion?.reconcile();
        emit({ type: "bastion-accounts", accounts });
        return accounts;
      };
      handle("saveBastionAccount", async (_w, input) =>
        emitAccounts(await store.saveBastionAccount(input)),
      );
      handle("deleteBastionAccount", async (_w, id) =>
        emitAccounts(await store.deleteBastionAccount(identifier.parse(id))),
      );
      const safeCopy = async (text: string) => {
        try {
          await clipboard.writeText(text);
        } catch {
          throw new Error("系统剪贴板写入失败，请检查权限");
        }
      };
      const visibleCodeWindow = (w: BrowserWindow) => {
        if (!w.isVisible() || w.isMinimized() || !w.isFocused())
          throw new Error("请在活动窗口主动查看或复制验证码");
      };
      handle("bastionCode", async (w, id) => {
        visibleCodeWindow(w);
        const code = await store.bastionCode(identifier.parse(id));
        visibleCodeWindow(w);
        return code;
      });
      handle("copyBastionCode", async (w, id) => {
        visibleCodeWindow(w);
        const code = await store.bastionCode(identifier.parse(id));
        visibleCodeWindow(w);
        await safeCopy(code.code);
        return code;
      });
      handle("copyManualCode", async (_w, input) => {
        const parsed = z
          .string()
          .regex(/^(?:\d{6}|\d{8})$/)
          .safeParse(input);
        if (!parsed.success) throw new Error("请输入 6 或 8 位数字验证码");
        await safeCopy(parsed.data);
      });
      handle("inspectTotp", (_w, input) => {
        if (typeof input !== "string") throw new Error("验证器配置无效");
        return parseTotp(input).parameters;
      });
      handle("setKeepAlive", (_w, value) => {
        const settings = store.setKeepAlive(keepAliveSchema.parse(value));
        emit({ type: "keepalive-settings", settings });
        return settings;
      });
      const emitFileSources = (workspaceId: string) => {
        const group = fileWorkspaces.group(workspaceId);
        emit({ type: "file-sources", workspaceId, group });
        emit({
          type: "file-association",
          workspaceId,
          fileSessionId: group.activeId === workspaceId ? null : group.activeId,
        });
        return group;
      };
      const validateFileSource = (
        workspaceId: unknown,
        sourceId: unknown,
        allowUnavailable = false,
      ) => {
        const workspace = identifier.parse(workspaceId),
          source = identifier.parse(sourceId);
        const owner = sessions.list().find((s) => s.id === workspace);
        if (!owner) throw new Error("工作区已关闭");
        if (
          owner.protocol !== "ssh" &&
          !(owner.protocol === "sftp" && owner.terminalStatus)
        )
          throw new Error("请在终端工作区关联文件来源");
        const target = allowUnavailable
          ? sessions.list().find((s) => s.id === source)
          : sessions.get(source).info;
        if (!target) throw new Error("文件会话已关闭");
        if (workspace !== source && !["sftp", "ftp"].includes(target.protocol))
          throw new Error("请选择独立SFTP或FTP文件会话");
        if (!allowUnavailable && ["closed", "error"].includes(target.status))
          throw new Error("文件连接不可用，请明确重新连接");
        return { workspace, source };
      };
      handle("attachFileSource", (_w, workspaceId, sourceId) => {
        const { workspace, source } = validateFileSource(workspaceId, sourceId);
        fileWorkspaces.attach(workspace, source);
        return emitFileSources(workspace);
      });
      handle("selectFileSource", (_w, workspaceId, sourceId) => {
        const { workspace, source } = validateFileSource(
          workspaceId,
          sourceId,
          true,
        );
        fileWorkspaces.select(workspace, source);
        return emitFileSources(workspace);
      });
      handle("detachFileSource", async (w, workspaceId, sourceId) => {
        const workspace = identifier.parse(workspaceId),
          source = identifier.parse(sourceId);
        if (!sessions.list().some((s) => s.id === workspace))
          throw new Error("工作区已关闭");
        if (source === workspace)
          throw new Error("复用此终端的文件来源不关闭，可收起文件面板");
        if (!fileWorkspaces.group(workspace).sourceIds.includes(source))
          return true;
        const dirty = [...dirtyEditors.values()].some(
          (ids) => ids.has(workspace) || ids.has(source),
        );
        const pending = transfers
          .list()
          .some(
            (t) =>
              t.sessionId === source && ["running", "queued"].includes(t.state),
          );
        if (dirty || pending) {
          const answer = await dialog.showMessageBox(w, {
            type: "question",
            message: "解除此文件标签的关联？",
            detail: `仅解除当前工作区关联，底层连接与其他工作区继续使用。${dirty ? " 未保存文档保留在当前工作区编辑标签中。" : ""}${pending ? " 排队和运行传输继续绑定原文件连接，不会取消。" : ""}`,
            buttons: ["取消", "仅解除关联"],
            defaultId: 0,
            cancelId: 0,
          });
          if (answer.response !== 1) return false;
        }
        if (!sessions.list().some((s) => s.id === workspace))
          throw new Error("工作区已关闭");
        fileWorkspaces.detach(workspace, source);
        emitFileSources(workspace);
        return true;
      });
      handle("linkFiles", async (w, workspaceId, fileSessionId) => {
        identifier.parse(workspaceId);
        sessions.get(workspaceId);
        if (fileSessionId !== null) {
          identifier.parse(fileSessionId);
          const target = sessions.get(fileSessionId);
          if (["closed", "error"].includes(target.info.status))
            throw new Error("所选文件连接已关闭或失败，请明确重新连接");
          if (!["sftp", "ftp"].includes(target.info.protocol))
            throw new Error("请选择独立 SFTP 或 FTP 文件会话");
        }
        if ([...dirtyEditors.values()].some((ids) => ids.has(workspaceId)))
          throw new Error(
            "此工作区有未保存的文件，请先保存或关闭草稿再切换关联",
          );
        const oldId = fileAssociations.get(workspaceId) ?? workspaceId;
        if (
          transfers
            .list()
            .some(
              (t) =>
                t.sessionId === oldId &&
                (t.state === "running" || t.state === "queued"),
            )
        ) {
          const choice = await dialog.showMessageBox(w, {
            type: "warning",
            message: "原文件连接仍有运行中的传输",
            detail: "切换或解除关联不会取消传输，也不会关闭共享文件连接。",
            buttons: ["取消", "保留传输并切换"],
            defaultId: 0,
            cancelId: 0,
          });
          if (choice.response !== 1) return false;
        }
        fileWorkspaces.replace(workspaceId, fileSessionId);
        emitFileSources(workspaceId);
        return true;
      });
      handle("connect", (_w, c) => sessions.connect(connectionSchema.parse(c)));
      handle("connectProfile", async (_w, id) =>
        sessions.connect(await store.resolve(identifier.parse(id))),
      );
      handle("local", (_w, s) =>
        sessions.local(
          z.enum(["powershell", "cmd", "default"]).optional().parse(s),
        ),
      );
      handle("openTerminal", (_w, id) =>
        sessions.openTerminal(identifier.parse(id)),
      );
      handle("editCommand", (w, input) => {
        const action = z
          .enum(["copy", "cut", "paste", "selectAll", "undo", "redo"])
          .parse(input);
        return w.webContents[action]();
      });
      handle("clipboardRead", async () => {
        let text: string;
        try {
          text = await clipboard.readText();
        } catch {
          throw new Error("系统剪贴板读取失败，请检查权限");
        }
        if (Buffer.byteLength(text, "utf8") > TEXT_LIMIT)
          throw new Error("剪贴板文本超过 1 MiB");
        return text;
      });
      handle("clipboardWrite", (_w, input) => {
        const text = z.string().max(TEXT_LIMIT).parse(input);
        if (Buffer.byteLength(text, "utf8") > TEXT_LIMIT)
          throw new Error("复制文本超过 1 MiB");
        return safeCopy(text);
      });
      const guardMutation = (id: string) => {
        if (operations.busy(id))
          throw new Error("目录操作进行中，暂不能保存或修改文件");
      };
      handle("createText", async (_w, id, file, text) => {
        identifier.parse(id);
        guardMutation(id);
        const result = await sessions.fileTask(id, async () =>
          createRemoteText(
            await sessions.sftp(id),
            remotePath.parse(file),
            z.string().max(TEXT_LIMIT).parse(text),
          ),
        );
        emit({ type: "files-changed", sessionId: id, path: result.path });
        return result;
      });
      handle("readText", async (_w, id, file) => {
        identifier.parse(id);
        return sessions.fileTask(id, async () =>
          readRemoteText(await sessions.sftp(id), remotePath.parse(file)),
        );
      });
      handle("saveText", async (_w, id, file, text, revision) => {
        identifier.parse(id);
        guardMutation(id);
        const result = await sessions.fileTask(id, async () =>
          saveRemoteText(
            await sessions.sftp(id),
            remotePath.parse(file),
            z.string().max(TEXT_LIMIT).parse(text),
            z
              .string()
              .regex(/^[a-f0-9]{64}$/)
              .parse(revision),
          ),
        );
        emit({ type: "files-changed", sessionId: id, path: result.path });
        return result;
      });
      handle("clearTransfers", () => transfers.clearCompleted());
      handle("attach", (_w, id) => sessions.attach(identifier.parse(id)));
      handle("setDirty", (w, id, dirty) => {
        identifier.parse(id);
        z.boolean().parse(dirty);
        const entries = dirtyEditors.get(w.id) ?? new Set<string>();
        dirty ? entries.add(id) : entries.delete(id);
        dirtyEditors.set(w.id, entries);
      });
      handle("close", async (w, id) => {
        identifier.parse(id);
        if ([...dirtyEditors.values()].some((ids) => ids.has(id))) {
          const choice = await dialog.showMessageBox(w, {
            type: "warning",
            message: "此会话有未保存的远程文件",
            detail: "关闭会话将丢弃所有窗口中此会话的草稿。",
            buttons: ["继续编辑", "丢弃并关闭会话"],
            defaultId: 0,
            cancelId: 0,
          });
          if (choice.response !== 1) return false;
        }
        const activeTransfers = transfers
          .list()
          .filter(
            (t) =>
              t.sessionId === id && ["running", "queued"].includes(t.state),
          );
        const linked = fileWorkspaces.referencing(id);
        if (activeTransfers.length || linked.length) {
          const choice = await dialog.showMessageBox(w, {
            type: "warning",
            message: "关闭此连接？",
            detail: `此文件连接有 ${activeTransfers.length} 项运行中或排队传输、被 ${linked.length} 个其他工作区关联。关闭将取消本连接传输并令关联文件面板断开，其他终端保持可用。`,
            buttons: ["取消", "关闭连接"],
            defaultId: 0,
            cancelId: 0,
          });
          if (choice.response !== 1) return false;
        }
        for (const ids of dirtyEditors.values()) ids.delete(id);
        transfers.cancelSession(id);
        fileWorkspaces.removeWorkspace(id);
        for (const workspaceId of fileWorkspaces.removeSource(id))
          emitFileSources(workspaceId);
        sessions.close(id);
        return true;
      });
      ipcMain.on("forterm:input", (event, id, data) => {
        owner(event);
        try {
          sessions.input(
            identifier.parse(id),
            z
              .string()
              .max(1024 * 1024)
              .parse(data),
          );
        } catch {}
      });
      ipcMain.on("forterm:resize", (event, id, cols, rows) => {
        owner(event);
        try {
          sessions.resize(
            identifier.parse(id),
            z.number().int().min(2).max(500).parse(cols),
            z.number().int().min(1).max(500).parse(rows),
          );
        } catch {}
      });
      handle("answer", (_w, id, values) => {
        prompts.answer(
          identifier.parse(id),
          z.array(z.string().max(16384)).max(32).nullable().parse(values),
        );
      });
      handle("list", (_w, id, p, requestId) =>
        sessions.listFiles(
          identifier.parse(id),
          remotePath.parse(p),
          requestId === undefined ? undefined : identifier.parse(requestId),
        ),
      );
      handle("cancelFileRequest", (_w, id, requestId) =>
        sessions.cancelFileRequest(
          identifier.parse(id),
          identifier.parse(requestId),
        ),
      );
      handle("mkdir", (_w, id, p) => {
        identifier.parse(id);
        guardMutation(id);
        return sessions.fileTask(id, () =>
          sessions.fileAction(id, "mkdir", remotePath.parse(p)),
        );
      });
      handle("rename", (_w, id, a, b) => {
        identifier.parse(id);
        guardMutation(id);
        return sessions.fileTask(id, () =>
          sessions.fileAction(
            id,
            "rename",
            remotePath.parse(a),
            remotePath.parse(b),
          ),
        );
      });
      handle("filePermissions", (_w, id, p) => {
        identifier.parse(id);
        return sessions.fileTask(id, () =>
          operations.permissions(id, remotePath.parse(p)),
        );
      });
      handle("setFilePermissions", (_w, id, p, normal, expected) => {
        identifier.parse(id);
        if ([...dirtyEditors.values()].some((ids) => ids.has(id)))
          throw new Error("文件连接有未保存草稿，请先保存或关闭再修改权限");
        return sessions.fileTask(id, () =>
          operations.chmod(
            id,
            remotePath.parse(p),
            z.number().int().min(0).max(511).parse(normal),
            z.number().int().min(0).max(4095).parse(expected),
          ),
        );
      });
      handle("cancelFileOperation", (_w, id) =>
        operations.cancel(identifier.parse(id)),
      );
      handle("remove", async (w, id, p, directory) => {
        identifier.parse(id);
        const target = safeOperationPath(remotePath.parse(p));
        z.boolean().parse(directory);
        if ([...dirtyEditors.values()].some((ids) => ids.has(id)))
          throw new Error("此文件连接有未保存草稿，请先保存或关闭后删除");
        if (transfers.activeInPath(id, target))
          throw new Error("目标涉及运行中或排队的传输，请先完成或取消");
        const choice = await dialog.showMessageBox(w, {
          type: "warning",
          message: directory
            ? "删除此目录及其全部子文件/子目录？"
            : "删除远程文件或链接？",
          detail: `文件连接：${id}\n完整路径：${target}\n逐项删除，不是原子操作；失败或取消时已完成的部分不会自动恢复。`,
          buttons: ["取消", "确认删除"],
          defaultId: 0,
          cancelId: 0,
        });
        if (choice.response === 1)
          await sessions.fileTask(id, () => {
            if ([...dirtyEditors.values()].some((ids) => ids.has(id)))
              throw new Error("确认期间产生了未保存草稿，删除已取消");
            return operations.remove(id, target);
          });
      });
      handle("upload", async (w, id, p) => {
        identifier.parse(id);
        remotePath.parse(p);
        const choice = await dialog.showOpenDialog(w, {
          properties: ["openFile", "multiSelections"],
        });
        if (choice.canceled) return;
        if (operations.busy(id)) throw new Error("目录操作进行中，暂不能上传");
        for (const local of choice.filePaths) {
          const dest = path.posix.join(p, path.basename(local));
          await transfers.start(id, local, dest, "upload", async () => {
            const exists = await sessions.remoteExists(id, dest);
            if (!exists) return true;
            const answer = await dialog.showMessageBox(w, {
              message: "覆盖远程文件？",
              detail: dest,
              buttons: ["取消", "覆盖"],
              defaultId: 0,
              cancelId: 0,
            });
            return answer.response === 1;
          });
        }
      });
      handle("download", async (w, id, p) => {
        identifier.parse(id);
        remotePath.parse(p);
        const choice = await dialog.showSaveDialog(w, {
          defaultPath: path.posix.basename(p),
        });
        if (!choice.canceled && choice.filePath)
          await transfers.start(id, choice.filePath, p, "download");
      });
      handle("cancelTransfer", (_w, id) =>
        transfers.cancel(identifier.parse(id)),
      );
      handle("monitor", (_w, id) => sessions.monitor(identifier.parse(id)));
      handle("saveProfile", (_w, p) => store.save(profileSchema.parse(p)));
      handle("deleteProfile", (_w, id) => store.delete(identifier.parse(id)));
      handle("importProfiles", async (w) => {
        const r = await dialog.showOpenDialog(w, {
          filters: [{ name: "Forterm JSON", extensions: ["json"] }],
          properties: ["openFile"],
        });
        if (r.canceled) return store.profiles();
        const data = await readFile(r.filePaths[0], "utf8");
        if (data.length > 4 * 1024 * 1024) throw new Error("配置文件过大");
        const profiles = await store.import(JSON.parse(data));
        emitAccounts(store.bastionAccounts());
        return profiles;
      });
      handle("exportProfiles", async (w) => {
        const r = await dialog.showSaveDialog(w, {
          defaultPath: "forterm-connections.json",
        });
        if (!r.canceled && r.filePath)
          await writeFile(r.filePath, JSON.stringify(store.export(), null, 2), {
            mode: 0o600,
          });
      });
      handle("exportReport", async (w, index) => {
        const report =
          reports[
            z
              .number()
              .int()
              .min(0)
              .max(reports.length - 1)
              .parse(index)
          ];
        const r = await dialog.showSaveDialog(w, {
          defaultPath: "forterm-diagnosis.json",
        });
        if (!r.canceled && r.filePath)
          await writeFile(r.filePath, JSON.stringify(report, null, 2), {
            mode: 0o600,
          });
      });
      handle("pickKey", async (w) => {
        const r = await dialog.showOpenDialog(w, {
          title: "选择 SSH 私钥",
          properties: ["openFile"],
        });
        return r.canceled ? null : r.filePaths[0];
      });
      ready = true;
      await launch(initial);
      for (const l of queued.splice(0)) await launch(l);
    })
    .catch(() => {
      dialog.showErrorBox("Forterm 启动失败", "无法初始化本地配置或桌面环境。");
      app.quit();
    });
  app.on("window-all-closed", () => app.quit());
  app.on("will-quit", () => {
    bastion?.close();
    operations?.close();
    transfers?.close();
    prompts?.close();
    sessions?.closeAll();
  });
}
