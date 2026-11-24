import { contextBridge, ipcRenderer } from "electron";
import type { FortermAPI, AppEvent } from "../shared/types";
const invoke = (name: string, ...args: unknown[]) =>
  ipcRenderer.invoke("forterm:" + name, ...args);
const api: FortermAPI = {
  attachFileSource: (workspace, source) =>
    invoke("attachFileSource", workspace, source),
  selectFileSource: (workspace, source) =>
    invoke("selectFileSource", workspace, source),
  detachFileSource: (workspace, source) =>
    invoke("detachFileSource", workspace, source),
  bastionAccess: () => invoke("bastionAccess"),
  bastionLogin: (id, input) => invoke("bastionLogin", id, input),
  bastionRespond: (id, ref, input) => invoke("bastionRespond", id, ref, input),
  bastionAssets: (id, search, cursor) =>
    invoke("bastionAssets", id, search, cursor),
  bastionOpen: (id, asset, account, protocol) =>
    invoke("bastionOpen", id, asset, account, protocol),
  bastionRefreshOperation: (id) => invoke("bastionRefreshOperation", id),
  bastionCancelOperation: (id) => invoke("bastionCancelOperation", id),
  bastionLogout: (id) => invoke("bastionLogout", id),
  editCommand: (action) => invoke("editCommand", action),
  filePermissions: (id, path) => invoke("filePermissions", id, path),
  setFilePermissions: (id, path, normal, expected) =>
    invoke("setFilePermissions", id, path, normal, expected),
  cancelFileOperation: (id) => invoke("cancelFileOperation", id),
  clipboardRead: () => invoke("clipboardRead"),
  clipboardWrite: (text) => invoke("clipboardWrite", text),
  createText: (id, path, text) => invoke("createText", id, path, text),
  linkFiles: (workspaceId, fileSessionId) =>
    invoke("linkFiles", workspaceId, fileSessionId),
  saveBastionAccount: (input) => invoke("saveBastionAccount", input),
  deleteBastionAccount: (id) => invoke("deleteBastionAccount", id),
  bastionCode: (id) => invoke("bastionCode", id),
  copyBastionCode: (id) => invoke("copyBastionCode", id),
  copyManualCode: (code) => invoke("copyManualCode", code),
  inspectTotp: (input) => invoke("inspectTotp", input),
  setDirty: (id, dirty) => invoke("setDirty", id, dirty),
  openTerminal: (id) => invoke("openTerminal", id),
  readText: (id, path) => invoke("readText", id, path),
  saveText: (id, path, text, revision) =>
    invoke("saveText", id, path, text, revision),
  clearTransfers: () => invoke("clearTransfers"),
  bootstrap: () => invoke("bootstrap"),
  setKeepAlive: (settings) => invoke("setKeepAlive", settings),
  onEvent: (fn) => {
    const listener = (_e: Electron.IpcRendererEvent, event: AppEvent) =>
      fn(event);
    ipcRenderer.on("forterm:event", listener);
    return () => ipcRenderer.removeListener("forterm:event", listener);
  },
  connect: (c) => invoke("connect", c),
  connectProfile: (id) => invoke("connectProfile", id),
  local: (s) => invoke("local", s),
  attach: (id) => invoke("attach", id),
  input: (id, d) => ipcRenderer.send("forterm:input", id, d),
  resize: (id, c, r) => ipcRenderer.send("forterm:resize", id, c, r),
  close: (id) => invoke("close", id),
  answer: (id, a) => invoke("answer", id, a),
  list: (id, p, requestId) => invoke("list", id, p, requestId),
  cancelFileRequest: (id, requestId) =>
    invoke("cancelFileRequest", id, requestId),
  mkdir: (id, p) => invoke("mkdir", id, p),
  rename: (id, a, b) => invoke("rename", id, a, b),
  remove: (id, p, d) => invoke("remove", id, p, d),
  upload: (id, p) => invoke("upload", id, p),
  download: (id, p) => invoke("download", id, p),
  cancelTransfer: (id) => invoke("cancelTransfer", id),
  monitor: (id) => invoke("monitor", id),
  saveProfile: (p) => invoke("saveProfile", p),
  deleteProfile: (id) => invoke("deleteProfile", id),
  importProfiles: () => invoke("importProfiles"),
  exportProfiles: () => invoke("exportProfiles"),
  exportReport: (i) => invoke("exportReport", i),
  pickKey: () => invoke("pickKey"),
};
contextBridge.exposeInMainWorld("forterm", api);
