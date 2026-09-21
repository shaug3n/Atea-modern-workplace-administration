import React from 'react';
import { messages } from '../../app/messages';
import type { GroupMembership, UserDetailSection } from './userDetailApi';
import { SectionHeader } from './IdentitySection';

export function GroupsSection({ section, canManage, onAdd, onRemove }: { section: UserDetailSection<GroupMembership>; canManage?: boolean; onAdd?: () => void; onRemove?: (group: GroupMembership) => void }) {
  return (
    <section className="detail-section" aria-labelledby="groups-section-title">
      <SectionHeader id="groups-section-title" title={messages.userGroupsSection} access={section.access} />
      {canManage && <button type="button" onClick={onAdd}>Add group</button>}
      {section.items.length === 0 ? <p>{section.access.error?.message ?? messages.userSectionNoData}</p> : (
        <ul className="record-list">
          {section.items.map((group) => (
            <li key={group.id}>
              <strong>{group.displayName || group.id}</strong>
              <span>{group.mailNickname || (group.securityEnabled ? 'Security group' : 'Group')}</span>
              {canManage && <button type="button" onClick={() => onRemove?.(group)}>Remove group</button>}
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
