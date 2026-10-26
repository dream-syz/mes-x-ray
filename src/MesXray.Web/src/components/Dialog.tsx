import { useEffect, useRef, type ReactNode } from "react";
import { X } from "@phosphor-icons/react";

/**
 * A modal sheet over the workspace: closes on Escape, on a click outside and through the close button, and gives the
 * focus back to the element that opened it. Children lay out their own body; `title` adds the standard header.
 */
export function Dialog({ label, title, className, closeLabel, onClose, children }: { label: string; title?: ReactNode; className?: string; closeLabel: string; onClose: () => void; children: ReactNode }) {
  const opener = useRef<Element | null>(null);
  const panel = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    opener.current = document.activeElement;
    // Something inside the sheet must own the focus, or Escape would still reach the page behind it.
    if (!panel.current?.contains(document.activeElement)) panel.current?.focus();
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        event.preventDefault();
        event.stopPropagation();
        onClose();
      }
    };
    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("keydown", onKey);
      (opener.current as HTMLElement | null)?.focus?.();
    };
  }, [onClose]);

  return (
    <div className="dialog-backdrop" onMouseDown={(event) => event.target === event.currentTarget && onClose()}>
      <div ref={panel} className={`dialog ${className ?? ""}`} role="dialog" aria-modal="true" aria-label={label} tabIndex={-1}>
        {title && (
          <div className="dialog-header">
            <h3>{title}</h3>
            <button type="button" className="btn ghost small" onClick={onClose} aria-label={closeLabel} title={closeLabel}>
              <X size={14} weight="bold" />
            </button>
          </div>
        )}
        {children}
      </div>
    </div>
  );
}
