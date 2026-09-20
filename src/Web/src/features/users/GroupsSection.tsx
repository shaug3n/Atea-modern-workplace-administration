import React from 'react';
import { messages } from '../../app/messages';
import type { GroupMembership, UserDetailSection } from './userDetailApi';
import { SectionHeader } from './IdentitySection';

export function GroupsSection({ section }: { section: UserDetailSection<GroupMembership> }) {
  return (
    <section className="detail-section" aria-labelledby="groups-section-title">
      <SectionHeader id="groups-section-title" title={messages.userGroupsSection} access={section.access} />
      {section.items.length === 0 ? <p>{section.access.error?.message ?? messages.userSectionNoData}</p> : (
        <ul className="record-list">
          {section.items.map((group) => (
            <li key={group.id}>
              <strong>{group.displayName || group.id}</strong>
              <span>{group.mailNickname || (group.securityEnabled ? 'Security group' : 'Group')}</span>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
