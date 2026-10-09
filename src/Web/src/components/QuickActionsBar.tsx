import React, { type ReactNode } from 'react';
import { messages } from '../app/messages';

export function QuickActionsBar({
  children,
  auditNotice = messages.quickActionsAuditNotice,
}: {
  children: ReactNode;
  auditNotice?: string;
}) {
  return (
    <div className="quick-actions-bar">
      <div className="quick-actions-bar__actions">{children}</div>
      <p className="quick-actions-bar__audit-notice">{auditNotice}</p>
    </div>
  );
}
