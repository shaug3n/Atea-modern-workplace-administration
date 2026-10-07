import React, { useState } from 'react';
import { messages } from '../../app/messages';
import { PimActivationDialog } from '../pim/PimActivationDialog';
import type { DirectoryRoleAssignment, PimEligibility, UserDetailSection } from './userDetailApi';
import { StatusBadge } from '../../components/StatusBadge';
import { humanizeAssignmentState } from '../../format/humanize';
import { SectionHeader } from './IdentitySection';

export function RolesAndPimSection({ roles, pim }: { roles: UserDetailSection<DirectoryRoleAssignment>; pim: UserDetailSection<PimEligibility> }) {
  const [selectedEligibility, setSelectedEligibility] = useState<PimEligibility | null>(null);
  return (
    <section className="detail-section" aria-labelledby="roles-pim-section-title">
      <SectionHeader id="roles-pim-section-title" title={messages.userRolesPimSection} access={pim.access.partialData ? pim.access : roles.access} />
      <div className="detail-section__split">
        <div>
          <h3>{messages.userActiveRoles}</h3>
          {roles.items.length === 0 ? <p>{roles.access.error?.message ?? messages.userSectionNoData}</p> : (
            <ul className="record-list">
              {roles.items.map((role) => (
                <li key={role.id}>
                  <strong>{role.displayName || role.roleTemplateId}</strong>
                  <StatusBadge {...humanizeAssignmentState(role.assignmentState)} />
                </li>
              ))}
            </ul>
          )}
        </div>
        <div>
          <h3>{messages.userPimEligibility}</h3>
          {pim.items.length === 0 ? <p>{pim.access.error?.message ?? messages.userSectionNoData}</p> : (
            <ul className="record-list">
              {pim.items.map((eligibility) => (
                <li key={eligibility.id}>
                  <strong>{eligibility.displayName || eligibility.roleTemplateId}</strong>
                  <StatusBadge {...humanizeAssignmentState(eligibility.status)} />
                  <span>{messages.userRequiredCapability}: {eligibility.requiredCapability}</span>
                  <Requirement enabled={eligibility.requiresApproval} label={messages.userPimApprovalRequired} />
                  <Requirement enabled={eligibility.requiresMfa} label={messages.userPimMfaRequired} />
                  <Requirement enabled={eligibility.requiresJustification} label={messages.userPimJustificationRequired} />
                  {eligibility.maximumDurationMinutes && <span>{messages.userPimTimeLimit}: {eligibility.maximumDurationMinutes} minutes</span>}
                  {eligibility.activationAction && (
                    <button
                      type="button"
                      disabled={!CanActivate(eligibility)}
                      title={messages.userPimTask11ActionTitle}
                      onClick={() => setSelectedEligibility(eligibility)}
                    >
                      {messages.userPimRequestActivation} {eligibility.displayName || eligibility.roleTemplateId}
                    </button>
                  )}
                </li>
              ))}
            </ul>
          )}
        </div>
      </div>
      {selectedEligibility && (
        <PimActivationDialog
          eligibility={selectedEligibility}
          onClose={() => setSelectedEligibility(null)}
        />
      )}
    </section>
  );
}

function Requirement({ enabled, label }: { enabled: boolean; label: string }) {
  return enabled ? <span className="status-chip">{label}</span> : null;
}

function CanActivate(eligibility: PimEligibility) {
  return eligibility.activationAvailable && eligibility.status === 'eligible_inactive';
}
