import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { UserDetailPage } from '../../../../src/Web/src/features/users/UserDetailPage';
import type { UserDetailResponse } from '../../../../src/Web/src/features/users/userDetailApi';

const apiMock = vi.hoisted(() => vi.fn());

vi.mock('../../../../src/Web/src/auth/useApi', () => ({
  useApi: () => apiMock,
}));

const detail: UserDetailResponse = {
  access: { authorization: { capability: 'users.view', state: 'allowed', reasonCode: 'active_role' }, fetchedAt: '2026-09-21T08:00:00Z', freshness: 'fresh', partialData: false },
  user: {
    id: 'user-1',
    displayName: 'Ada Lovelace',
    userPrincipalName: 'ada@example.com',
    mail: 'ada@example.com',
    accountEnabled: true,
    userType: 'Member',
    givenName: 'Ada',
    surname: 'Lovelace',
    jobTitle: 'Principal Engineer',
    department: 'Digital Workplace',
    officeLocation: 'Oslo',
    mobilePhone: '+47 22 00 00 00',
    usageLocation: 'NO',
    isReadOnly: true,
    sourceOfAuthority: 'on_premises_sync',
    sourceOfAuthorityReason: 'This user is synchronized from an on-premises directory and must be edited at the source.',
  },
  licenses: {
    access: { authorization: { capability: 'licenses.assign', state: 'read_only', reasonCode: 'role_read_only' }, fetchedAt: '2026-09-21T08:00:00Z', freshness: 'fresh', partialData: false },
    items: [{ skuId: 'sku-1', skuPartNumber: 'ENTERPRISEPACK', displayName: 'Microsoft 365 E3' }],
  },
  groups: {
    access: { authorization: { capability: 'groups.manage_members', state: 'read_only', reasonCode: 'role_read_only' }, fetchedAt: '2026-09-21T08:00:00Z', freshness: 'fresh', partialData: false },
    items: [{ id: 'group-1', displayName: 'Workplace Operators', mailNickname: 'workplace-operators', securityEnabled: true, groupTypes: [] }],
  },
  roles: {
    access: { authorization: { capability: 'roles.assign', state: 'read_only', reasonCode: 'role_read_only' }, fetchedAt: '2026-09-21T08:00:00Z', freshness: 'fresh', partialData: false },
    items: [{ id: 'role-1', roleTemplateId: 'template-1', displayName: 'Global Reader', assignmentState: 'active', directoryScopeId: '/' }],
  },
  pim: {
    access: { authorization: { capability: 'pim.activate', state: 'pim_mfa_required', reasonCode: 'pim_mfa_required', nextStep: { label: 'Complete MFA for PIM activation', href: '/api/pim/activations' } }, fetchedAt: '2026-09-21T08:00:00Z', freshness: 'fresh', partialData: false },
    items: [{
      id: 'pim-1',
      roleTemplateId: 'template-2',
      displayName: 'User Administrator',
      status: 'eligible_inactive',
      requiredCapability: 'pim.activate',
      activationAvailable: true,
      requiresApproval: true,
      requiresMfa: true,
      requiresJustification: true,
      maximumDurationMinutes: 480,
      activationAction: { action: 'request_activation', href: '/api/pim/activations', method: 'POST', requiredCapability: 'pim.activate', requiresConfirmation: true },
    }],
  },
};

describe('UserDetailPage', () => {
  afterEach(() => cleanup());

  it('renders independent sections, PIM activation contract, and source-of-authority read-only explanation', async () => {
    render(<UserDetailPage userId="user-1" loadUserDetail={async () => detail} />);

    expect(await screen.findByRole('heading', { name: 'Ada Lovelace' })).toBeTruthy();
    expect(screen.getByText('Principal Engineer')).toBeTruthy();
    expect(screen.getByText('Microsoft 365 E3')).toBeTruthy();
    expect(screen.getByText('Workplace Operators')).toBeTruthy();
    expect(screen.getByText('Global Reader')).toBeTruthy();
    expect(screen.getByText('User Administrator')).toBeTruthy();
    expect(screen.getByText('Approval required')).toBeTruthy();
    expect(screen.getByText('MFA required')).toBeTruthy();
    expect(screen.getByText('Justification required')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Request activation for User Administrator' }));
    expect(screen.getByRole('dialog', { name: 'Request PIM activation' })).toBeTruthy();
    expect(screen.getByText(/synchronized from an on-premises directory/)).toBeTruthy();
    expect(screen.queryByRole('button', { name: /Edit user/i })).toBeNull();
  });

  it('shows stale section indicators without hiding permitted identity details', async () => {
    const staleDetail: UserDetailResponse = {
      ...detail,
      licenses: {
        access: { authorization: { capability: 'licenses.assign', state: 'read_only', reasonCode: 'role_read_only' }, fetchedAt: '2026-09-21T08:00:00Z', freshness: 'stale', partialData: true, error: { category: 'throttled', message: 'Microsoft Graph throttled this section request.', statusCode: 429, retryAfterSeconds: 30 } },
        items: [],
      },
    };

    render(<UserDetailPage userId="user-1" loadUserDetail={async () => staleDetail} />);

    expect(await screen.findByRole('heading', { name: 'Ada Lovelace' })).toBeTruthy();
    expect(screen.getByText('Directory data may be stale')).toBeTruthy();
    expect(screen.getAllByText('Microsoft Graph throttled this section request.')).toHaveLength(2);
  });

  it('opens edit for an allowed capability and refreshes the detail after saving', async () => {
    const loadUserDetail = vi.fn(async () => ({ ...detail, user: { ...detail.user, isReadOnly: false, sourceOfAuthorityReason: null } }));
    apiMock.mockResolvedValue(new Response(JSON.stringify({ status: 'succeeded', requiredCapability: 'users.update', replayed: false }), { status: 200 }));

    render(<UserDetailPage userId="user-1" loadUserDetail={loadUserDetail} capabilities={[{ capability: 'users.update', state: 'allowed', reasonCode: 'active_role' }, { capability: 'users.disable', state: 'hidden', reasonCode: 'not_returned' }]} />);
    fireEvent.click(await screen.findByRole('button', { name: 'Edit user' }));
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.click(screen.getByRole('button', { name: 'Confirm action' }));

    await waitFor(() => expect(apiMock).toHaveBeenCalledWith('/api/users/user-1', expect.objectContaining({ method: 'PATCH' })));
    await waitFor(() => expect(loadUserDetail).toHaveBeenCalledTimes(2));
  });

  it('reactivates a disabled user and refreshes the detail', async () => {
    const loadUserDetail = vi.fn(async () => ({ ...detail, user: { ...detail.user, accountEnabled: false, isReadOnly: false, sourceOfAuthorityReason: null } }));
    apiMock.mockResolvedValue(new Response(JSON.stringify({ status: 'succeeded', requiredCapability: 'users.reactivate', replayed: false }), { status: 200 }));

    render(<UserDetailPage userId="user-1" loadUserDetail={loadUserDetail} capabilities={[{ capability: 'users.update', state: 'hidden', reasonCode: 'not_returned' }, { capability: 'users.disable', state: 'allowed', reasonCode: 'active_role' }]} />);
    fireEvent.click(await screen.findByRole('button', { name: 'Reactivate user' }));
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.click(screen.getByRole('button', { name: 'Confirm action' }));

    await waitFor(() => expect(apiMock).toHaveBeenCalledWith('/api/users/user-1/reactivate', expect.objectContaining({ method: 'POST' })));
    await waitFor(() => expect(loadUserDetail).toHaveBeenCalledTimes(2));
  });

  it('does not render group or license mutation controls for read-only sections', async () => {
    render(<UserDetailPage userId="user-1" loadUserDetail={async () => detail} capabilities={[]} />);

    expect(await screen.findByRole('heading', { name: 'Ada Lovelace' })).toBeTruthy();
    expect(screen.queryByRole('button', { name: /add group/i })).toBeNull();
    expect(screen.queryByRole('button', { name: /assign license/i })).toBeNull();
  });

  it('refreshes detail after an allowed group mutation', async () => {
    const loadUserDetail = vi.fn(async () => detail);
    apiMock.mockResolvedValue(new Response(JSON.stringify({ status: 'succeeded', requiredCapability: 'groups.manage_members', replayed: false }), { status: 200 }));

    render(<UserDetailPage userId="user-1" loadUserDetail={loadUserDetail} capabilities={[{ capability: 'groups.manage_members', state: 'allowed', reasonCode: 'active_role' }, { capability: 'licenses.assign', state: 'allowed', reasonCode: 'active_role' }]} />);
    fireEvent.click(await screen.findByRole('button', { name: /add group/i }));
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.click(screen.getByRole('button', { name: 'Confirm action' }));

    await waitFor(() => expect(apiMock).toHaveBeenCalledWith('/api/users/user-1/groups/group-1', expect.objectContaining({ method: 'POST' })));
    await waitFor(() => expect(loadUserDetail).toHaveBeenCalledTimes(2));
  });
});
