import { CompactToolbar } from "./CompactToolbar";
import { useEffect, useRef, useState } from "react";
import { Activity, RefreshCw, PanelRightClose } from "lucide-react";
import type { SessionInfo } from "../shared/types";
export function MonitorPanel({
  session,
  visible = true,
  onCollapse,
}: {
  session: SessionInfo;
  visible?: boolean;
  onCollapse?: () => void;
}) {
  const [output, setOutput] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [auto, setAuto] = useState(false);
  const [updated, setUpdated] = useState("");
  const running = useRef(false);
  const mounted = useRef(true);
  const read = async () => {
    if (running.current || session.status !== "ready") return;
    running.current = true;
    setBusy(true);
    setError("");
    try {
      const result = await window.forterm.monitor(session.id);
      if (mounted.current) {
        setOutput(result);
        setUpdated(new Date().toLocaleTimeString("zh-CN"));
      }
    } catch (e) {
      if (mounted.current) {
        setError(String(e).replace(/^Error.*?: /, ""));
        setAuto(false);
      }
    } finally {
      running.current = false;
      if (mounted.current) setBusy(false);
    }
  };
  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);
  useEffect(() => {
    if (!auto || !visible || session.status !== "ready") return;
    void read();
    const timer = setInterval(() => void read(), 10000);
    return () => clearInterval(timer);
  }, [auto, visible, session.id, session.status]);
  const load = output.match(/(?:^|\n)([0-9]+\.[0-9]+)\s+[0-9.]+\s+[0-9.]+/);
  const memory = output.match(/MemTotal:\s+(\d+)\s+kB/);
  return (
    <div className="monitor-panel">
      <CompactToolbar label="监控工具栏">
        <Activity size={16} />
        <h3 className="toolbar-title" title="当前终端资产的服务器状态">
          服务器状态
        </h3>
        <button
          className="secondary"
          aria-label="刷新状态"
          title="手动刷新服务器状态"
          disabled={busy || session.status !== "ready"}
          onClick={() => void read()}
        >
          <RefreshCw size={14} className={busy ? "spin" : ""} />
          <span className="monitor-refresh-label">
            {busy ? "读取中…" : "刷新"}
          </span>
        </button>
        <label className="monitor-auto" title="每 10 秒刷新，仅面板可见时运行">
          <input
            aria-label="每 10 秒刷新（仅面板打开时）"
            type="checkbox"
            checked={auto}
            disabled={session.status !== "ready"}
            onChange={(e) => setAuto(e.target.checked)}
          />
          自动
        </label>
        {updated && (
          <span className="toolbar-meta" title={`更新于 ${updated}`}>
            {updated}
          </span>
        )}
        {onCollapse && (
          <button
            aria-label="收起监控区"
            title="收起监控区"
            onClick={onCollapse}
          >
            <PanelRightClose size={14} />
          </button>
        )}
      </CompactToolbar>
      <div className="monitor-content">
        <p className="monitor-explanation">
          复用当前终端 SSH 的 Linux 指标；负载不是 CPU 使用率，不安装远程程序。
        </p>
        {error && (
          <p className="editor-warning" role="status">
            {error}。终端与文件通道可继续使用。
          </p>
        )}
        {output ? (
          <>
            <div className="metric-grid">
              <div>
                <small>1 分钟负载</small>
                <strong>{load?.[1] ?? "不可用"}</strong>
              </div>
              <div>
                <small>总内存</small>
                <strong>
                  {memory
                    ? (Number(memory[1]) / 1024).toFixed(0) + " MiB"
                    : "不可用"}
                </strong>
              </div>
            </div>
            <details className="monitor-details">
              <summary>查看 CPU / 内存 / 磁盘 / 网络原始数据</summary>
              <pre aria-label="服务器监控数据">{output}</pre>
            </details>
          </>
        ) : (
          !error && (
            <p className="subtle">
              点击刷新后显示负载、CPU 累计计数、内存、磁盘与网络累计计数。
            </p>
          )
        )}
      </div>
    </div>
  );
}
