import React from 'react';
import { messages } from '../../app/messages';

export type PimGuidedHandoffData = {
  nextStep: string;
  roleTemplateId: string;
  roleDisplayName: string | null;
  portalUrl: string | null;
  refreshAction: string;
  graphCorrelationId?: string | null;
  graphRequestId?: string | null;
};

export function PimGuidedHandoff({ handoff }: { handoff: PimGuidedHandoffData }) {
  return (
    <section className="permission-panel" role="status" aria-label={messages.pimGuidedHandoffTitle}>
      <h3>{messages.pimGuidedHandoffTitle}</h3>
      <p>{handoff.nextStep}</p>
      <dl className="detail-list">
        <div>
          <dt>{messages.userActiveRoles}</dt>
          <dd>{handoff.roleDisplayName || handoff.roleTemplateId}</dd>
        </div>
        {handoff.graphCorrelationId && (
          <div>
            <dt>{messages.pimGraphCorrelationId}</dt>
            <dd>{handoff.graphCorrelationId}</dd>
          </div>
        )}
      </dl>
      <div className="users-page__actions">
        {handoff.portalUrl && <a href={handoff.portalUrl} target="_blank" rel="noreferrer">{messages.pimOpenEntra}</a>}
        <button type="button" onClick={() => window.location.reload()}>{messages.pimRefresh}</button>
      </div>
    </section>
  );
}
