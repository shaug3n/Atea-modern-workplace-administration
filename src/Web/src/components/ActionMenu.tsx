import React, { useEffect, useRef, useState } from 'react';

export type ActionMenuItem = {
  label: string;
  onSelect: () => void;
  busy?: boolean;
  disabled?: boolean;
  danger?: boolean;
};

export function ActionMenu({ label, items, onOpenChange }: { label: string; items: ActionMenuItem[]; onOpenChange?: (open: boolean) => void }) {
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
    <button ref={triggerRef} type="button" className="button button--secondary" aria-haspopup="menu" aria-expanded={open} onClick={() => { const next = !open; setOpen(next); onOpenChange?.(next); }}>{label}</button>
    {open && <div className="action-menu__popover" role="menu" aria-label={label}>
      {items.map((item) => <button key={item.label} type="button" role="menuitem" aria-label={item.label} className={`button action-menu__item${item.danger ? ' button--danger' : ''}`} disabled={item.disabled || item.busy} onClick={() => { item.onSelect(); close(); }}>{item.busy ? `${item.label}…` : item.label}</button>)}
    </div>}
  </div>;
}
