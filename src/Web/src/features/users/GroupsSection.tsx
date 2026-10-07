import React from 'react';
import { messages } from '../../app/messages';
import type { GroupMembership, UserDetailSection } from './userDetailApi';
import { SectionHeader } from './IdentitySection';

export function GroupsSection({ section, canManage, onAdd, onRemove }: { section: UserDetailSection<GroupMembership>; canManage?: boolean; onAdd?: () => void; onRemove?: (group: GroupMembership) => void }) {
  return (
    <section className="detail-section" aria-labelledby="groups-section-title">
      <SectionHeader id="groups-section-title" title={messages.userGroupsSection} access={section.access} />
      {canManage && <button type="button" className="button button--secondary button--sm" onClick={onAdd}>Add group</button>}
      {section.items.length === 0 ? <p>{section.access.error?.message ?? messages.userSectionNoData}</p> : (
        <ul className="record-list">
          {section.items.map((group) => (
            <li key={group.id}>
              <div className="record-list__main">
                <strong>{group.displayName || group.id}</strong>
                {meta(group) && <span className="record-list__meta">{meta(group)}</span>}
              </div>
              {canManage && <button type="button" className="button button--tertiary button--sm button--danger-text" aria-label={`Remove group ${group.displayName || group.id}`} onClick={() => onRemove?.(group)}>Remove</button>}
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

function meta(group: GroupMembership) {
  const name = (group.displayName ?? '').trim().toLowerCase();
  const nickname = (group.mailNickname ?? '').trim();
  if (nickname && nickname.toLowerCase() !== name) return nickname;
  return group.securityEnabled ? 'Security group' : 'Group';
}
