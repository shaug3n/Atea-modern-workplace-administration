import React, { useId, useState, type KeyboardEvent, type ReactNode, useRef } from 'react';

export function InfoTip({ label, content }: { label: string; content: ReactNode }) {
  const [open, setOpen] = useState(false);
  const id = useId();
  const triggerRef = useRef<HTMLButtonElement>(null);

  function handleKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key === 'Escape' && open) {
      event.preventDefault();
      setOpen(false);
      triggerRef.current?.focus();
      return;
    }

    if (event.target === triggerRef.current && (event.key === 'Enter' || event.key === ' ')) {
      event.preventDefault();
      setOpen(value => !value);
    }
  }

  return (
    <span className="info-tip" onKeyDown={handleKeyDown}>
      <button
        ref={triggerRef}
        type="button"
        className="info-tip__trigger"
        aria-label={label}
        aria-haspopup="dialog"
        aria-expanded={open}
        aria-controls={id}
        onClick={() => setOpen(value => !value)}
      >
        <span aria-hidden="true">i</span>
      </button>
      {open && (
        <div className="info-tip__popover" id={id} role="dialog" aria-label={label}>
          {content}
        </div>
      )}
    </span>
  );
}
