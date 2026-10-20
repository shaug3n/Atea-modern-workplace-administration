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
  const menuRef = useRef<HTMLDivElement>(null);
  const initialFocus = useRef<'first' | 'last'>('first');

  const close = (returnFocus = true) => {
    setOpen(false);
    onOpenChange?.(false);
    if (returnFocus) triggerRef.current?.focus();
  };
  const openMenu = (direction: 'first' | 'last' = 'first') => {
    initialFocus.current = direction;
    setOpen(true);
    onOpenChange?.(true);
  };

  useEffect(() => {
    if (!open) return;
    const enabledItems = menuRef.current?.querySelectorAll<HTMLButtonElement>('button[role="menuitem"]:not(:disabled)');
    if (enabledItems?.length) {
      const index = initialFocus.current === 'last' ? enabledItems.length - 1 : 0;
      enabledItems[index]?.focus();
    } else triggerRef.current?.focus();
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

  const handleMenuKeyDown = (event: React.KeyboardEvent<HTMLDivElement>) => {
    if (!['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) return;
    const enabledItems = Array.from(menuRef.current?.querySelectorAll<HTMLButtonElement>('button[role="menuitem"]:not(:disabled)') ?? []);
    if (!enabledItems.length) return;
    event.preventDefault();
    const currentIndex = enabledItems.indexOf(document.activeElement as HTMLButtonElement);
    const nextIndex = event.key === 'Home'
      ? 0
      : event.key === 'End'
        ? enabledItems.length - 1
        : currentIndex < 0
          ? 0
          : (currentIndex + (event.key === 'ArrowDown' ? 1 : -1) + enabledItems.length) % enabledItems.length;
    enabledItems[nextIndex]?.focus();
  };

  return <div ref={rootRef} className="action-menu">
    <button ref={triggerRef} type="button" className="button button--secondary" aria-label={ariaLabel} aria-haspopup="menu" aria-expanded={open} onKeyDown={event => {
      if (open || (event.key !== 'ArrowDown' && event.key !== 'ArrowUp')) return;
      event.preventDefault();
      openMenu(event.key === 'ArrowUp' ? 'last' : 'first');
    }} onClick={() => { if (open) close(); else openMenu(); }}>{label}</button>
    {open && <div ref={menuRef} className="action-menu__popover" role="menu" aria-label={ariaLabel ?? label} onKeyDown={handleMenuKeyDown}>
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
