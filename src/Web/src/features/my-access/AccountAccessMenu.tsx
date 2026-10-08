import React, { useEffect, useRef } from 'react';
import type { AppSession } from '../../components/TenantContextHeader';
import { useAccessTransparency } from './accessContext';
import { summarizeAccess, type AccessSummaryState } from './accessSummary';
import './accountAccessMenu.css';

const stateLabels: Record<AccessSummaryState, string> = {
  allowed: 'Allowed',
  read_only: 'Read only',
  hidden: 'Not available',
  disabled: 'Disabled',
  consent_required: 'Microsoft consent required',
  pim_activation_required: 'PIM activation required',
  pim_approval_required: 'PIM approval required',
  pim_mfa_required: 'PIM MFA required',
  pim_eligibility_expired: 'PIM eligibility expired',
  temporarily_unavailable: 'Temporarily unavailable',
  mixed: 'Mixed results',
  partial: 'Partial evidence',
  unavailable: 'Evidence unavailable',
  workspace_not_granted: 'Workspace access not granted',
  module_disabled: 'Module disabled',
  not_applicable: 'Not applicable',
};

export function AccountAccessMenu({
  session,
  onNavigate,
  onRefresh,
}: {
  session: AppSession;
  onNavigate?: (path: string) => void;
  onRefresh?: () => Promise<void>;
}) {
  const { snapshot, loading, error, refresh } = useAccessTransparency();
  const details = useRef<HTMLDetailsElement>(null);
  const summary = useRef<HTMLElement>(null);
  const access = summarizeAccess(snapshot, session);
  const stale = snapshot !== null && (loading || error);

  useEffect(() => {
    const node = details.current;
    if (!node) return;
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key !== 'Escape' || !node.open) return;
      event.preventDefault();
      node.open = false;
      summary.current?.focus();
    };
    node.addEventListener('keydown', closeOnEscape);
    return () => node.removeEventListener('keydown', closeOnEscape);
  }, []);

  const onSummaryKeyDown = (event: React.KeyboardEvent<HTMLElement>) => {
    if (event.key !== 'Enter' && event.key !== ' ') return;
    event.preventDefault();
    if (details.current) details.current.open = !details.current.open;
  };

  const onMyAccess = (event: React.MouseEvent<HTMLAnchorElement>) => {
    details.current && (details.current.open = false);
    if (onNavigate) {
      event.preventDefault();
      onNavigate('/my-access');
    }
  };

  const handleRefresh = () => void (onRefresh ?? refresh)();

  return (
    <details className="account-access-menu" ref={details}>
      <summary
        className="account-access-menu__summary"
        ref={summary}
        onKeyDown={onSummaryKeyDown}
      >
        DOMAIN ACCESS
      </summary>
      <div className="account-access-menu__panel">
        <div className="account-access-menu__header">
          <strong>DOMAIN ACCESS</strong>
          <button type="button" className="button button--sm button--secondary" onClick={handleRefresh} disabled={loading}>
            {loading ? 'Refreshing access' : 'Refresh access'}
          </button>
        </div>
        {loading && !snapshot
          ? <p className="account-access-menu__status" role="status">Loading access evidence…</p>
          : !snapshot || error || access.sourceState !== 'graph_authoritative'
            ? <p className="account-access-menu__status" role="status">{stale ? 'Previous access check · evidence may be stale' : 'Access evidence unavailable'}</p>
            : stale
              ? <p className="account-access-menu__status" role="status">Previous access check · evidence may be stale</p>
              : null}
        <ul className="account-access-menu__modules">
          {access.modules.map(module => (
            <li key={module.key}>
              <strong>{module.label}</strong>
              <span>Read access: {stateLabels[module.read.state]}</span>
              <span>Write access: {stateLabels[module.write.state]}</span>
            </li>
          ))}
        </ul>
        <a className="account-access-menu__link" href="/my-access" onClick={onMyAccess}>My access</a>
      </div>
    </details>
  );
}
