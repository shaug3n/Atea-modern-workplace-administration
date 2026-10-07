import React, { useEffect, useRef, useState } from 'react';
import { Icon } from './icons';

function shorten(value: string) {
  return value.length > 16 ? `${value.slice(0, 8)}…${value.slice(-4)}` : value;
}

export function CopyValue({ value, label, truncate }: { value: string; label: string; truncate?: boolean }) {
  const [copied, setCopied] = useState(false);
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  useEffect(() => () => clearTimeout(timer.current), []);
  const canCopy = typeof navigator !== 'undefined' && Boolean(navigator.clipboard?.writeText);

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(value);
      setCopied(true);
      clearTimeout(timer.current);
      timer.current = setTimeout(() => setCopied(false), 2000);
    } catch {
      setCopied(false);
    }
  };

  return (
    <span className="copy-value">
      {truncate
        ? <><code aria-hidden="true" title={value}>{shorten(value)}</code><span className="sr-only">{value}</span></>
        : <code>{value}</code>}
      {canCopy && <button type="button" className="button button--icon button--sm" aria-label={`Copy ${label}`} onClick={() => void copy()}><Icon name="copy" size={14} /></button>}
      <span role="status" className="copy-value__copied">{copied ? 'Copied' : ''}</span>
    </span>
  );
}
