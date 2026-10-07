import React, { useEffect, useRef, useState } from 'react';
import type { WorkspaceIssue } from '../notifications/workspaceIssues';
import { formatRelative } from '../format/dateTime';
import { Icon } from './icons';
import { TechnicalDetails } from './TechnicalDetails';

export function NotificationsMenu({ issues, onRefresh, open: controlledOpen, onOpenChange, access }: { issues: WorkspaceIssue[]; onRefresh: () => void | Promise<void>; open?: boolean; onOpenChange?: (open: boolean) => void; access?: { label: string; checkedAt?: string | null } }) {
  const [internalOpen, setInternalOpen] = useState(false);
  const open = controlledOpen ?? internalOpen;
  const setOpen = (value: boolean) => { setInternalOpen(value); onOpenChange?.(value); };
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
  });
  const groups = [...new Set(issues.map(issue => issue.area))];
  return <div className="notifications-menu" ref={container}>
    <button ref={button} type="button" className="button button--icon notifications-menu__button" aria-label={`Notifications, ${count} need attention`} aria-expanded={open} aria-controls="workspace-notifications" data-attention={count > 0 ? 'true' : undefined} onClick={() => setOpen(!open)} onKeyDown={event => { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); setOpen(true); } }}><Icon name="bell" size={20} />{count > 0 && <span className="notifications-menu__count" aria-hidden="true">{count}</span>}</button>
    {open && <section id="workspace-notifications" className="notifications-menu__panel" aria-label="Workspace notifications">
      <div className="notifications-menu__top"><strong>Workspace notifications</strong></div>
      {groups.length === 0 ? <p>No issues need your attention.</p> : groups.map(area => <section key={area}><h2>{area}</h2><ul>{issues.filter(issue => issue.area === area).map(issue => <li key={issue.key}><strong>{issue.title}</strong><p>{issue.detail}</p>{issue.action?.href && <a href={issue.action.href}>{issue.action.label}</a>}{issue.correlationId && <TechnicalDetails items={[{ label: 'Reference', value: issue.correlationId }]} />}</li>)}</ul></section>)}
      <div className="notifications-menu__footer">
        <span>{access ? `Access check: ${access.label}${access.checkedAt ? ` · checked ${formatRelative(access.checkedAt)}` : ''}` : 'Access check'}</span>
        <button type="button" className="button button--sm button--secondary" onClick={() => void onRefresh()}>Refresh</button>
      </div>
    </section>}
  </div>;
}
