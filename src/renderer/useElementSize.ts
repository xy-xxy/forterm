import { useCallback, useLayoutEffect, useState } from "react";
export function useElementSize<T extends HTMLElement>() {
  const [element, setElement] = useState<T | null>(null);
  const [size, setSize] = useState({ width: 0, height: 0 });
  const ref = useCallback((node: T | null) => setElement(node), []);
  useLayoutEffect(() => {
    if (!element) return;
    const update = () => {
      const { width, height } = element.getBoundingClientRect();
      setSize((old) =>
        old.width === width && old.height === height ? old : { width, height },
      );
    };
    update();
    const observer = new ResizeObserver(update);
    observer.observe(element);
    return () => observer.disconnect();
  }, [element]);
  return [ref, size] as const;
}
