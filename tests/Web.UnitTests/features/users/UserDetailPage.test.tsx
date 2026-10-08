import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
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

  it('renders accessible tabs and supports roving keyboard navigation', async () => {
    render(<UserDetailPage userId="user-1" modules={['users', 'devices', 'licenses']} loadUserDetail={async () => detail} />);
    const tabs = await screen.findAllByRole('tab');
    expect(tabs.map(tab => tab.textContent)).toEqual(['Identity', 'Devices', 'Activity']);
    expect(tabs[0].getAttribute('aria-selected')).toBe('true');
    expect(tabs[0].getAttribute('aria-controls')).toBe('user-profile-panel-identity');
    expect(screen.getByRole('tabpanel', { name: 'Identity' }).id).toBe('user-profile-panel-identity');

    tabs[0].focus();
    fireEvent.keyDown(tabs[0], { key: 'ArrowRight' });
    expect(tabs[1].getAttribute('aria-selected')).toBe('true');
    expect(document.activeElement).toBe(tabs[1]);
    fireEvent.keyDown(tabs[1], { key: 'ArrowLeft' });
    expect(tabs[0].getAttribute('aria-selected')).toBe('true');
    expect(document.activeElement).toBe(tabs[0]);
    fireEvent.keyDown(tabs[0], { key: 'ArrowRight' });
    fireEvent.keyDown(tabs[1], { key: 'End' });
    expect(tabs[2].getAttribute('aria-selected')).toBe('true');
    expect(document.activeElement).toBe(tabs[2]);
    fireEvent.keyDown(tabs[2], { key: 'Home' });
    expect(tabs[0].getAttribute('aria-selected')).toBe('true');
    expect(document.activeElement).toBe(tabs[0]);
  });

  it('shows explicit unavailable summaries and activity without invented records', async () => {
    render(<UserDetailPage userId="user-1" modules={['users', 'licenses']} loadUserDetail={async () => ({
      ...detail,
      user: { ...detail.user!, accountEnabled: null },
      licenses: { ...detail.licenses, access: { ...detail.licenses.access, authorization: { capability: 'licenses.assign', state: 'disabled', reasonCode: 'module_disabled' } } },
    })} />);
    const heading = await screen.findByRole('heading', { name: 'Ada Lovelace' });
    expect(heading).toBeTruthy();
    const summary = screen.getByRole('region', { name: 'User summary' });
    expect(summary.textContent).toContain('Account status unavailable');
    expect(summary.textContent).toContain('Phishing status is unavailable.');
    expect(summary.textContent).toContain('Last sign-in is unavailable.');
    expect(summary.textContent).toContain('Authentication methods are unavailable.');
    expect(summary.textContent).not.toMatch(/MFA (registered|compliant|compliance)/i);
    expect(summary.textContent).not.toContain('1 assigned');
    const activityTab = screen.getByRole('tab', { name: 'Activity' });
    fireEvent.click(activityTab);
    const activity = screen.getByRole('tabpanel', { name: 'Activity' });
    expect(activity.textContent).toContain('This workspace does not have a supported user activity source.');
    expect(within(activity).queryByRole('list')).toBeNull();
  });

  it('summarizes returned authentication methods from the section single-read callback', async () => {
    apiMock.mockResolvedValue(new Response(JSON.stringify({
      userObjectId: 'user-1',
      items: [{ id: 'method-1', type: 'fido2AuthenticationMethod', displayName: 'YubiKey' }],
      fetchedAt: '2026-09-22T08:00:00Z', freshness: 'live', partialData: false, access: { state: 'allowed' },
    }), { status: 200 }));
    render(<UserDetailPage userId="user-1" loadUserDetail={async () => detail} capabilities={[
      { capability: 'authentication.methods.view', state: 'allowed', reasonCode: 'active_role' },
    ]} />);

    const summary = await screen.findByRole('region', { name: 'User summary' });
    await waitFor(() => expect(summary.textContent).toContain('YubiKey'));
    expect(apiMock.mock.calls.filter(([path]) => path === '/api/users/user-1/authentication-methods')).toHaveLength(1);
    expect(summary.textContent).not.toMatch(/MFA (registered|compliant|compliance)/i);
  });

  it('uses actual view and update decisions in the domain access chip', async () => {
    render(<UserDetailPage userId="user-1" modules={['users']} loadUserDetail={async () => detail} capabilities={[
      { capability: 'users.view', state: 'allowed', reasonCode: 'active_role' },
      { capability: 'users.update', state: 'allowed', reasonCode: 'active_role' },
    ]} />);
    await screen.findByRole('heading', { name: 'Ada Lovelace' });
    expect(screen.getByText('Read / write')).toBeTruthy();
  });

  it('keeps independently authorized security actions available for a read-only profile source', async () => {
    render(<UserDetailPage userId="user-1" loadUserDetail={async () => detail} capabilities={[
      { capability: 'users.sessions.revoke', state: 'allowed', reasonCode: 'active_role' },
    ]} />);
    await screen.findByRole('heading', { name: 'Ada Lovelace' });
    fireEvent.click(screen.getByRole('button', { name: 'More actions' }));
    expect(screen.getByRole('menuitem', { name: 'Revoke sessions' })).toBeTruthy();
  });

  it('keeps a returned audit warning visible after a successful quick action', async () => {
    apiMock.mockResolvedValue(new Response(JSON.stringify({ status: 'succeeded', auditWarning: 'Audit record could not be written.' }), { status: 200 }));
    render(<UserDetailPage userId="user-1" loadUserDetail={async () => ({ ...detail, user: { ...detail.user!, isReadOnly: false } })} capabilities={[
      { capability: 'users.disable', state: 'allowed', reasonCode: 'active_role' },
    ]} />);

    await screen.findByRole('heading', { name: 'Ada Lovelace' });
    fireEvent.click(screen.getByRole('button', { name: 'More actions' }));
    fireEvent.click(screen.getByRole('menuitem', { name: 'Disable user' }));
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: '  Account security  ' } });
    fireEvent.change(screen.getByLabelText('Type DISABLE to confirm'), { target: { value: 'DISABLE' } });
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.click(screen.getByRole('button', { name: 'Disable user' }));

    expect((await screen.findByRole('alert')).textContent).toContain('Audit record could not be written.');
    expect(JSON.parse(apiMock.mock.calls.at(-1)![1].body)).toEqual({ reason: 'Account security' });
  });

  it('keeps a session-revocation audit warning visible after success', async () => {
    apiMock.mockResolvedValue(new Response(JSON.stringify({ status: 'succeeded', auditWarning: 'Sessions were revoked, but the audit record could not be written.' }), { status: 200 }));
    render(<UserDetailPage userId="user-1" loadUserDetail={async () => detail} capabilities={[
      { capability: 'users.sessions.revoke', state: 'allowed', reasonCode: 'active_role' },
    ]} />);

    await screen.findByRole('heading', { name: 'Ada Lovelace' });
    fireEvent.click(screen.getByRole('button', { name: 'More actions' }));
    fireEvent.click(screen.getByRole('menuitem', { name: 'Revoke sessions' }));
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: '  Suspected token exposure  ' } });
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.click(screen.getByRole('button', { name: 'Revoke sessions' }));

    expect((await screen.findByRole('alert')).textContent).toContain('Sessions were revoked, but the audit record could not be written.');
    expect(JSON.parse(apiMock.mock.calls[0][1].body)).toEqual({ reason: 'Suspected token exposure' });
  });

  it('offers card-level profile editing through the existing editor only when source editing is allowed', async () => {
    render(<UserDetailPage userId="user-1" loadUserDetail={async () => ({ ...detail, user: { ...detail.user!, isReadOnly: false } })} capabilities={[
      { capability: 'users.update', state: 'allowed', reasonCode: 'active_role' },
    ]} />);
    await screen.findByRole('heading', { name: 'Ada Lovelace' });
    fireEvent.click(screen.getByRole('button', { name: 'Edit job information' }));
    expect(screen.getByRole('dialog', { name: 'Edit user' })).toBeTruthy();
  });

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

  it('summarizes verified account and license data with explicit unavailable security signals', async () => {
    render(<UserDetailPage userId="user-1" modules={['users', 'licenses']} loadUserDetail={async () => detail} />);
    await screen.findByRole('heading', { name: 'Ada Lovelace' });
    const strip = screen.getByRole('region', { name: 'User summary' });
    expect(strip.textContent).toContain('Account');
    expect(strip.textContent).toContain('Account enabled');
    expect(strip.textContent).toContain('1 assigned');
    expect(strip.textContent).toContain('Known authentication methods');
    expect(strip.textContent).toContain('Last sign-in is unavailable.');
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

  it.each([
    ['read_only', 'This action is read-only for your current Entra role.'],
    ['consent_required', 'Delegated Microsoft Graph consent is required before this action can run.'],
    ['pim_activation_required', 'Activate the required Entra role in PIM before continuing.'],
    ['pim_approval_required', 'This action is waiting for PIM approval.'],
    ['pim_mfa_required', 'Complete MFA for PIM activation before continuing.'],
    ['pim_eligibility_expired', 'Your PIM eligibility has expired. Request renewed access.'],
    ['disabled', 'This action is disabled for the current workspace.'],
    ['temporarily_unavailable', 'Microsoft Graph authorization could not be verified. Try again later.'],
  ] as const)('keeps disable unavailable state %s visible without dispatch', async (state, reason) => {
    render(<UserDetailPage userId="user-1" loadUserDetail={async () => ({ ...detail, user: { ...detail.user!, isReadOnly: false } })} capabilities={[
      { capability: 'users.disable', state, reasonCode: state },
    ]} />);

    await screen.findByRole('heading', { name: 'Ada Lovelace' });
    const disable = screen.getByRole('button', { name: 'Disable user' }) as HTMLButtonElement;
    expect(disable.disabled).toBe(true);
    expect(disable.closest('.disabled-reason')?.textContent).toContain(reason);
    fireEvent.click(disable);
    expect(apiMock).not.toHaveBeenCalled();
  });

  it('suppresses the disable action when its capability is hidden', async () => {
    render(<UserDetailPage userId="user-1" loadUserDetail={async () => ({ ...detail, user: { ...detail.user!, isReadOnly: false } })} capabilities={[
      { capability: 'users.disable', state: 'hidden', reasonCode: 'not_returned' },
    ]} />);

    await screen.findByRole('heading', { name: 'Ada Lovelace' });
    expect(screen.queryByRole('button', { name: 'Disable user' })).toBeNull();
    expect(screen.queryByText(/Disable user:/)).toBeNull();
    expect(apiMock).not.toHaveBeenCalled();
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
    expect(screen.getAllByText(/synchronized from an on-premises directory/).length).toBeGreaterThan(0);
    const lockedReason = screen.getAllByText('Profile fields cannot be edited here because this account is managed by its source of authority.')[0];
    expect(lockedReason.closest('.disabled-reason')?.tabIndex).toBe(0);
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
    expect(screen.getByText("Some sections couldn't load")).toBeTruthy();
    expect(issueReporter.report).toHaveBeenCalledWith(expect.objectContaining({ key: 'users:detail' }));
    fireEvent.click(screen.getByRole('button', { name: 'Retry user details' }));
    await waitFor(() => expect(attempts).toBe(2));
    expect(screen.getByRole('heading', { name: 'Ada Lovelace' })).toBeTruthy();
    expect(screen.getByText("Some sections couldn't load")).toBeTruthy();
    expect((screen.getByRole('button', { name: 'Retrying…' }) as HTMLButtonElement).disabled).toBe(true);
    finishRetry?.(detail);
    await waitFor(() => expect(screen.queryByText("Some sections couldn't load")).toBeNull());
    expect(issueReporter.clear).toHaveBeenCalledWith('users:detail');
    expect(screen.getByRole('heading', { name: 'Ada Lovelace' })).toBeTruthy();
  });

  it('refreshes only the failed license section while leaving the profile usable', async () => {
    const stale = { ...detail, licenses: { ...detail.licenses, access: { ...detail.licenses.access, freshness: 'stale' as const, partialData: true } } };
    let finishRetry: ((value: UserDetailResponse) => void) | undefined;
    let attempts = 0;
    render(<UserDetailPage userId="user-1" modules={['users', 'licenses']} loadUserDetail={async () => ++attempts === 1 ? stale : new Promise<UserDetailResponse>(resolve => { finishRetry = resolve; })} />);
    await screen.findByRole('heading', { name: 'Ada Lovelace' });
    const licenses = screen.getByRole('region', { name: 'Assigned licenses' });
    fireEvent.click(within(licenses).getByRole('button', { name: 'Refresh' }));
    await waitFor(() => expect(attempts).toBe(2));

    expect(licenses.getAttribute('aria-busy')).toBe('true');
    expect(screen.getByRole('region', { name: 'User summary' }).getAttribute('aria-busy')).toBeNull();
    finishRetry?.({ ...detail, user: { ...detail.user!, displayName: 'Grace Hopper' }, licenses: { ...detail.licenses, items: [] } });
    await waitFor(() => expect(licenses.getAttribute('aria-busy')).toBe('false'));
    expect(screen.getByRole('heading', { name: 'Ada Lovelace' })).toBeTruthy();
    expect(screen.queryByRole('heading', { name: 'Grace Hopper' })).toBeNull();
    expect(screen.getByRole('tabpanel', { name: 'Identity' })).toBeTruthy();
  });

  it('ignores a section retry response after navigating to another user', async () => {
    const stale = { ...detail, licenses: { ...detail.licenses, access: { ...detail.licenses.access, freshness: 'stale' as const, partialData: true } } };
    let userOneLoads = 0;
    let finishRetry: ((value: UserDetailResponse) => void) | undefined;
    const loadUserDetail = (id: string) => {
      if (id === 'user-2') return Promise.resolve({ ...detail, user: { ...detail.user!, id, displayName: 'Grace Hopper' }, licenses: { ...detail.licenses, items: [{ skuId: 'new-sku', skuPartNumber: 'NEW', displayName: 'User Two License' }] } });
      if (++userOneLoads === 1) return Promise.resolve(stale);
      return new Promise<UserDetailResponse>(resolve => { finishRetry = resolve; });
    };
    const view = render(<UserDetailPage userId="user-1" modules={['users', 'licenses']} loadUserDetail={loadUserDetail} />);
    await screen.findByRole('heading', { name: 'Ada Lovelace' });
    const licenses = screen.getByRole('region', { name: 'Assigned licenses' });
    fireEvent.click(within(licenses).getByRole('button', { name: 'Refresh' }));
    await waitFor(() => expect(userOneLoads).toBe(2));
    view.rerender(<UserDetailPage userId="user-2" modules={['users', 'licenses']} loadUserDetail={loadUserDetail} />);
    await screen.findByRole('heading', { name: 'Grace Hopper' });
    finishRetry?.({ ...detail, licenses: { ...detail.licenses, items: [{ skuId: 'old-sku', skuPartNumber: 'OLD', displayName: 'Late User One License' }] } });

    expect(await screen.findByText('User Two License')).toBeTruthy();
    await waitFor(() => expect(screen.queryByText('Late User One License')).toBeNull());
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
    apiMock.mockResolvedValue(new Response(JSON.stringify({ status: 'succeeded', requiredCapability: 'users.update', replayed: false, auditWarning: 'Profile changed, but the audit record could not be written.' }), { status: 200 }));

    render(<UserDetailPage userId="user-1" loadUserDetail={loadUserDetail} capabilities={[{ capability: 'users.update', state: 'allowed', reasonCode: 'active_role' }, { capability: 'users.disable', state: 'hidden', reasonCode: 'not_returned' }]} />);
    fireEvent.click(await screen.findByRole('button', { name: 'Edit user' }));
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: '  Correcting identity  ' } });
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.click(screen.getByRole('button', { name: 'Save changes' }));

    await waitFor(() => expect(apiMock).toHaveBeenCalledWith('/api/users/user-1', expect.objectContaining({ method: 'PATCH' })));
    expect(JSON.parse(apiMock.mock.calls[0][1].body)).toMatchObject({ displayName: 'Ada Lovelace', reason: 'Correcting identity' });
    await waitFor(() => expect(loadUserDetail).toHaveBeenCalledTimes(2));
    expect((await screen.findByRole('alert')).textContent).toContain('Profile changed, but the audit record could not be written.');
  });

  it('reactivates a disabled user and refreshes the detail', async () => {
    const loadUserDetail = vi.fn(async () => ({ ...detail, user: { ...detail.user, accountEnabled: false, isReadOnly: false, sourceOfAuthorityReason: null } }));
    apiMock.mockResolvedValue(new Response(JSON.stringify({ status: 'succeeded', requiredCapability: 'users.reactivate', replayed: false, auditWarning: 'Account enabled, but the audit record could not be written.' }), { status: 200 }));

    render(<UserDetailPage userId="user-1" loadUserDetail={loadUserDetail} capabilities={[{ capability: 'users.update', state: 'hidden', reasonCode: 'not_returned' }, { capability: 'users.disable', state: 'allowed', reasonCode: 'active_role' }]} />);
    await screen.findByRole('heading', { name: 'Ada Lovelace' });
    expect(screen.getByRole('region', { name: 'User summary' }).textContent).toContain('Account disabled');
    fireEvent.click(await screen.findByRole('button', { name: 'Reactivate user' }));
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: '  Access restored  ' } });
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.click(screen.getByRole('button', { name: 'Enable user' }));

    await waitFor(() => expect(apiMock).toHaveBeenCalledWith('/api/users/user-1/reactivate', expect.objectContaining({ method: 'POST' })));
    expect(JSON.parse(apiMock.mock.calls[0][1].body)).toEqual({ reason: 'Access restored' });
    await waitFor(() => expect(loadUserDetail).toHaveBeenCalledTimes(2));
    expect((await screen.findByRole('alert')).textContent).toContain('Account enabled, but the audit record could not be written.');
  });

  it('keeps a failed reactivation alert and entered reason inside its dialog', async () => {
    apiMock.mockRejectedValue(new Error('offline'));
    render(<UserDetailPage userId="user-1" loadUserDetail={async () => ({ ...detail, user: { ...detail.user, accountEnabled: false, isReadOnly: false, sourceOfAuthorityReason: null } })} capabilities={[{ capability: 'users.update', state: 'hidden', reasonCode: 'not_returned' }, { capability: 'users.disable', state: 'allowed', reasonCode: 'active_role' }]} />);
    await screen.findByRole('heading', { name: 'Ada Lovelace' });
    fireEvent.click(await screen.findByRole('button', { name: 'Reactivate user' }));
    const dialog = screen.getByRole('dialog', { name: 'Reactivate user' });
    fireEvent.change(within(dialog).getByLabelText('Reason'), { target: { value: 'Access restored' } });
    fireEvent.click(within(dialog).getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.click(within(dialog).getByRole('button', { name: 'Enable user' }));

    expect((await within(dialog).findByRole('alert')).textContent).toContain('Sign-in could not be restored');
    expect((within(dialog).getByLabelText('Reason') as HTMLTextAreaElement).value).toBe('Access restored');
  });

  it('resets a user password and displays the temporary credential once', async () => {
    apiMock.mockResolvedValue(new Response(JSON.stringify({
      status: 'succeeded',
      requiredCapability: 'users.reset_password',
      replayed: false,
      auditWarning: 'Password reset completed, but its audit record could not be written.',
      temporaryCredentialNotice: { temporaryPassword: 'Temp-Password-12345!', forceChangePasswordNextSignIn: true },
    }), { status: 200 }));

    render(<UserDetailPage userId="user-1" loadUserDetail={async () => ({ ...detail, user: { ...detail.user, isReadOnly: false, sourceOfAuthorityReason: null } })} capabilities={[{ capability: 'users.reset_password', state: 'allowed', reasonCode: 'active_role' }]} />);
    fireEvent.click(await screen.findByRole('button', { name: 'Reset password' }));
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: '  Password compromised  ' } });
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Reset password' }));

    await waitFor(() => expect(apiMock).toHaveBeenCalledWith('/api/users/user-1/reset-password', expect.objectContaining({ method: 'POST' })));
    expect(await screen.findByText('Temp-Password-12345!')).toBeTruthy();
    expect(screen.getByText('The user must change this password at next sign-in.')).toBeTruthy();
    expect((await screen.findByRole('alert')).textContent).toContain('Password reset completed, but its audit record could not be written.');
    expect(JSON.parse(apiMock.mock.calls[0][1].body)).toEqual({ reason: 'Password compromised' });
  });

  it('keeps a TAP audit warning visible after closing the one-time secret view', async () => {
    apiMock.mockImplementation(async (path: string) => {
      if (path.includes('/temporary-access-pass')) {
        return new Response(JSON.stringify({ status: 'succeeded', temporaryAccessPass: 'fixture-tap-value', auditWarning: 'Pass issued, but its audit record could not be written.' }), { status: 200 });
      }
      if (path.includes('/authentication-methods')) {
        return new Response(JSON.stringify({ userObjectId: 'user-1', items: [], fetchedAt: '2026-09-23T08:00:00Z', freshness: 'live', partialData: false, access: { state: 'allowed' } }), { status: 200 });
      }
      return new Response('{}', { status: 200 });
    });
    render(<UserDetailPage userId="user-1" loadUserDetail={async () => detail} capabilities={[
      { capability: 'authentication.methods.view', state: 'allowed', reasonCode: 'active_role' },
      { capability: 'authentication.methods.manage', state: 'allowed', reasonCode: 'active_role' },
    ]} />);

    fireEvent.click(await screen.findByRole('button', { name: 'Grant Temporary Access Pass' }));
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: 'New device access' } });
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.click(screen.getByRole('button', { name: 'Issue Temporary Access Pass' }));

    expect(await screen.findByText('fixture-tap-value')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Close' }));
    expect(screen.queryByText('fixture-tap-value')).toBeNull();
    expect((await screen.findAllByRole('alert')).some((alert) => alert.textContent?.includes('Pass issued, but its audit record could not be written.'))).toBe(true);
  });

  it('does not render group or license mutation controls for read-only sections', async () => {
    render(<UserDetailPage userId="user-1" loadUserDetail={async () => detail} capabilities={[]} />);

    expect(await screen.findByRole('heading', { name: 'Ada Lovelace' })).toBeTruthy();
    expect(screen.queryByRole('button', { name: /add group/i })).toBeNull();
    expect(screen.queryByRole('button', { name: /assign license/i })).toBeNull();
  });

  it('keeps unavailable authentication management visible and non-dispatching for a Global Reader capability snapshot', async () => {
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
    expect((screen.getByRole('button', { name: 'Grant Temporary Access Pass' }) as HTMLButtonElement).disabled).toBe(true);
    expect(screen.queryByRole('button', { name: 'Revoke sessions' })).toBeNull();
    expect(apiMock.mock.calls.every(([, init]) => init?.method !== 'POST' && init?.method !== 'DELETE')).toBe(true);
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
      : new Response(JSON.stringify({ status: 'succeeded', requiredCapability: 'groups.manage_members', replayed: false, auditWarning: 'Group changed, but the audit record could not be written.' }), { status: 200 }));

    render(<UserDetailPage userId="user-1" loadUserDetail={loadUserDetail} capabilities={[{ capability: 'groups.manage_members', state: 'allowed', reasonCode: 'active_role' }, { capability: 'licenses.assign', state: 'allowed', reasonCode: 'active_role' }]} />);
    fireEvent.click(await screen.findByRole('button', { name: /add group/i }));
    fireEvent.change(await screen.findByRole('combobox', { name: /group/i }), { target: { value: 'group-2' } });
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: 'Project membership' } });
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.click(screen.getByRole('button', { name: 'Add to group' }));

    await waitFor(() => expect(apiMock).toHaveBeenCalledWith('/api/users/user-1/groups/group-2', expect.objectContaining({ method: 'POST' })));
    await waitFor(() => expect(loadUserDetail).toHaveBeenCalledTimes(2));
    expect((await screen.findByRole('alert')).textContent).toContain('Group changed, but the audit record could not be written.');
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
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: 'Project membership' } });
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.click(screen.getByRole('button', { name: 'Add to group' }));
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
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: 'Correcting group membership' } });
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.click(screen.getByRole('button', { name: 'Add to group' }));

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
      : new Response(JSON.stringify({ status: 'succeeded', requiredCapability: 'licenses.assign', replayed: false, auditWarning: 'License changed, but the audit record could not be written.' }), { status: 200 }));

    render(<UserDetailPage userId="user-1" loadUserDetail={loadUserDetail} capabilities={[{ capability: 'licenses.assign', state: 'allowed', reasonCode: 'active_role' }]} />);
    fireEvent.click(await screen.findByRole('button', { name: /assign license/i }));
    expect(await screen.findByRole('option', { name: 'Microsoft 365 E5' })).toBeTruthy();
    fireEvent.change(screen.getByRole('combobox', { name: 'License' }), { target: { value: 'sku-2' } });
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: 'Role requires E5' } });
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Assign license' }));
    await waitFor(() => expect(apiMock).toHaveBeenCalledWith('/api/users/user-1/licenses/sku-2', expect.objectContaining({ method: 'POST' })));
    await waitFor(() => expect(loadUserDetail).toHaveBeenCalledTimes(2));
    expect((await screen.findByRole('alert')).textContent).toContain('License changed, but the audit record could not be written.');
  });

  it('opens group selection when the user has no existing groups', async () => {
    const emptyDetail = { ...detail, groups: { ...detail.groups, items: [] } };
    apiMock.mockResolvedValue(new Response(JSON.stringify({ items: [{ id: 'group-2', displayName: 'Engineering' }], access: { state: 'allowed' } }), { status: 200 }));
    render(<UserDetailPage userId="user-1" loadUserDetail={async () => emptyDetail} capabilities={[{ capability: 'groups.manage_members', state: 'allowed', reasonCode: 'active_role' }]} />);

    fireEvent.click(await screen.findByRole('button', { name: /add group/i }));
    expect(await screen.findByRole('option', { name: 'Engineering' })).toBeTruthy();
  });
});
