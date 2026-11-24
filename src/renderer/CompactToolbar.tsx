import {
  useEffect,
  useLayoutEffect,
  useRef,
  useState,
  type ReactNode,
} from "react";
import { ChevronLeft, ChevronRight } from "lucide-react";
export function CompactToolbar({
  label,
  className = "",
  children,
}: {
  label: string;
  className?: string;
  children: ReactNode;
}) {
  const root = useRef<HTMLDivElement>(null),
    viewport = useRef<HTMLDivElement>(null),
    track = useRef<HTMLDivElement>(null);
  const [scroll, setScroll] = useState({
    overflow: false,
    left: false,
    right: false,
  });
  const measure = () => {
    const r = root.current,
      v = viewport.current,
      t = track.current;
    if (!r || !v || !t) return;
    const style = getComputedStyle(r),
      available =
        r.clientWidth -
        parseFloat(style.paddingLeft || "0") -
        parseFloat(style.paddingRight || "0");
    const next = {
      overflow: t.scrollWidth > available + 1,
      left: v.scrollLeft > 1,
      right: v.scrollLeft + v.clientWidth < v.scrollWidth - 1,
    };
    setScroll((old) =>
      Object.keys(next).every(
        (k) => old[k as keyof typeof old] === next[k as keyof typeof next],
      )
        ? old
        : next,
    );
  };
  useLayoutEffect(measure, [children]);
  useEffect(() => {
    const observer = new ResizeObserver(measure);
    if (root.current) observer.observe(root.current);
    if (track.current) observer.observe(track.current);
    return () => observer.disconnect();
  }, []);
  return (
    <div
      className={"compact-toolbar " + className}
      ref={root}
      role="toolbar"
      aria-label={label}
    >
      {scroll.overflow && (
        <button
          type="button"
          className="toolbar-scroll-arrow"
          aria-label={`向左滚动${label}`}
          title="向左查看操作"
          disabled={!scroll.left}
          onClick={() =>
            viewport.current?.scrollBy({ left: -160, behavior: "smooth" })
          }
        >
          <ChevronLeft size={15} />
        </button>
      )}
      <div
        ref={viewport}
        className="compact-toolbar-viewport"
        tabIndex={0}
        aria-label={`${label}（可横向滚动）`}
        onScroll={measure}
      >
        <div ref={track} className="compact-toolbar-track">
          {children}
        </div>
      </div>
      {scroll.overflow && (
        <button
          type="button"
          className="toolbar-scroll-arrow"
          aria-label={`向右滚动${label}`}
          title="向右查看操作"
          disabled={!scroll.right}
          onClick={() =>
            viewport.current?.scrollBy({ left: 160, behavior: "smooth" })
          }
        >
          <ChevronRight size={15} />
        </button>
      )}
    </div>
  );
}
