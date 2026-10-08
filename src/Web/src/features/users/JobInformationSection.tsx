import React from 'react';
import { messages } from '../../app/messages';
import { DisabledReason } from '../../components/DisabledReason';
import { LoadingSkeleton } from '../../components/LoadingSkeleton';
import type { SectionAccessState, UserDetails } from './userDetailApi';
import { DetailItem, SectionHeader } from './IdentitySection';

export function JobInformationSection({ user, access, canEdit, onEdit, busy = false }: { user: UserDetails; access: SectionAccessState; canEdit?: boolean; onEdit?: () => void; busy?: boolean }) {
  return (
    <section className="detail-section" aria-labelledby="job-section-title" aria-busy={busy}>
      <SectionHeader id="job-section-title" title={messages.userJobSection} access={access} section="identity" actions={(
        user.isReadOnly
          ? <DisabledReason reason={messages.userProfileEditSourceLocked} sourceOfTruth={user.sourceOfAuthorityReason || messages.userSourceReadOnlyTitle}><button type="button" className="button button--tertiary button--sm" disabled>{messages.userProfileEditJobInformation}</button></DisabledReason>
          : canEdit && <button type="button" className="button button--tertiary button--sm" onClick={onEdit}>{messages.userProfileEditJobInformation}</button>
      )} />
      {busy && <LoadingSkeleton label={messages.statusLoading} lines={2} />}
      <dl className="detail-list">
        <DetailItem label={messages.userGivenName} value={user.givenName} />
        <DetailItem label={messages.userSurname} value={user.surname} />
        <DetailItem label={messages.userJobTitle} value={user.jobTitle} />
        <DetailItem label={messages.userDepartment} value={user.department} />
        <DetailItem label={messages.userOfficeLocation} value={user.officeLocation} />
        <DetailItem label={messages.userMobilePhone} value={user.mobilePhone} />
        <DetailItem label={messages.userUsageLocation} value={user.usageLocation} />
      </dl>
    </section>
  );
}
