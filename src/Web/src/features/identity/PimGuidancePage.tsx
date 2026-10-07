import React, { useState } from 'react';
import { WorkspacePageHeader } from '../../components/WorkspacePageHeader';
import { Icon } from '../../components/icons';
import { formatRelative } from '../../format/dateTime';

export function PimGuidancePage({ onRefreshAccess }: { onRefreshAccess?: () => Promise<void> }) {
  const [refreshing, setRefreshing] = useState(false);
  const [refreshedAt, setRefreshedAt] = useState<string | null>(null);
  const refresh = async () => {
    setRefreshing(true);
    try { await onRefreshAccess?.(); setRefreshedAt(new Date().toISOString()); } finally { setRefreshing(false); }
  };
  return <section className="pim-guidance">
    <WorkspacePageHeader title="PIM guidance" description="Check your eligible Entra roles and activate the required role in Microsoft Entra. An activation may require approval or MFA. Return here and refresh access after Microsoft confirms it." />
    <ol className="pim-steps content-panel">
      <li><strong>Open Microsoft Entra PIM</strong><p><a href="https://entra.microsoft.com/#view/Microsoft_Azure_PIMCommon/ActivationMenuBlade" target="_blank" rel="noopener noreferrer">Open Microsoft Entra PIM <Icon name="external" size={14} /><span className="sr-only"> (opens in a new tab)</span></a></p></li>
      <li><strong>Activate the role you need</strong><p>Typical roles are User Administrator and Intune Administrator.</p></li>
      <li><strong>Come back and refresh access</strong>
        <p><button type="button" className="button button--secondary" disabled={refreshing} onClick={() => void refresh()}>{refreshing ? 'Refreshing…' : 'Refresh access'}</button></p>
        {refreshedAt && <p role="status">Access check refreshed {formatRelative(refreshedAt)}</p>}
      </li>
    </ol>
  </section>;
}
