import { useCallback, useEffect, useRef, useState, type KeyboardEvent, type PointerEvent } from "react";

export type PaneSide = "left" | "right";
export interface PaneWidths {
  left: number;
  right: number;
}

export const DEFAULT_WIDTHS: PaneWidths = { left: 280, right: 440 };
const LIMITS: Record<PaneSide, [number, number]> = { left: [200, 520], right: [340, 760] };
const STORAGE_KEY = "mes-xray.panes";
const KEY_STEP = 16;

const clamp = (side: PaneSide, value: number): number => Math.min(LIMITS[side][1], Math.max(LIMITS[side][0], Math.round(value)));

function readWidths(): PaneWidths {
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    if (raw) {
      const saved = JSON.parse(raw) as Partial<PaneWidths>;
      return {
        left: typeof saved.left === "number" ? clamp("left", saved.left) : DEFAULT_WIDTHS.left,
        right: typeof saved.right === "number" ? clamp("right", saved.right) : DEFAULT_WIDTHS.right,
      };
    }
  } catch {
    // storage unavailable or corrupt: defaults
  }
  return DEFAULT_WIDTHS;
}

/** Widths of the two side panes, remembered in the browser; the centre pane takes what is left. */
export function usePaneWidths() {
  const [widths, setWidths] = useState<PaneWidths>(readWidths);
  useEffect(() => {
    try {
      window.localStorage.setItem(STORAGE_KEY, JSON.stringify(widths));
    } catch {
      // ignore
    }
  }, [widths]);
  const resize = useCallback((side: PaneSide, width: number) => setWidths((current) => ({ ...current, [side]: clamp(side, width) })), []);
  const reset = useCallback((side: PaneSide) => setWidths((current) => ({ ...current, [side]: DEFAULT_WIDTHS[side] })), []);
  return { widths, resize, reset };
}

/**
 * The grab bar between two panes. Dragging resizes the side pane it belongs to, double-click restores the default,
 * and the arrow keys move it for keyboard users. The left bar grows its pane to the right, the right bar to the left.
 */
export function PaneResizer({ side, width, label, onResize, onReset }: { side: PaneSide; width: number; label: string; onResize: (width: number) => void; onReset: () => void }) {
  const drag = useRef<{ pointerId: number; startX: number; startWidth: number } | null>(null);
  const sign = side === "left" ? 1 : -1;

  const onPointerDown = (event: PointerEvent<HTMLDivElement>) => {
    if (event.button !== 0) return;
    event.preventDefault();
    drag.current = { pointerId: event.pointerId, startX: event.clientX, startWidth: width };
    event.currentTarget.setPointerCapture(event.pointerId);
    document.body.classList.add("is-resizing");
  };
  const onPointerMove = (event: PointerEvent<HTMLDivElement>) => {
    const state = drag.current;
    if (!state || state.pointerId !== event.pointerId) return;
    onResize(state.startWidth + sign * (event.clientX - state.startX));
  };
  const onPointerUp = (event: PointerEvent<HTMLDivElement>) => {
    if (drag.current?.pointerId !== event.pointerId) return;
    drag.current = null;
    event.currentTarget.releasePointerCapture(event.pointerId);
    document.body.classList.remove("is-resizing");
  };
  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key === "ArrowLeft") onResize(width - sign * KEY_STEP);
    else if (event.key === "ArrowRight") onResize(width + sign * KEY_STEP);
    else if (event.key === "Home" || event.key === "Enter") onReset();
    else return;
    event.preventDefault();
  };

  return (
    <div
      className={`pane-resizer pane-resizer-${side}`}
      role="separator"
      aria-orientation="vertical"
      aria-label={label}
      aria-valuenow={width}
      aria-valuemin={LIMITS[side][0]}
      aria-valuemax={LIMITS[side][1]}
      title={label}
      tabIndex={0}
      onPointerDown={onPointerDown}
      onPointerMove={onPointerMove}
      onPointerUp={onPointerUp}
      onPointerCancel={onPointerUp}
      onDoubleClick={onReset}
      onKeyDown={onKeyDown}
    />
  );
}
