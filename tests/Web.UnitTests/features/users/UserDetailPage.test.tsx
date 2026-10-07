import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { UserDetailPage } from '../../../../src/Web/src/features/users/UserDetailPage';
import type { UserDetailResponse } from '../../../../src/Web/src/features/users/userDetailApi';
import { appRoutes } from '../../../../src/Web/src/app/routes';
import { readFileSync } from 'node:fs';

const apiMock = vi.hoisted(() => vi.fn());
const issueReporter = vi.hoisted(() => ({ report: vi.fn(), clear: vi.fn() }));
vi.mock('../../../../src/Web/src/notifications/WorkspaceNotifications', () => ({ useWorkspaceIssueReporter: () => issueReporter }));

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
  afterEach(() => { cleanup(); apiMock.mockReset(); issueReporter.report.mockClear(); issueReporter.clear.mockClear(); });

  it('omits license and device sections when their modules are unassigned, including nested fetches', async () => {
    render(<UserDetailPage userId="user-1" modules={['users']} loadUserDetail={async () => detail} capabilities={[{ capability: 'devices.view', state: 'allowed', reasonCode: 'active_role' }]} />);
    expect(await screen.findByRole('heading', { name: 'Ada Lovelace' })).toBeTruthy();
    expect(screen.queryByRole('heading', { name: 'Assigned licenses' })).toBeNull();
    expect(screen.queryByRole('heading', { name: 'Associated devices' })).toBeNull();
    expect(apiMock).not.toHaveBeenCalled();
  });

  it('does not render a retained license grant when the route session has disabled the module', async () => {
    window.history.replaceState({}, '', '/users/user-1');
    apiMock.mockResolvedValue(new Response(JSON.stringify(detail), { status: 200 }));
    const route = appRoutes.find((candidate) => candidate.path === '/users/:userId');

    render(<>{route?.render({
      capabilities: [],
      session: { user: {}, workspace: { id: 'workspace-1', name: 'Contoso', moduleAccess: ['users', 'licenses'], enabledModules: ['users'] } },
    })}</>);

    expect(await screen.findByRole('heading', { name: 'Ada Lovelace' })).toBeTruthy();
    expect(screen.queryByRole('heading', { name: 'Assigned licenses' })).toBeNull();
    expect(apiMock).toHaveBeenCalledWith('/api/users/user-1');
  });

  it('summarizes only verified account and license data and guides MFA review', async () => {
    render(<UserDetailPage userId="user-1" modules={['users', 'licenses']} loadUserDetail={async () => detail} />);
    await screen.findByRole('heading', { name: 'Ada Lovelace' });
    const strip = screen.getByRole('region', { name: 'User status summary' });
    expect(strip.textContent).toContain('Account');
    expect(strip.textContent).toContain('Enabled');
    expect(strip.textContent).toContain('1 assigned');
    expect(strip.textContent).toContain('MFA');
    expect(strip.textContent).not.toMatch(/last sign.in/i);
  });

  it('explains each unavailable quick action using its own capability decision', async () => {
    render(<UserDetailPage userId="user-1" loadUserDetail={async () => ({ ...detail, user: { ...detail.user!, isReadOnly: false } })} capabilities={[
      { capability: 'users.update', state: 'read_only', reasonCode: 'role_read_only' },
      { capability: 'users.reset_password', state: 'pim_activation_required', reasonCode: 'pim_activation_required' },
      { capability: 'users.sessions.revoke', state: 'consent_required', reasonCode: 'consent_required' },
    ]} />);
    await screen.findByRole('heading', { name: 'Ada Lovelace' });
    expect(screen.getByText(/Edit user:.*read-only/i)).toBeTruthy();
    expect(screen.getByText(/Reset password:.*PIM/i)).toBeTruthy();
    expect(screen.getByText(/Revoke sessions:.*consent/i)).toBeTruthy();
  });

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
    expect(screen.getAllByText('May be out of date')[0]).toBeTruthy();
    expect(screen.getAllByText('Section data is unavailable. Check Notifications for details.')).toHaveLength(2);
    expect(document.body.textContent).not.toContain('Microsoft Graph throttled this section request.');
  });

  it('retries partial section data in place and clears its issue after recovery', async () => {
    const partial = { ...detail, licenses: { ...detail.licenses, access: { ...detail.licenses.access, partialData: true, freshness: 'stale' as const, error: { category: 'throttled', message: 'private Graph diagnostic' } } } };
    let attempts = 0;
    let finishRetry: ((value: UserDetailResponse) => void) | undefined;
    render(<UserDetailPage userId="user-1" loadUserDetail={async () => ++attempts === 1 ? partial : new Promise<UserDetailResponse>(resolve => { finishRetry = resolve; })} />);
    expect(await screen.findByRole('heading', { name: 'Ada Lovelace' })).toBeTruthy();
    expect(screen.getByText(/partial user details/i)).toBeTruthy();
    expect(issueReporter.report).toHaveBeenCalledWith(expect.objectContaining({ key: 'users:detail' }));
    fireEvent.click(screen.getByRole('button', { name: 'Retry user details' }));
    await waitFor(() => expect(attempts).toBe(2));
    expect(screen.getByRole('heading', { name: 'Ada Lovelace' })).toBeTruthy();
    expect(screen.getByText(/partial user details/i)).toBeTruthy();
    expect((screen.getByRole('button', { name: 'Retrying user details' }) as HTMLButtonElement).disabled).toBe(true);
    finishRetry?.(detail);
    await waitFor(() => expect(screen.queryByText(/partial user details/i)).toBeNull());
    expect(issueReporter.clear).toHaveBeenCalledWith('users:detail');
    expect(screen.getByRole('heading', { name: 'Ada Lovelace' })).toBeTruthy();
  });

  it('wraps a long principal name in the shared user detail header', async () => {
    const style = document.createElement('style');
    style.textContent = readFileSync('src/styles/theme.css', 'utf8');
    document.head.appendChild(style);
    try {
      const longUpn = `${'a'.repeat(120)}@example.com`;
      render(<UserDetailPage userId="user-1" loadUserDetail={async () => ({ ...detail, user: { ...detail.user!, userPrincipalName: longUpn } })} />);
      await screen.findByRole('heading', { name: 'Ada Lovelace' });
      const description = document.querySelector('.user-detail-hero .workspace-page-header__description');
      expect(description?.textContent).toBe(longUpn);
      if (!description) throw new Error('User detail header description missing');
      expect(description.classList.contains('workspace-page-header__description')).toBe(true);
      expect(getComputedStyle(description).overflowWrap).toBe('anywhere');
    } finally { style.remove(); }
  });

  it('reports a failed detail read and clears it after a successful retry', async () => {
    let attempts = 0;
    render(<UserDetailPage userId="user-1" loadUserDetail={async () => { if (++attempts === 1) throw new Error('private Graph diagnostic'); return detail; }} />);
    expect(await screen.findByRole('button', { name: 'Retry' })).toBeTruthy();
    expect(issueReporter.report).toHaveBeenCalledWith(expect.objectContaining({ key: 'users:detail', kind: 'service' }));
    expect(JSON.stringify(issueReporter.report.mock.calls)).not.toContain('private Graph diagnostic');
    expect(document.body.textContent).not.toContain('private Graph diagnostic');
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    await screen.findByRole('heading', { name: 'Ada Lovelace' });
    expect(issueReporter.clear).toHaveBeenCalledWith('users:detail');
    expect(attempts).toBe(2);
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

  it('resets a user password and displays the temporary credential once', async () => {
    apiMock.mockResolvedValue(new Response(JSON.stringify({
      status: 'succeeded',
      requiredCapability: 'users.reset_password',
      replayed: false,
      temporaryCredentialNotice: { temporaryPassword: 'Temp-Password-12345!', forceChangePasswordNextSignIn: true },
    }), { status: 200 }));

    render(<UserDetailPage userId="user-1" loadUserDetail={async () => ({ ...detail, user: { ...detail.user, isReadOnly: false, sourceOfAuthorityReason: null } })} capabilities={[{ capability: 'users.reset_password', state: 'allowed', reasonCode: 'active_role' }]} />);
    fireEvent.click(await screen.findByRole('button', { name: 'Reset password' }));
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.click(screen.getByRole('button', { name: 'Confirm action' }));

    await waitFor(() => expect(apiMock).toHaveBeenCalledWith('/api/users/user-1/reset-password', expect.objectContaining({ method: 'POST' })));
    expect(await screen.findByText('Temp-Password-12345!')).toBeTruthy();
    expect(screen.getByText('The user must change this password at next sign-in.')).toBeTruthy();
  });

  it('does not render group or license mutation controls for read-only sections', async () => {
    render(<UserDetailPage userId="user-1" loadUserDetail={async () => detail} capabilities={[]} />);

    expect(await screen.findByRole('heading', { name: 'Ada Lovelace' })).toBeTruthy();
    expect(screen.queryByRole('button', { name: /add group/i })).toBeNull();
    expect(screen.queryByRole('button', { name: /assign license/i })).toBeNull();
  });

  it('does not render user security mutations for a Global Reader capability snapshot', async () => {
    render(<UserDetailPage userId="user-1" loadUserDetail={async () => detail} capabilities={[{
      capability: 'users.view', state: 'read_only', reasonCode: 'role_read_only',
    }, {
      capability: 'authentication.methods.manage', state: 'read_only', reasonCode: 'role_read_only',
    }, {
      capability: 'users.sessions.revoke', state: 'read_only', reasonCode: 'role_read_only',
    }, {
      capability: 'authentication.methods.view', state: 'read_only', reasonCode: 'role_read_only',
    }]} />);

    expect(await screen.findByRole('heading', { name: 'Ada Lovelace' })).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Grant Temporary Access Pass' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Reset MFA methods' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Revoke sessions' })).toBeNull();
  });

  it('does not infer mutation permission from section access when the capability snapshot omits it', async () => {
    const permittedSections = {
      ...detail,
      groups: { ...detail.groups, access: { ...detail.groups.access, authorization: { capability: 'groups.manage_members', state: 'allowed', reasonCode: 'active_role' } } },
      licenses: { ...detail.licenses, access: { ...detail.licenses.access, authorization: { capability: 'licenses.assign', state: 'allowed', reasonCode: 'active_role' } } },
    };
    render(<UserDetailPage userId="user-1" loadUserDetail={async () => permittedSections} capabilities={[]} />);

    expect(await screen.findByRole('heading', { name: 'Ada Lovelace' })).toBeTruthy();
    expect(screen.queryByRole('button', { name: /add group/i })).toBeNull();
    expect(screen.queryByRole('button', { name: /assign license/i })).toBeNull();
  });

  it('guides direct user-detail routes through PIM when directory access is gated', async () => {
    const gatedDetail: UserDetailResponse = {
      ...detail,
      access: {
        authorization: {
          capability: 'users.view',
          state: 'pim_activation_required',
          reasonCode: 'pim_activation_required',
          nextStep: { label: 'Activate the required Entra role', href: '/identity' },
        },
        fetchedAt: '2026-09-21T08:00:00Z',
        freshness: 'unavailable',
        partialData: true,
        error: { category: 'capability_required', message: 'User details cannot be read until the role is active.', state: 'pim_activation_required' },
      },
      user: null,
    };

    render(<UserDetailPage userId="user-1" loadUserDetail={async () => gatedDetail} />);

    expect(await screen.findByRole('heading', { name: 'Activate an Entra role to view users' })).toBeTruthy();
    expect(screen.getByText('Your eligible Entra role must be active before the directory can be read.')).toBeTruthy();
    expect(screen.getByRole('link', { name: 'Activate the required Entra role' }).getAttribute('href')).toBe('/identity');
  });

  it('keeps the MFA and passkey section visible when delegated consent is required', async () => {
    render(<UserDetailPage userId="user-1" loadUserDetail={async () => detail} capabilities={[{
      capability: 'authentication.methods.view',
      state: 'consent_required',
      reasonCode: 'consent_required',
      nextStep: { label: 'Grant delegated consent', href: '/api/workspaces/current/consent/start' },
    }]} />);

    expect(await screen.findByRole('heading', { name: 'Ada Lovelace' })).toBeTruthy();
    expect(screen.getByRole('heading', { name: 'Authentication methods' })).toBeTruthy();
    expect(screen.getByText('Delegated Microsoft Graph consent is required before this action can run.')).toBeTruthy();
  });

  it('refreshes detail after an allowed group mutation', async () => {
    const loadUserDetail = vi.fn(async () => detail);
    apiMock.mockImplementation(async (path: string) => path.startsWith('/api/groups')
      ? new Response(JSON.stringify({ items: [{ id: 'group-2', displayName: 'Engineering' }], access: { state: 'allowed' } }), { status: 200 })
      : new Response(JSON.stringify({ status: 'succeeded', requiredCapability: 'groups.manage_members', replayed: false }), { status: 200 }));

    render(<UserDetailPage userId="user-1" loadUserDetail={loadUserDetail} capabilities={[{ capability: 'groups.manage_members', state: 'allowed', reasonCode: 'active_role' }, { capability: 'licenses.assign', state: 'allowed', reasonCode: 'active_role' }]} />);
    fireEvent.click(await screen.findByRole('button', { name: /add group/i }));
    fireEvent.change(await screen.findByRole('combobox', { name: /group/i }), { target: { value: 'group-2' } });
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.click(screen.getByRole('button', { name: 'Confirm action' }));

    await waitFor(() => expect(apiMock).toHaveBeenCalledWith('/api/users/user-1/groups/group-2', expect.objectContaining({ method: 'POST' })));
    await waitFor(() => expect(loadUserDetail).toHaveBeenCalledTimes(2));
  });

  it('loads a group catalog for add, excludes assigned groups, and works when the user has no groups', async () => {
    const loadUserDetail = vi.fn(async () => detail);
    apiMock.mockImplementation(async (path: string) => {
      if (path.startsWith('/api/groups')) return new Response(JSON.stringify({ items: [{ id: 'group-2', displayName: 'Engineering' }, { id: 'group-1', displayName: 'Already assigned' }], access: { state: 'allowed' } }), { status: 200 });
      return new Response(JSON.stringify({ status: 'succeeded', requiredCapability: 'groups.manage_members', replayed: false }), { status: 200 });
    });

    render(<UserDetailPage userId="user-1" loadUserDetail={loadUserDetail} capabilities={[{ capability: 'groups.manage_members', state: 'allowed', reasonCode: 'active_role' }]} />);
    fireEvent.click(await screen.findByRole('button', { name: /add group/i }));

    expect(await screen.findByRole('option', { name: 'Engineering' })).toBeTruthy();
    expect(screen.queryByRole('option', { name: 'Already assigned' })).toBeNull();
    fireEvent.change(screen.getByRole('combobox', { name: /group/i }), { target: { value: 'group-2' } });
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.click(screen.getByRole('button', { name: 'Confirm action' }));
    await waitFor(() => expect(apiMock).toHaveBeenCalledWith('/api/users/user-1/groups/group-2', expect.objectContaining({ method: 'POST' })));
    await waitFor(() => expect(loadUserDetail).toHaveBeenCalledTimes(2));
  });

  it('shows actionable catalog mutation errors without refreshing stale detail', async () => {
    const loadUserDetail = vi.fn(async () => detail);
    apiMock.mockImplementation(async (path: string) => {
      if (path.startsWith('/api/groups')) return new Response(JSON.stringify({ items: [{ id: 'group-2', displayName: 'Engineering' }], access: { state: 'allowed' } }), { status: 200 });
      return new Response(JSON.stringify({ status: 'not_found', requiredCapability: 'groups.manage_members', replayed: false, error: 'group_not_found' }), { status: 404 });
    });

    render(<UserDetailPage userId="user-1" loadUserDetail={loadUserDetail} capabilities={[{ capability: 'groups.manage_members', state: 'allowed', reasonCode: 'active_role' }]} />);
    fireEvent.click(await screen.findByRole('button', { name: /add group/i }));
    fireEvent.change(await screen.findByRole('combobox', { name: /group/i }), { target: { value: 'group-2' } });
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.click(screen.getByRole('button', { name: 'Confirm action' }));

    expect((await screen.findByRole('alert')).textContent).toMatch(/selected group is no longer available/i);
    expect(loadUserDetail).toHaveBeenCalledTimes(1);
  });

  it('shows actionable catalog authorization errors before allowing selection', async () => {
    apiMock.mockResolvedValue(new Response(JSON.stringify({ error: 'not_authorized' }), { status: 403 }));
    render(<UserDetailPage userId="user-1" loadUserDetail={async () => detail} capabilities={[{ capability: 'groups.manage_members', state: 'allowed', reasonCode: 'active_role' }]} />);

    fireEvent.click(await screen.findByRole('button', { name: /add group/i }));

    expect((await screen.findByRole('alert')).textContent).toMatch(/Graph denied access to the group catalog/i);
  });

  it('shows actionable license catalog errors returned inside a successful overview response', async () => {
    apiMock.mockResolvedValue(new Response(JSON.stringify({ error: { category: 'not_authorized' }, access: { state: 'allowed' }, items: [] }), { status: 200 }));
    render(<UserDetailPage userId="user-1" loadUserDetail={async () => detail} capabilities={[{ capability: 'licenses.assign', state: 'allowed', reasonCode: 'active_role' }]} />);

    fireEvent.click(await screen.findByRole('button', { name: /assign license/i }));

    expect((await screen.findByRole('alert')).textContent).toMatch(/Graph denied access to the license catalog/i);
  });

  it('loads a license catalog for assignment when the user has no assigned licenses', async () => {
    const emptyDetail = { ...detail, licenses: { ...detail.licenses, items: [] } };
    const loadUserDetail = vi.fn(async () => emptyDetail);
    apiMock.mockImplementation(async (path: string) => path.startsWith('/api/licenses')
      ? new Response(JSON.stringify({ items: [{ skuId: 'sku-2', partNumber: 'E5', displayName: 'Microsoft 365 E5' }], access: { state: 'allowed' } }), { status: 200 })
      : new Response(JSON.stringify({ status: 'succeeded', requiredCapability: 'licenses.assign', replayed: false }), { status: 200 }));

    render(<UserDetailPage userId="user-1" loadUserDetail={loadUserDetail} capabilities={[{ capability: 'licenses.assign', state: 'allowed', reasonCode: 'active_role' }]} />);
    fireEvent.click(await screen.findByRole('button', { name: /assign license/i }));
    expect(await screen.findByRole('option', { name: 'Microsoft 365 E5' })).toBeTruthy();
    fireEvent.change(screen.getByRole('combobox', { name: 'License' }), { target: { value: 'sku-2' } });
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.click(screen.getByRole('button', { name: 'Confirm action' }));
    await waitFor(() => expect(apiMock).toHaveBeenCalledWith('/api/users/user-1/licenses/sku-2', expect.objectContaining({ method: 'POST' })));
    await waitFor(() => expect(loadUserDetail).toHaveBeenCalledTimes(2));
  });

  it('opens group selection when the user has no existing groups', async () => {
    const emptyDetail = { ...detail, groups: { ...detail.groups, items: [] } };
    apiMock.mockResolvedValue(new Response(JSON.stringify({ items: [{ id: 'group-2', displayName: 'Engineering' }], access: { state: 'allowed' } }), { status: 200 }));
    render(<UserDetailPage userId="user-1" loadUserDetail={async () => emptyDetail} capabilities={[{ capability: 'groups.manage_members', state: 'allowed', reasonCode: 'active_role' }]} />);

    fireEvent.click(await screen.findByRole('button', { name: /add group/i }));
    expect(await screen.findByRole('option', { name: 'Engineering' })).toBeTruthy();
  });
});
