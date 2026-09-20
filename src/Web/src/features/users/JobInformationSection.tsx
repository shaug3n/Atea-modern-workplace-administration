import React from 'react';
import { messages } from '../../app/messages';
import type { SectionAccessState, UserDetails } from './userDetailApi';
import { DetailItem, SectionHeader } from './IdentitySection';

export function JobInformationSection({ user, access }: { user: UserDetails; access: SectionAccessState }) {
  return (
    <section className="detail-section" aria-labelledby="job-section-title">
      <SectionHeader id="job-section-title" title={messages.userJobSection} access={access} />
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
