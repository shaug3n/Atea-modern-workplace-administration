import React from 'react';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { UserDetailPage } from '../../src/Web/src/features/users/UserDetailPage';

const apiMock = vi.hoisted(() => vi.fn((path: string, init?: RequestInit) => window.fetch(path, init)));

vi.mock('../../src/Web/src/auth/useApi', () => ({
  useApi: () => apiMock,
}));

describe('user detail browser boundary', () => {
  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
  });

  it('loads detail from same-origin API only and never calls Graph from the browser', async () => {
    const requests: string[] = [];
    const requestBodies: Array<BodyInit | null | undefined> = [];
    vi.spyOn(window, 'fetch').mockImplementation(async (input, init) => {
      const path = String(input);
      requests.push(path);
      if (path.endsWith('/disable')) requestBodies.push(init?.body);
      expect(new URL(path, window.location.origin).origin).toBe(window.location.origin);
      expect(path).not.toMatch(/graph\.microsoft\.com/i);
      if (path === '/api/users/user-1/disable') {
        return new Response(JSON.stringify({ status: 'succeeded', requiredCapability: 'users.disable', replayed: false }), { status: 200, headers: { 'Content-Type': 'application/json' } });
      }
      expect(path).toBe('/api/users/user-1');
      return new Response(JSON.stringify({
        access: { authorization: { capability: 'users.view', state: 'allowed', reasonCode: 'active_role' }, fetchedAt: new Date().toISOString(), freshness: 'fresh', partialData: false },
        user: { id: 'user-1', displayName: 'Ada Lovelace', userPrincipalName: 'ada@example.com', mail: 'ada@example.com', accountEnabled: true, userType: 'Member', isReadOnly: false, sourceOfAuthority: 'cloud' },
        licenses: { access: { authorization: { capability: 'licenses.assign', state: 'read_only', reasonCode: 'role_read_only' }, fetchedAt: new Date().toISOString(), freshness: 'fresh', partialData: false }, items: [] },
        groups: { access: { authorization: { capability: 'groups.manage_members', state: 'read_only', reasonCode: 'role_read_only' }, fetchedAt: new Date().toISOString(), freshness: 'fresh', partialData: false }, items: [] },
        roles: { access: { authorization: { capability: 'roles.assign', state: 'read_only', reasonCode: 'role_read_only' }, fetchedAt: new Date().toISOString(), freshness: 'fresh', partialData: false }, items: [] },
        pim: { access: { authorization: { capability: 'pim.activate', state: 'read_only', reasonCode: 'role_read_only' }, fetchedAt: new Date().toISOString(), freshness: 'fresh', partialData: false }, items: [] },
      }), { status: 200, headers: { 'Content-Type': 'application/json' } });
    });

    render(<UserDetailPage userId="user-1" capabilities={[
      { capability: 'users.view', state: 'allowed', reasonCode: 'active_role' },
      { capability: 'users.disable', state: 'allowed', reasonCode: 'active_role' },
    ]} />);

    await waitFor(() => expect(document.body.textContent).toContain('Ada Lovelace'));
    expect(requests).toEqual(['/api/users/user-1']);
    expect(document.body.textContent).toContain('Roles and PIM');
    expect(screen.getByRole('tab', { name: 'Identity', selected: true })).toBeTruthy();
    expect(screen.getByText('Phishing status is unavailable.')).toBeTruthy();
    expect(screen.getByText('Last sign-in is unavailable.')).toBeTruthy();
    expect(document.body.textContent).not.toMatch(/MFA (?:compliant|compliance)/i);

    const identityTab = screen.getByRole('tab', { name: 'Identity' });
    fireEvent.keyDown(identityTab, { key: 'ArrowRight' });
    expect(screen.getByRole('tab', { name: 'Devices', selected: true })).toBeTruthy();
    fireEvent.keyDown(screen.getByRole('tab', { name: 'Devices' }), { key: 'End' });
    expect(screen.getByRole('tab', { name: 'Activity', selected: true })).toBeTruthy();
    expect(screen.getByRole('tabpanel', { name: 'Activity' }).textContent).toContain('does not have a supported user activity source');
    fireEvent.keyDown(screen.getByRole('tab', { name: 'Activity' }), { key: 'Home' });
    expect(screen.getByRole('tab', { name: 'Identity', selected: true })).toBeTruthy();

    fireEvent.click(screen.getByRole('button', { name: 'More actions' }));
    fireEvent.click(screen.getByRole('menuitem', { name: 'Disable user' }));
    fireEvent.change(screen.getByLabelText('Type DISABLE to confirm'), { target: { value: 'DISABLE' } });
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: 'Approved access removal' } });
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.click(screen.getByRole('button', { name: 'Disable user' }));
    await waitFor(() => expect(requests).toContain('/api/users/user-1/disable'));
    expect(JSON.parse(String(requestBodies.at(-1)))).toEqual({ reason: 'Approved access removal' });
  });
});
