import type {
  BastionAccessSnapshot,
  BastionAccessState,
  BastionAssetPage,
  BastionOpenOperation,
  BastionProtocol,
} from "./bastion-access";
import type {
  BastionAccount,
  BastionAccountInput,
  TotpParameters,
  TotpReading,
} from "./bastion";
export type Protocol = "ssh" | "sftp" | "ftp";
export interface KeepAliveSettings {
  enabled: boolean;
  intervalSeconds: number;
  sftpProbe: boolean;
}
export interface Connection {
  protocol: Protocol;
  host: string;
  port: number;
  username: string;
  password?: string;
  keyPath?: string;
  passphrase?: string;
  title?: string;
  temporary?: boolean;
  jumpId?: string;
}
export interface Profile
  extends Omit<Connection, "password" | "passphrase" | "temporary"> {
  id: string;
  group: string;
  hasPassword?: boolean;
}
export interface SessionInfo {
  id: string;
  title: string;
  protocol: Protocol | "local";
  host: string;
  temporary: boolean;
  status: "connecting" | "ready" | "closed" | "error";
  message?: string;
  launchInvocations?: number;
  keepAlive?: KeepAliveSettings;
  terminalStatus?: "opening" | "ready" | "closed" | "error";
  terminalMessage?: string;
}
export interface RemoteText {
  path: string;
  text: string;
  revision: string;
  mode: number;
}
export interface Report {
  version: 1;
  appVersion?: string;
  id?: string;
  invocations?: number;
  lastTime?: string;
  entry: string;
  time: string;
  arguments: { index: number; kind: string; value: string }[];
  supported: boolean;
  summary: string;
}
export interface Launch {
  diagnose: boolean;
  newWindow: boolean;
  connection?: Connection;
  report: Report;
  error?: string;
}
export interface RemoteFile {
  name: string;
  directory: boolean;
  size: number;
  modified: number;
  mode: number | null;
}
export interface Transfer {
  id: string;
  sessionId: string;
  name: string;
  direction: "upload" | "download";
  bytes: number;
  total: number;
  state: "queued" | "running" | "done" | "error" | "cancelled";
  message?: string;
}
export interface Prompt {
  id: string;
  title: string;
  text: string;
  fields: { label: string; secret: boolean }[];
  kind: "auth" | "host";
  changed?: boolean;
}
export interface FileSourceGroup {
  sourceIds: string[];
  activeId: string;
}
export type AppEvent =
  | { type: "file-sources"; workspaceId: string; group: FileSourceGroup }
  | { type: "bastion-access"; snapshot: BastionAccessSnapshot }
  | { type: "window-visibility"; visible: boolean }
  | { type: "files-changed"; sessionId: string; path: string }
  | { type: "file-operation"; operation: FileOperation }
  | {
      type: "file-association";
      workspaceId: string;
      fileSessionId: string | null;
    }
  | { type: "bastion-accounts"; accounts: BastionAccount[] }
  | { type: "transfers-cleared" }
  | { type: "session"; session: SessionInfo }
  | { type: "session-focus"; id: string }
  | { type: "session-removed"; id: string }
  | { type: "data"; id: string; data: string }
  | { type: "transfer"; transfer: Transfer }
  | { type: "prompt"; prompt: Prompt }
  | { type: "prompt-closed"; id: string }
  | { type: "diagnostic"; report: Report }
  | { type: "keepalive-settings"; settings: KeepAliveSettings }
  | { type: "notice"; message: string };
export interface Bootstrap {
  bastionAccess: BastionAccessSnapshot;
  fileOperations: FileOperation[];
  fileAssociations: Record<string, string>;
  fileSourceGroups: Record<string, FileSourceGroup>;
  bastionAccounts: BastionAccount[];
  sessions: SessionInfo[];
  profiles: Profile[];
  reports: Report[];
  prompts: Prompt[];
  diagnose: boolean;
  transfers: Transfer[];
  keepAlive: KeepAliveSettings;
}
export interface FortermAPI {
  bastionAccess(): Promise<BastionAccessSnapshot>;
  bastionLogin(
    id: string,
    input: { password?: string },
  ): Promise<BastionAccessState>;
  bastionRespond(
    id: string,
    challengeRef: string,
    input: { code?: string; useStoredTotp?: boolean },
  ): Promise<BastionAccessState>;
  bastionAssets(
    id: string,
    search: string,
    cursor?: string,
  ): Promise<BastionAssetPage>;
  bastionOpen(
    id: string,
    assetRef: string,
    accountRef: string,
    protocol: BastionProtocol,
  ): Promise<BastionOpenOperation>;
  bastionRefreshOperation(id: string): Promise<BastionOpenOperation>;
  bastionCancelOperation(id: string): Promise<BastionOpenOperation>;
  bastionLogout(id: string): Promise<BastionAccessState>;

  editCommand(
    action: "copy" | "cut" | "paste" | "selectAll" | "undo" | "redo",
  ): Promise<void>;
  filePermissions(id: string, path: string): Promise<FilePermissions>;
  setFilePermissions(
    id: string,
    path: string,
    normal: number,
    expected: number,
  ): Promise<FilePermissions>;
  cancelFileOperation(id: string): Promise<void>;
  clipboardRead(): Promise<string>;
  clipboardWrite(text: string): Promise<void>;
  createText(id: string, path: string, text: string): Promise<RemoteText>;
  attachFileSource(
    workspaceId: string,
    sourceId: string,
  ): Promise<FileSourceGroup>;
  selectFileSource(
    workspaceId: string,
    sourceId: string,
  ): Promise<FileSourceGroup>;
  detachFileSource(workspaceId: string, sourceId: string): Promise<boolean>;
  linkFiles(
    workspaceId: string,
    fileSessionId: string | null,
  ): Promise<boolean>;
  saveBastionAccount(input: BastionAccountInput): Promise<BastionAccount[]>;
  deleteBastionAccount(id: string): Promise<BastionAccount[]>;
  bastionCode(id: string): Promise<TotpReading>;
  copyBastionCode(id: string): Promise<TotpReading>;
  copyManualCode(code: string): Promise<void>;
  inspectTotp(input: string): Promise<TotpParameters>;
  bootstrap(): Promise<Bootstrap>;
  setKeepAlive(settings: KeepAliveSettings): Promise<KeepAliveSettings>;
  onEvent(fn: (e: AppEvent) => void): () => void;
  connect(connection: Connection): Promise<string>;
  connectProfile(id: string): Promise<string>;
  local(shell?: "powershell" | "cmd" | "default"): Promise<string>;
  openTerminal(id: string): Promise<void>;
  readText(id: string, path: string): Promise<RemoteText>;
  saveText(
    id: string,
    path: string,
    text: string,
    revision: string,
  ): Promise<RemoteText>;
  clearTransfers(): Promise<void>;
  attach(id: string): Promise<string>;
  input(id: string, data: string): void;
  resize(id: string, cols: number, rows: number): void;
  close(id: string): Promise<boolean>;
  setDirty(id: string, dirty: boolean): Promise<void>;
  answer(id: string, answers: string[] | null): Promise<void>;
  list(
    id: string,
    path: string,
    requestId?: string,
  ): Promise<{ path: string; files: RemoteFile[] }>;
  cancelFileRequest(id: string, requestId: string): Promise<void>;
  mkdir(id: string, path: string): Promise<void>;
  remove(id: string, path: string, directory: boolean): Promise<void>;
  rename(id: string, from: string, to: string): Promise<void>;
  upload(id: string, path: string): Promise<void>;
  download(id: string, path: string): Promise<void>;
  cancelTransfer(id: string): Promise<void>;
  monitor(id: string): Promise<string>;
  saveProfile(
    profile: Omit<Profile, "id"> & { id?: string; password?: string },
  ): Promise<Profile[]>;
  deleteProfile(id: string): Promise<Profile[]>;
  importProfiles(): Promise<Profile[]>;
  exportProfiles(): Promise<void>;
  exportReport(index: number): Promise<void>;
  pickKey(): Promise<string | null>;
}

export interface FilePermissions {
  sessionId: string;
  path: string;
  mode: number | null;
  supported: boolean;
  link: boolean;
  message?: string;
}
export interface FileOperation {
  id: string;
  sessionId: string;
  path: string;
  state: "running" | "done" | "error" | "cancelled";
  count: number;
  message?: string;
}
