import React from 'react';
import { messages } from '../../app/messages';
import { DataFreshness } from '../../components/DataFreshness';
import type { SectionAccessState, UserDetails } from './userDetailApi';

export function IdentitySection({ user, access }: { user: UserDetails; access: SectionAccessState }) {
  return (
    <section className="detail-section" aria-labelledby="identity-section-title">
      <SectionHeader id="identity-section-title" title={messages.userIdentitySection} access={access} />
      {user.isReadOnly && user.sourceOfAuthorityReason && (
        <div className="source-authority" role="status">
          <strong>{messages.userSourceReadOnlyTitle}</strong>
          <p>{user.sourceOfAuthorityReason}</p>
        </div>
      )}
      <dl className="detail-list">
        <DetailItem label={messages.usersNameColumn} value={user.displayName} />
        <DetailItem label={messages.usersUpnColumn} value={user.userPrincipalName} />
        <DetailItem label={messages.usersMailColumn} value={user.mail} />
        <DetailItem label={messages.usersTypeColumn} value={user.userType} />
        <DetailItem label={messages.usersAccountStatusColumn} value={user.accountEnabled === false ? messages.userAccountDisabled : messages.userAccountEnabled} />
      </dl>
    </section>
  );
}

export function SectionHeader({ id, title, access }: { id: string; title: string; access: SectionAccessState }) {
  return (
    <div className="detail-section__header">
      <h2 id={id}>{title}</h2>
      <DataFreshness fetchedAt={access.fetchedAt} freshness={access.freshness} partialData={access.partialData} message={access.error?.message} />
    </div>
  );
}

export function DetailItem({ label, value }: { label: string; value: string | null | undefined }) {
  return (
    <div>
      <dt>{label}</dt>
      <dd>{value || messages.usersUnavailableValue}</dd>
    </div>
  );
}
