import React, { useEffect, useId, useRef, useState } from 'react';

export type ActionMenuItem = {
  label: string;
  onSelect: () => void;
  busy?: boolean;
  disabled?: boolean;
  danger?: boolean;
  description?: string;
  separatorBefore?: boolean;
};

export function ActionMenu({ label, items, onOpenChange, ariaLabel }: { label: string; items: ActionMenuItem[]; onOpenChange?: (open: boolean) => void; ariaLabel?: string }) {
  const idPrefix = useId();
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);

  const close = (returnFocus = true) => {
    setOpen(false);
    onOpenChange?.(false);
    if (returnFocus) triggerRef.current?.focus();
  };

  useEffect(() => {
    if (!open) return;
    const onPointerDown = (event: PointerEvent) => {
      if (!rootRef.current?.contains(event.target as Node)) close();
    };
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') { event.preventDefault(); close(); }
    };
    document.addEventListener('pointerdown', onPointerDown);
    document.addEventListener('keydown', onKeyDown);
    return () => { document.removeEventListener('pointerdown', onPointerDown); document.removeEventListener('keydown', onKeyDown); };
  }, [open]);

  return <div ref={rootRef} className="action-menu">
    <button ref={triggerRef} type="button" className="button button--secondary" aria-label={ariaLabel} aria-haspopup="menu" aria-expanded={open} onClick={() => { const next = !open; setOpen(next); onOpenChange?.(next); }}>{label}</button>
    {open && <div className="action-menu__popover" role="menu" aria-label={ariaLabel ?? label}>
      {items.map((item, index) => {
        const descriptionId = item.description ? `${idPrefix}-${index}` : undefined;
        return <React.Fragment key={item.label}>
          {item.separatorBefore && index > 0 && <div role="separator" className="action-menu__separator" />}
          <button type="button" role="menuitem" aria-label={item.label} aria-describedby={descriptionId} aria-disabled={item.disabled ? true : undefined} className={`button action-menu__item${item.danger ? ' button--danger' : ''}`} disabled={item.busy} onClick={() => { if (item.disabled) return; item.onSelect(); close(); }}>
            <span>{item.busy ? `${item.label}…` : item.label}</span>
            {item.description && <small id={descriptionId} className="action-menu__description">{item.description}</small>}
          </button>
        </React.Fragment>;
      })}
    </div>}
  </div>;
}
