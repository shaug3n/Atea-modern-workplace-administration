import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { CapabilityDecision } from '../../../../src/Web/src/capabilities/capabilityTypes';
import { UsersPage } from '../../../../src/Web/src/features/users/UsersPage';
import type { UserFiltersState, UsersDirectoryResponse } from '../../../../src/Web/src/features/users/usersApi';

const apiMock = vi.hoisted(() => vi.fn());
const issueReporter = vi.hoisted(() => ({ report: vi.fn(), clear: vi.fn() }));
vi.mock('../../../../src/Web/src/notifications/WorkspaceNotifications', () => ({ useWorkspaceIssueReporter: () => issueReporter }));

vi.mock('../../../../src/Web/src/auth/useApi', () => ({
  useApi: () => apiMock,
}));

const usersResponse: UsersDirectoryResponse = {
  items: [
    { id: 'user-1', displayName: 'Ada Lovelace', userPrincipalName: 'ada@example.com', mail: 'ada@example.com', accountEnabled: true, userType: 'Member' },
    { id: 'user-2', displayName: 'Grace Hopper', userPrincipalName: 'grace@example.com', mail: 'grace@example.com', accountEnabled: true, userType: 'Member' },
  ],
  continuationToken: null,
  fetchedAt: '2026-09-21T08:00:00Z',
  freshness: 'fresh',
  partialData: false,
};

function decision(capability: CapabilityDecision['capability'], state: CapabilityDecision['state']): CapabilityDecision {
  return { capability, state, reasonCode: state === 'allowed' ? 'active_role' : 'role_read_only' };
}

const allowedCapabilities: CapabilityDecision[] = [
  decision('users.view', 'allowed'),
  decision('users.create', 'hidden'),
  decision('users.update', 'allowed'),
  decision('users.disable', 'allowed'),
];

async function openDisable(name: string) {
  fireEvent.click(await screen.findByRole('button', { name: `Actions for ${name}` }));
  fireEvent.click(screen.getByRole('menuitem', { name: 'Disable user' }));
}

describe('UsersPage', () => {
  afterEach(() => {
    cleanup();
    apiMock.mockReset();
    issueReporter.report.mockClear(); issueReporter.clear.mockClear();
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
    window.history.replaceState(null, '', '/users');
  });

  it('keeps title and filters on a failed read, then clears its service issue on retry', async () => {
    const loader = vi.fn().mockRejectedValueOnce(new Error('secret graph details')).mockResolvedValue(usersResponse);
    render(<UsersPage capabilities={allowedCapabilities} loadUsers={loader} />);
    await screen.findByRole('alert');
    expect(screen.getByRole('heading', { name: 'Users' })).toBeTruthy();
    expect(screen.getByRole('searchbox', { name: 'Search users' })).toBeTruthy();
    expect(issueReporter.report).toHaveBeenCalledWith(expect.objectContaining({ key: 'users:read', kind: 'service' }));
    expect(JSON.stringify(issueReporter.report.mock.calls)).not.toContain('secret graph details');
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    await screen.findByText('Ada Lovelace');
    expect(issueReporter.clear).toHaveBeenCalledWith('users:read');
  });

  it('labels verified partial user results while keeping records visible', async () => {
    render(<UsersPage capabilities={allowedCapabilities} loadUsers={async () => ({
      ...usersResponse,
      partialData: false,
      error: { category: 'temporarily_unavailable', message: 'raw Graph diagnostic' },
    })} />);
    expect(await screen.findByRole('table')).toBeTruthy();
    expect(screen.getByText('Partly loaded')).toBeTruthy();
    expect(screen.getByText(/verified/).textContent).toContain('Partial results');
    expect(screen.getByRole('table').textContent).toContain('Ada Lovelace');
    expect(screen.queryByText('raw Graph diagnostic')).toBeNull();
    expect(issueReporter.report).toHaveBeenCalledWith(expect.objectContaining({ key: 'users:read', kind: 'service' }));
  });

  it('removes old row actions as soon as filters change and shows only the new response', async () => {
    let resolveNext!: (value: UsersDirectoryResponse) => void;
    const next = new Promise<UsersDirectoryResponse>(resolve => { resolveNext = resolve; });
    const loader = vi.fn().mockResolvedValueOnce(usersResponse).mockImplementationOnce(() => next);
    render(<UsersPage capabilities={allowedCapabilities} loadUsers={loader} />);
    await screen.findByRole('link', { name: 'Ada Lovelace' });
    fireEvent.change(screen.getByRole('searchbox', { name: 'Search users' }), { target: { value: 'Grace' } });
    expect(screen.queryByRole('button', { name: 'Actions for Ada Lovelace' })).toBeNull();
    expect(screen.queryByRole('link', { name: 'Ada Lovelace' })).toBeNull();
    expect(screen.getByText('Loading users…')).toBeTruthy();
    await waitFor(() => expect(loader).toHaveBeenCalledTimes(2));
    resolveNext({ ...usersResponse, items: [usersResponse.items[1]] });
    expect(await screen.findByRole('link', { name: 'Grace Hopper' })).toBeTruthy();
    expect(screen.queryByText('Ada Lovelace')).toBeNull();
  });

  it('removes old row actions while refresh is in flight', async () => {
    let resolveNext!: (value: UsersDirectoryResponse) => void;
    const next = new Promise<UsersDirectoryResponse>(resolve => { resolveNext = resolve; });
    const loader = vi.fn().mockResolvedValueOnce(usersResponse).mockImplementationOnce(() => next);
    render(<UsersPage capabilities={allowedCapabilities} loadUsers={loader} />);
    await screen.findByRole('button', { name: 'Actions for Ada Lovelace' });
    fireEvent.click(screen.getByRole('button', { name: 'Refresh' }));
    expect(screen.queryByRole('button', { name: 'Actions for Ada Lovelace' })).toBeNull();
    expect(screen.getByText('Loading users…')).toBeTruthy();
    resolveNext(usersResponse);
    expect(await screen.findByRole('button', { name: 'Actions for Ada Lovelace' })).toBeTruthy();
  });

  it('closes a pending disable review when filters change', async () => {
    render(<UsersPage capabilities={allowedCapabilities} loadUsers={async () => usersResponse} />);
    await openDisable('Ada Lovelace');
    expect(screen.getByRole('dialog', { name: 'Disable user' })).toBeTruthy();
    fireEvent.change(screen.getByRole('searchbox', { name: 'Search users' }), { target: { value: 'Grace' } });
    expect(screen.queryByRole('dialog', { name: 'Disable user' })).toBeNull();
    expect(apiMock).not.toHaveBeenCalled();
  });

  it('loads supported filters from the URL and writes edits back without unsupported role filters', async () => {
    window.history.replaceState(null, '', '/users?search=Ada&accountStatus=enabled&userType=Member&license=E3&tenantRole=Global');
    const loadUsers = vi.fn(async () => usersResponse);
    const pushState = vi.spyOn(window.history, 'pushState');
    const replaceState = vi.spyOn(window.history, 'replaceState');
    render(<UsersPage capabilities={allowedCapabilities} loadUsers={loadUsers} />);

    await waitFor(() => expect(loadUsers).toHaveBeenCalledWith(expect.objectContaining({ search: 'Ada', accountStatus: 'enabled', userType: 'Member', license: 'E3', tenantRole: '' }), null));
    expect(window.location.search).not.toContain('tenantRole');
    pushState.mockClear();
    replaceState.mockClear();
    fireEvent.change(screen.getByRole('searchbox', { name: 'Search users' }), { target: { value: 'Grace' } });
    await waitFor(() => expect(new URLSearchParams(window.location.search).get('search')).toBe('Grace'));
    expect(replaceState).toHaveBeenCalled();
    expect(pushState).not.toHaveBeenCalled();
  });

  it('pushes a shortcut view, preserves compatible filters, and resets paging immediately', async () => {
    window.history.replaceState(null, '', '/users?search=Ada&license=E3');
    const loadUsers = vi.fn()
      .mockResolvedValueOnce({ ...usersResponse, continuationToken: 'next-token' })
      .mockResolvedValue(usersResponse);
    const pushState = vi.spyOn(window.history, 'pushState');
    render(<UsersPage capabilities={allowedCapabilities} loadUsers={loadUsers} />);

    fireEvent.click(await screen.findByRole('button', { name: 'Next page' }));
    await waitFor(() => expect(loadUsers).toHaveBeenCalledTimes(2));
    expect(screen.getByText('Page 2')).toBeTruthy();

    fireEvent.click(screen.getByRole('button', { name: /Enabled/ }));

    await waitFor(() => expect(loadUsers).toHaveBeenCalledTimes(3));
    expect(loadUsers).toHaveBeenLastCalledWith({
      search: 'Ada',
      accountStatus: 'enabled',
      tenantRole: '',
      license: 'E3',
      userType: '',
    }, null);
    expect(screen.getByText('Page 1')).toBeTruthy();
    expect(new URLSearchParams(window.location.search).get('search')).toBe('Ada');
    expect(new URLSearchParams(window.location.search).get('license')).toBe('E3');
    expect(new URLSearchParams(window.location.search).get('accountStatus')).toBe('enabled');
    expect(pushState).toHaveBeenCalled();
    expect(screen.queryByText(/total users/i)).toBeNull();
  });

  it('restores a prior shortcut from popstate and loads its filters from page one', async () => {
    window.history.replaceState(null, '', '/users?search=Ada&license=E3&accountStatus=disabled');
    const loadUsers = vi.fn(async () => usersResponse);
    render(<UsersPage capabilities={allowedCapabilities} loadUsers={loadUsers} />);
    fireEvent.click(await screen.findByRole('button', { name: /Guests/ }));
    await waitFor(() => expect(loadUsers).toHaveBeenCalledTimes(2));
    expect(screen.getByRole('button', { name: /Guests/ }).getAttribute('aria-pressed')).toBe('true');

    window.history.replaceState(null, '', '/users?search=Ada&license=E3&accountStatus=disabled&tenantRole=Global');
    window.dispatchEvent(new PopStateEvent('popstate'));

    await waitFor(() => expect(loadUsers).toHaveBeenCalledTimes(3));
    expect(loadUsers).toHaveBeenLastCalledWith({
      search: 'Ada',
      accountStatus: 'disabled',
      tenantRole: '',
      license: 'E3',
      userType: '',
    }, null);
    expect(screen.getByRole('button', { name: /Guests/ }).getAttribute('aria-pressed')).toBe('false');
    expect(screen.getByRole('button', { name: /Disabled/ }).getAttribute('aria-pressed')).toBe('true');
    expect(window.location.search).not.toContain('tenantRole');

    window.history.replaceState(null, '', '/users?search=Ada&license=E3&userType=Guest');
    window.dispatchEvent(new PopStateEvent('popstate'));
    await waitFor(() => expect(loadUsers).toHaveBeenCalledTimes(4));
    expect(loadUsers).toHaveBeenLastCalledWith({
      search: 'Ada',
      accountStatus: '',
      tenantRole: '',
      license: 'E3',
      userType: 'Guest',
    }, null);
    expect(screen.getByRole('button', { name: /Guests/ }).getAttribute('aria-pressed')).toBe('true');
  });

  it('keeps optional filters open and focused when the last license character is cleared', async () => {
    window.history.replaceState(null, '', '/users?license=E');
    render(<UsersPage capabilities={allowedCapabilities} loadUsers={async () => usersResponse} />);
    const license = screen.getByRole('textbox', { name: 'License' }) as HTMLInputElement;
    license.focus();
    fireEvent.change(license, { target: { value: '' } });
    expect((screen.getByText('More filters').closest('details') as HTMLDetailsElement).open).toBe(true);
    expect(document.activeElement).toBe(license);
  });

  it('exports active user filters and makes truncation visible', async () => {
    apiMock.mockResolvedValue(new Response('"Id"\n"user-1"\n', { status: 200, headers: { 'X-Export-Row-Count': '10000', 'X-Export-Max-Rows': '10000', 'X-Export-Truncated': 'true', 'Content-Type': 'text/csv' } }));
    vi.stubGlobal('URL', Object.assign(URL, { createObjectURL: vi.fn(() => 'blob:test'), revokeObjectURL: vi.fn() }));
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});
    render(<UsersPage capabilities={allowedCapabilities} loadUsers={async () => usersResponse} />);
    fireEvent.change(screen.getByRole('searchbox', { name: 'Search users' }), { target: { value: 'Grace' } });
    const button = screen.getByRole('button', { name: /export filtered csv/i }) as HTMLButtonElement;
    expect(button.disabled).toBe(false);
    fireEvent.click(button);
    await waitFor(() => expect(apiMock).toHaveBeenCalledWith(expect.stringContaining('/api/users/export.csv?search=Grace'), expect.anything()));
    expect(await screen.findByText(/10,000.*maximum.*truncated/i)).toBeTruthy();
  });

  it('announces the page number and disables paging while loading', async () => {
    const first = { ...usersResponse, continuationToken: 'next' };
    let resolveSecond!: (value: UsersDirectoryResponse) => void;
    const loadUsers = vi.fn().mockResolvedValueOnce(first).mockImplementationOnce(() => new Promise<UsersDirectoryResponse>((resolve) => { resolveSecond = resolve; }));
    render(<UsersPage capabilities={allowedCapabilities} loadUsers={loadUsers} />);
    fireEvent.click(await screen.findByRole('button', { name: 'Next page' }));
    expect(screen.getByText('Page 2')).toBeTruthy();
    expect((screen.getByRole('button', { name: 'Next page' }) as HTMLButtonElement).disabled).toBe(true);
    resolveSecond(usersResponse);
  });

  it('shows a response error as an access failure instead of an empty result', async () => {
    render(<UsersPage capabilities={allowedCapabilities} loadUsers={async () => ({ ...usersResponse, items: [], error: { category: 'not_authorized', message: 'Directory access was denied.' } })} />);
    expect(await screen.findByRole('alert')).toBeTruthy();
    expect(screen.getByText('User data is unavailable. Check Notifications for details.')).toBeTruthy();
    expect(screen.queryByText('Directory access was denied.')).toBeNull();
    expect(screen.queryByText('No users match the current filters.')).toBeNull();
  });

  it('guides PIM-gated directory access without fetching users', async () => {
    const loadUsers = vi.fn(async () => usersResponse);
    render(<UsersPage capabilities={[{ capability: 'users.view', state: 'pim_activation_required', reasonCode: 'pim_activation_required', nextStep: { label: 'Activate the required Entra role', href: '/identity' } }]} loadUsers={loadUsers} />);
    expect(await screen.findByText(/Activate the required Entra role in PIM/)).toBeTruthy();
    expect(loadUsers).not.toHaveBeenCalled();
  });

  it('opens the create form when the create capability is allowed', async () => {
    render(<UsersPage capabilities={[decision('users.view', 'allowed'), decision('users.create', 'allowed'), decision('users.disable', 'hidden')]} loadUsers={async () => usersResponse} />);

    fireEvent.click(await screen.findByRole('button', { name: 'Create user' }));
    expect(screen.getByRole('dialog', { name: 'Create user' })).toBeTruthy();
    expect(screen.getByLabelText('Name')).toBeTruthy();
  });

  it('opens disable confirmation and mutates only after review and destructive phrase', async () => {
    const loadUsers = vi.fn(async (_filters: UserFiltersState, _continuationToken: string | null) => usersResponse);
    apiMock.mockResolvedValue(new Response(JSON.stringify({
      status: 'succeeded',
      requiredCapability: 'users.disable',
      replayed: false,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

    render(<UsersPage capabilities={allowedCapabilities} loadUsers={loadUsers} />);

    await openDisable('Ada Lovelace');
    expect(screen.getByRole('dialog', { name: 'Disable user' })).toBeTruthy();
    expect(screen.getByText('Disable sign-in for this user.')).toBeTruthy();

    const confirm = screen.getByRole('button', { name: 'Disable user' }) as HTMLButtonElement;
    expect(confirm.disabled).toBe(true);
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    expect(confirm.disabled).toBe(true);
    fireEvent.change(screen.getByLabelText('Type DISABLE to confirm'), { target: { value: 'DISABLE' } });
    expect(confirm.disabled).toBe(false);
    expect(apiMock).not.toHaveBeenCalled();

    fireEvent.click(confirm);

    await waitFor(() => expect(apiMock).toHaveBeenCalledTimes(1));
    const [path, init] = apiMock.mock.calls[0];
    expect(path).toBe('/api/users/user-1/disable');
    expect(path).not.toMatch(/graph\.microsoft\.com/i);
    expect(init.method).toBe('POST');
    expect(init.headers['Idempotency-Key']).toBeTruthy();
    expect(await screen.findByText('Ada Lovelace sign-in was disabled.')).toBeTruthy();
    await waitFor(() => expect(loadUsers).toHaveBeenCalledTimes(2));
  });

  it('keeps disable inert for read-only capability decisions', async () => {
    render(
      <UsersPage
        capabilities={[
          decision('users.view', 'allowed'),
          decision('users.create', 'hidden'),
          decision('users.update', 'read_only'),
          decision('users.disable', 'read_only'),
        ]}
        loadUsers={async () => usersResponse}
      />,
    );

    fireEvent.click(await screen.findByRole('button', { name: 'Actions for Ada Lovelace' }));
    const disableItem = screen.getByRole('menuitem', { name: 'Disable user' });
    expect(disableItem.getAttribute('aria-disabled')).toBe('true');
    expect(disableItem.textContent).toContain('Requires permission: Disable users');
    fireEvent.click(disableItem);

    expect(screen.queryByRole('dialog', { name: 'Disable user' })).toBeNull();
    expect(apiMock).not.toHaveBeenCalled();
  });

  it('resets disable confirmation when the target user changes', async () => {
    render(<UsersPage capabilities={allowedCapabilities} loadUsers={async () => usersResponse} />);

    await openDisable('Ada Lovelace');
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.change(screen.getByLabelText('Type DISABLE to confirm'), { target: { value: 'DISABLE' } });
    expect((screen.getByRole('button', { name: 'Disable user' }) as HTMLButtonElement).disabled).toBe(false);

    await openDisable('Grace Hopper');

    expect(within(screen.getByRole('dialog', { name: 'Disable user' })).getByText('Grace Hopper')).toBeTruthy();
    expect((screen.getByLabelText('I reviewed the target, change and required capability.') as HTMLInputElement).checked).toBe(false);
    expect((screen.getByLabelText('Type DISABLE to confirm') as HTMLInputElement).value).toBe('');
    expect((screen.getByRole('button', { name: 'Disable user' }) as HTMLButtonElement).disabled).toBe(true);
  });

  it('closes disable confirmation with permission guidance when disable capability is revoked before submit', async () => {
    const loadUsers = vi.fn(async (_filters: UserFiltersState, _continuationToken: string | null) => usersResponse);
    const { rerender } = render(<UsersPage capabilities={allowedCapabilities} loadUsers={loadUsers} />);

    await openDisable('Ada Lovelace');
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.change(screen.getByLabelText('Type DISABLE to confirm'), { target: { value: 'DISABLE' } });

    rerender(<UsersPage capabilities={[
      decision('users.view', 'allowed'),
      decision('users.create', 'hidden'),
      decision('users.update', 'allowed'),
      decision('users.disable', 'read_only'),
    ]} loadUsers={loadUsers} />);
    fireEvent.click(screen.getByRole('button', { name: 'Disable user' }));

    expect(screen.queryByRole('dialog', { name: 'Disable user' })).toBeNull();
    expect(screen.getByRole('alert').textContent).toContain('cannot disable this user');
    expect(apiMock).not.toHaveBeenCalled();
  });

  it('keeps the disable dialog open with friendly guidance when mutation fails', async () => {
    apiMock.mockResolvedValue(new Response(JSON.stringify({
      status: 'idempotency_key_reused',
      requiredCapability: 'users.disable',
      replayed: false,
      error: 'idempotency_key_reused',
    }), { status: 409, headers: { 'Content-Type': 'application/json' } }));

    render(<UsersPage capabilities={allowedCapabilities} loadUsers={async () => usersResponse} />);

    await openDisable('Ada Lovelace');
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.change(screen.getByLabelText('Type DISABLE to confirm'), { target: { value: 'DISABLE' } });
    fireEvent.click(screen.getByRole('button', { name: 'Disable user' }));

    expect((await screen.findByRole('alert')).textContent).toContain('idempotency key');
    expect(screen.getByRole('dialog', { name: 'Disable user' })).toBeTruthy();
  });
});
