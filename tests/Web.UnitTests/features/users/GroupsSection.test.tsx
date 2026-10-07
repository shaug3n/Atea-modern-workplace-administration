import { cleanup, render, screen } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it } from 'vitest';
import { GroupsSection } from '../../../../src/Web/src/features/users/GroupsSection';

const access = { authorization: { capability: 'groups.manage_members', state: 'allowed', reasonCode: 'active_role' }, fetchedAt: '2026-09-21T08:00:00Z', freshness: 'fresh', partialData: false } as never;

describe('GroupsSection', () => {
  afterEach(cleanup);

  it('renders a group name once when the nickname matches it', () => {
    render(<GroupsSection section={{ access, items: [{ id: 'g1', displayName: 'haugentech', mailNickname: 'haugentech', securityEnabled: true, groupTypes: [] }] }} />);
    expect(screen.getAllByText('haugentech')).toHaveLength(1);
    expect(screen.getByText('Security group')).toBeTruthy();
  });

  it('shows a differing nickname as secondary text', () => {
    render(<GroupsSection section={{ access, items: [{ id: 'g1', displayName: 'Workplace Operators', mailNickname: 'workplace-operators', securityEnabled: true, groupTypes: [] }] }} />);
    expect(screen.getByText('workplace-operators')).toBeTruthy();
  });
});
