import React from 'react';
import { messages } from '../../app/messages';
import type { DirectoryRoleAssignment, PimEligibility, UserDetailSection } from './userDetailApi';
import { SectionHeader } from './IdentitySection';

export function RolesAndPimSection({ roles, pim }: { roles: UserDetailSection<DirectoryRoleAssignment>; pim: UserDetailSection<PimEligibility> }) {
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
                  <span>{role.assignmentState}</span>
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
                  <span>{eligibility.status}</span>
                  <span>{messages.userRequiredCapability}: {eligibility.requiredCapability}</span>
                  <Requirement enabled={eligibility.requiresApproval} label={messages.userPimApprovalRequired} />
                  <Requirement enabled={eligibility.requiresMfa} label={messages.userPimMfaRequired} />
                  <Requirement enabled={eligibility.requiresJustification} label={messages.userPimJustificationRequired} />
                  {eligibility.maximumDurationMinutes && <span>{messages.userPimTimeLimit}: {eligibility.maximumDurationMinutes} minutes</span>}
                  {eligibility.activationAction && (
                    <button type="button" aria-disabled="true" title={messages.userPimTask11ActionTitle}>
                      {messages.userPimRequestActivation} {eligibility.displayName || eligibility.roleTemplateId}
                    </button>
                  )}
                </li>
              ))}
            </ul>
          )}
        </div>
      </div>
    </section>
  );
}

function Requirement({ enabled, label }: { enabled: boolean; label: string }) {
  return enabled ? <span className="status-chip">{label}</span> : null;
}
