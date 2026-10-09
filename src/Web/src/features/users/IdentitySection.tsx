import React from 'react';
import { messages } from '../../app/messages';
import { DisabledReason } from '../../components/DisabledReason';
import { LoadingSkeleton } from '../../components/LoadingSkeleton';
import { DataFreshness } from '../../components/DataFreshness';
import type { SectionAccessState, UserDetails } from './userDetailApi';

export function IdentitySection({ user, access, canEdit, onEdit, busy = false }: { user: UserDetails; access: SectionAccessState; canEdit?: boolean; onEdit?: () => void; busy?: boolean }) {
  return (
    <section className="detail-section" aria-labelledby="identity-section-title" aria-busy={busy}>
      <SectionHeader id="identity-section-title" title={messages.userIdentitySection} access={access} section="identity" actions={(canEdit || user.isReadOnly) && (
        user.isReadOnly
          ? <DisabledReason reason={messages.userProfileEditSourceLocked} sourceOfTruth={user.sourceOfAuthorityReason || messages.userSourceReadOnlyTitle}><button type="button" className="button button--tertiary button--sm" disabled>{messages.userProfileEditIdentity}</button></DisabledReason>
          : canEdit && <button type="button" className="button button--tertiary button--sm" onClick={onEdit}>{messages.userProfileEditIdentity}</button>
      )} />
      {user.isReadOnly && user.sourceOfAuthorityReason && (
        <div className="source-authority" role="status">
          <strong>{messages.userSourceReadOnlyTitle}</strong>
          <p>{user.sourceOfAuthorityReason}</p>
        </div>
      )}
      {busy && <LoadingSkeleton label={messages.statusLoading} lines={2} />}
      <dl className="detail-list">
        <DetailItem label={messages.usersNameColumn} value={user.displayName} />
        <DetailItem label={messages.usersUpnColumn} value={user.userPrincipalName} />
        <DetailItem label={messages.usersMailColumn} value={user.mail} />
        <DetailItem label={messages.usersTypeColumn} value={user.userType} />
        <DetailItem label={messages.usersAccountStatusColumn} value={user.accountEnabled === null ? messages.usersUnavailableValue : user.accountEnabled ? messages.userAccountEnabled : messages.userAccountDisabled} />
      </dl>
    </section>
  );
}

export type UserDetailSectionKey = 'identity' | 'licenses' | 'groups' | 'roles';

export const SectionRetryContext = React.createContext<((section: UserDetailSectionKey) => void) | undefined>(undefined);

export function SectionHeader({ id, title, access, section, actions }: { id: string; title: string; access: SectionAccessState; section: UserDetailSectionKey; actions?: React.ReactNode }) {
  const retry = React.useContext(SectionRetryContext);
  const healthy = access.freshness === 'fresh' && !access.partialData && !access.error;
  return (
    <div className="detail-section__header">
      <h2 id={id}>{title}</h2>
      <div className="detail-section__header-actions">
        {!healthy && <DataFreshness fetchedAt={access.fetchedAt} freshness={access.freshness} partialData={access.partialData} message={access.error?.message} onRefresh={retry ? () => retry(section) : undefined} />}
        {actions}
      </div>
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
