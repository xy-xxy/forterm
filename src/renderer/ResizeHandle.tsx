import { useEffect, useId, useRef } from "react";
import { clamp } from "./layout";
export interface ResizeBounds {
  min: number;
  max: number;
  pixelsPerUnit?: number;
}
export function ResizeHandle({
  label,
  axis,
  value,
  bounds,
  onChange,
  onReset,
  reverse = false,
  ratio = false,
  area,
}: {
  label: string;
  axis: "x" | "y";
  value: number;
  bounds: ResizeBounds;
  onChange: (value: number) => void;
  onReset: () => void;
  reverse?: boolean;
  ratio?: boolean;
  area?: string;
}) {
  const id = useId();
  const element = useRef<HTMLDivElement>(null);
  const gesture = useRef<{
    pointer: number;
    coordinate: number;
    value: number;
  } | null>(null);
  const latest = useRef({ bounds, onChange });
  latest.current = { bounds, onChange };
  const finish = () => {
    const active = gesture.current;
    gesture.current = null;
    if (document.body.dataset.resizeOwner === id) {
      delete document.body.dataset.resizeOwner;
      delete document.body.dataset.resizing;
    }
    if (active && element.current?.hasPointerCapture(active.pointer))
      element.current.releasePointerCapture(active.pointer);
    element.current?.removeAttribute("data-dragging");
  };
  useEffect(() => {
    const release = (event: PointerEvent) => {
      if (gesture.current?.pointer === event.pointerId) finish();
    };
    const recover = (event: PointerEvent) => {
      if (gesture.current?.pointer === event.pointerId && event.buttons === 0)
        finish();
    };
    window.addEventListener("blur", finish);
    // Capture-phase cleanup also covers a release over a nested control that
    // consumes the event, or loss of the original element's capture target.
    window.addEventListener("pointerup", release, true);
    window.addEventListener("pointercancel", release, true);
    window.addEventListener("pointermove", recover, true);
    return () => {
      window.removeEventListener("blur", finish);
      window.removeEventListener("pointerup", release, true);
      window.removeEventListener("pointercancel", release, true);
      window.removeEventListener("pointermove", recover, true);
      finish();
    };
  }, []);
  const apply = (next: number) =>
    latest.current.onChange(
      clamp(next, latest.current.bounds.min, latest.current.bounds.max),
    );
  const percent = (n: number) => Math.round(n * (ratio ? 100 : 1));
  return (
    <div
      ref={element}
      className={"resize-handle resize-" + axis}
      style={{ gridArea: area }}
      role="separator"
      tabIndex={0}
      aria-label={label}
      aria-orientation={axis === "x" ? "vertical" : "horizontal"}
      aria-valuemin={percent(bounds.min)}
      aria-valuemax={percent(bounds.max)}
      aria-valuenow={percent(value)}
      aria-valuetext={
        ratio ? `${Math.round(value * 100)}%` : `${Math.round(value)} 像素`
      }
      title={`${label}：拖动或方向键调整；Shift 加速；双击或 Enter 重置`}
      onPointerDown={(event) => {
        if (
          event.button !== 0 ||
          !event.isPrimary ||
          gesture.current ||
          document.body.dataset.resizeOwner
        )
          return;
        event.preventDefault();
        event.stopPropagation();
        gesture.current = {
          pointer: event.pointerId,
          coordinate: axis === "x" ? event.clientX : event.clientY,
          value,
        };
        document.body.dataset.resizeOwner = id;
        document.body.dataset.resizing = axis;
        event.currentTarget.dataset.dragging = "true";
        try {
          event.currentTarget.setPointerCapture(event.pointerId);
        } catch {
          finish();
        }
      }}
      onPointerMove={(event) => {
        const active = gesture.current;
        if (!active || active.pointer !== event.pointerId) return;
        event.preventDefault();
        event.stopPropagation();
        const delta =
          ((axis === "x" ? event.clientX : event.clientY) - active.coordinate) *
          (reverse ? -1 : 1);
        apply(
          active.value + delta / (latest.current.bounds.pixelsPerUnit || 1),
        );
      }}
      onPointerUp={(event) => {
        if (gesture.current?.pointer === event.pointerId) finish();
      }}
      onPointerCancel={(event) => {
        if (gesture.current?.pointer === event.pointerId) finish();
      }}
      onLostPointerCapture={(event) => {
        if (
          gesture.current?.pointer === event.pointerId &&
          !event.currentTarget.hasPointerCapture(event.pointerId)
        )
          finish();
      }}
      onDoubleClick={(event) => {
        event.preventDefault();
        onReset();
      }}
      onKeyDown={(event) => {
        const negative = axis === "x" ? "ArrowLeft" : "ArrowUp";
        const positive = axis === "x" ? "ArrowRight" : "ArrowDown";
        if (![negative, positive, "Home", "End", "Enter"].includes(event.key))
          return;
        event.preventDefault();
        event.stopPropagation();
        if (event.key === "Enter") {
          onReset();
          return;
        }
        if (event.key === "Home" || event.key === "End") {
          apply(event.key === "Home" ? bounds.min : bounds.max);
          return;
        }
        const step = (ratio ? 0.02 : 10) * (event.shiftKey ? 5 : 1);
        apply(
          value + step * (event.key === positive ? 1 : -1) * (reverse ? -1 : 1),
        );
      }}
    >
      <span />
    </div>
  );
}
