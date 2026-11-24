import type { Connection, Launch, Report } from "../shared/types";
import { APP_VERSION } from "../shared/version";

// Never include raw argv, URL-parser errors, usernames or URI authority in diagnostics.
const known = new Set([
  "-url",
  "-newtab",
  "-newwin",
  "-i",
  "--diagnose",
  "--entry=xshell",
  "--entry=xftp",
  "--entry=diagnose",
]);
function unwrap(s: string): string {
  return s.length > 1 && s.startsWith('"') && s.endsWith('"')
    ? s.slice(1, -1)
    : s;
}
export function parseURL(raw: string): Connection {
  const text = unwrap(raw);
  if (!/^(ssh|sftp|ftp):\/\//i.test(text) || /[\r\n\0]/.test(text))
    throw new Error("需要 SSH、SFTP 或 FTP URL");
  let u: URL;
  try {
    u = new URL(text);
  } catch {
    throw new Error("连接 URL 格式无效");
  }
  if (u.search || u.hash || (u.pathname && u.pathname !== "/"))
    throw new Error(
      "URL 中存在不支持的路径或参数；请对凭据中的特殊字符进行百分号编码",
    );
  let username: string, password: string;
  try {
    username = decodeURIComponent(u.username);
    password = decodeURIComponent(u.password);
  } catch {
    throw new Error("凭据的百分号编码无效");
  }
  const host = u.hostname.replace(/^\[|\]$/g, "");
  const port = u.port
    ? Number(u.port)
    : u.protocol.toLowerCase() === "ftp:"
      ? 21
      : 22;
  if (!host || !username || !Number.isInteger(port) || port < 1 || port > 65535)
    throw new Error("连接地址、用户名或端口无效");
  return {
    protocol:
      u.protocol.toLowerCase() === "ftp:"
        ? "ftp"
        : u.protocol.toLowerCase() === "sftp:"
          ? "sftp"
          : "ssh",
    host,
    port,
    username,
    password,
    temporary: true,
  };
}
export function parseLaunch(argv: string[], entry = "forterm"): Launch {
  let diagnose = entry === "forterm-diagnose",
    newWindow = false,
    rawURL: string | undefined,
    title: string | undefined,
    keyPath: string | undefined,
    error: string | undefined;
  let mode = entry;
  const args = argv.map(unwrap);
  const report: Report = {
    version: 1,
    appVersion: APP_VERSION,
    entry: "forterm",
    time: new Date().toISOString(),
    arguments: [],
    supported: true,
    summary: "",
  };
  for (let i = 0; i < args.length; i++) {
    const arg = args[i],
      low = arg.toLowerCase();
    if (low === "--diagnose" || low === "--entry=diagnose") {
      diagnose = true;
      report.arguments.push({ index: i, kind: "option", value: low });
      continue;
    }
    if (low === "--entry=xshell" || low === "--entry=xftp") {
      mode = low.slice(8);
      report.arguments.push({ index: i, kind: "option", value: low });
      continue;
    }
    if (/^(ssh|sftp|ftp):\/\//i.test(arg)) {
      if (rawURL) error = "一次调用只能传入一个连接";
      rawURL = arg;
      report.arguments.push({
        index: i,
        kind: "connection-url",
        value: `${arg.slice(0, arg.indexOf(":")).toLowerCase()}://<用户>:<凭据>@<地址>:<端口>`,
      });
      continue;
    }
    if (["-url", "-newwin", "-newtab", "-i"].includes(low)) {
      report.arguments.push({ index: i, kind: "option", value: low });
      if (low === "-newwin") newWindow = true;
      const next = args[i + 1];
      if (low === "-newwin" && next?.toLowerCase() === "-url") continue;
      if (next === undefined || known.has(next.toLowerCase())) {
        error = "启动选项缺少参数";
        continue;
      }
      i++;
      report.arguments.push({
        index: i,
        kind:
          low === "-url" || low === "-newwin"
            ? "connection-url"
            : low === "-newtab"
              ? "tab-title"
              : "key-path",
        value: "<已隐藏>",
      });
      if (low === "-newtab") title = next;
      else if (low === "-i") keyPath = next;
      else {
        if (rawURL) error = "一次调用只能传入一个连接";
        rawURL = next;
      }
      continue;
    }
    report.arguments.push({
      index: i,
      kind: "unknown",
      value: "<未知参数已隐藏>",
    });
    error = "包含尚未支持的启动参数，请使用诊断入口";
  }
  let connection: Connection | undefined;
  if (rawURL && !error) {
    try {
      connection = parseURL(rawURL);
      connection.title = title;
      connection.keyPath = keyPath;
      if (mode === "xftp" && connection.protocol !== "ftp")
        connection.protocol = "sftp";
    } catch (e) {
      error = (e as Error).message;
    }
  }
  if ((title || keyPath || newWindow) && !rawURL && !error)
    error = "没有提供连接 URL";
  report.entry = ["xshell", "xftp"].includes(mode)
    ? mode
    : diagnose
      ? "forterm-diagnose"
      : "forterm";
  report.supported = !error;
  report.summary =
    error ??
    (connection
      ? diagnose
        ? `已识别 ${connection.protocol.toUpperCase()} 启动参数；诊断模式未建立连接`
        : `${connection.protocol.toUpperCase()} 临时会话；凭据仅在内存使用`
      : "未传入连接参数");
  return { diagnose, newWindow, connection, report, error };
}
