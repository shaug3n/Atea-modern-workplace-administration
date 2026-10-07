import { cleanup, render, screen } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it } from 'vitest';
import { RolesAndPimSection } from '../../../../src/Web/src/features/users/RolesAndPimSection';

const access = { authorization: { capability: 'roles.assign', state: 'allowed', reasonCode: 'active_role' }, fetchedAt: '2026-09-21T08:00:00Z', freshness: 'fresh', partialData: false } as never;
const pim = { access, items: [] };

describe('RolesAndPimSection', () => {
  afterEach(cleanup);

  it('renders the role name and its state as separate nodes', () => {
    render(<RolesAndPimSection roles={{ access, items: [{ id: 'r1', roleTemplateId: 't1', displayName: 'Global Reader', assignmentState: 'active', directoryScopeId: '/' }] }} pim={pim} />);
    expect(screen.getByText('Global Reader')).toBeTruthy();
    expect(screen.getByText('Active')).toBeTruthy();
    expect(document.body.textContent).not.toContain('Global Readeractive');
  });

  it('renders unknown states as a neutral badge without the raw value', () => {
    render(<RolesAndPimSection roles={{ access, items: [{ id: 'r1', roleTemplateId: 't1', displayName: 'Reader', assignmentState: 'weird_state', directoryScopeId: '/' }] }} pim={pim} />);
    expect(document.body.textContent).not.toContain('weird_state');
    expect(screen.getByText('Weird state')).toBeTruthy();
  });
});
