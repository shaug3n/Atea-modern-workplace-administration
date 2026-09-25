import React, { useEffect, useRef, useState } from 'react';
import type { WorkspaceIssue } from '../notifications/workspaceIssues';

export function NotificationsMenu({ issues, onRefresh }: { issues: WorkspaceIssue[]; onRefresh: () => void | Promise<void> }) {
  const [open, setOpen] = useState(false);
  const button = useRef<HTMLButtonElement>(null);
  const container = useRef<HTMLDivElement>(null);
  const count = issues.filter(issue => issue.severity === 'warning').length;
  useEffect(() => {
    if (!open) return;
    const keydown = (event: KeyboardEvent) => { if (event.key === 'Escape') { setOpen(false); button.current?.focus(); } };
    const pointer = (event: PointerEvent) => { if (event.target instanceof Node && !container.current?.contains(event.target)) setOpen(false); };
    document.addEventListener('keydown', keydown);
    document.addEventListener('pointerdown', pointer);
    return () => { document.removeEventListener('keydown', keydown); document.removeEventListener('pointerdown', pointer); };
  }, [open]);
  const groups = [...new Set(issues.map(issue => issue.area))];
  return <div className="notifications-menu" ref={container}>
    <button ref={button} type="button" aria-label={`Notifications, ${count} warnings`} aria-expanded={open} aria-controls="workspace-notifications" onClick={() => setOpen(value => !value)} onKeyDown={event => { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); setOpen(true); } }}>Notifications {count > 0 && <span className="notifications-menu__count" aria-hidden="true">{count}</span>}</button>
    {open && <section id="workspace-notifications" className="notifications-menu__panel" aria-label="Workspace notifications">
      <div className="notifications-menu__top"><strong>Workspace notifications</strong><button type="button" onClick={() => void onRefresh()}>Refresh</button></div>
      {groups.length === 0 ? <p>No issues need attention right now.</p> : groups.map(area => <section key={area}><h2>{area}</h2><ul>{issues.filter(issue => issue.area === area).map(issue => <li key={issue.key}><strong>{issue.title}</strong><p>{issue.detail}</p>{issue.action?.href && <a href={issue.action.href}>{issue.action.label}</a>}{issue.correlationId && <small>Reference: {issue.correlationId}</small>}</li>)}</ul></section>)}
    </section>}
  </div>;
}
