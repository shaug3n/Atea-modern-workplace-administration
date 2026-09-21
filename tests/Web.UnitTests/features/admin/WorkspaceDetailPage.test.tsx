import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { WorkspaceDetailPage } from '../../../../src/Web/src/features/admin/WorkspaceDetailPage';

const api = vi.hoisted(() => ({ getWorkspace: vi.fn(), addMembership: vi.fn(), createInvitation: vi.fn() }));
vi.mock('../../../../src/Web/src/features/admin/adminApi', () => ({ adminApi: api }));
afterEach(() => { cleanup(); vi.clearAllMocks(); });

const detail = { id: 'w-1', tenantId: '11111111-1111-1111-1111-111111111111', displayName: 'Demo', connectionStatus: 'awaiting_invitation', lastVerifiedAt: null, connectionFailureCategory: null, memberships: [{ id: 'm-1', tenantObjectId: '22222222-2222-2222-2222-222222222222', email: 'admin@example.com', platformRole: 'CustomerAdmin', isAteaOperator: false }], invitations: [{ id: 'i-1', email: 'admin@example.com', displayName: 'Admin', expiresAt: '2026-10-01T00:00:00Z', redeemedAt: null, nonceHash: 'must-not-render' }] };

describe('WorkspaceDetailPage', () => {
  it('validates and creates a membership, then refreshes workspace details', async () => {
    api.getWorkspace.mockResolvedValue(detail);
    api.addMembership.mockResolvedValue({ id: 'm-2', tenantObjectId: '33333333-3333-3333-3333-333333333333', email: 'operator@example.com', platformRole: 'PlatformOperator', isAteaOperator: true });
    render(<WorkspaceDetailPage workspaceId="w-1" />);
    fireEvent.click(await screen.findByRole('button', { name: 'Add membership' }));
    fireEvent.click(screen.getAllByRole('button', { name: 'Add membership', exact: true })[1]);
    expect(screen.getByRole('alert').textContent).toContain('Enter a valid Entra object ID.');
    fireEvent.change(screen.getByLabelText('Entra object ID'), { target: { value: '33333333-3333-3333-3333-333333333333' } });
    fireEvent.change(screen.getByLabelText('Membership email'), { target: { value: 'operator@example.com' } });
    fireEvent.change(screen.getByLabelText('Platform role'), { target: { value: 'PlatformOperator' } });
    fireEvent.click(screen.getByLabelText('Atea operator'));
    fireEvent.click(screen.getAllByRole('button', { name: 'Add membership', exact: true })[1]);
    expect(await screen.findByText(/Membership added/)).toBeTruthy();
    expect(api.addMembership).toHaveBeenCalledWith('w-1', { tenantObjectId: '33333333-3333-3333-3333-333333333333', email: 'operator@example.com', platformRole: 'PlatformOperator', isAteaOperator: true });
    expect(api.getWorkspace).toHaveBeenCalledTimes(2);
  });

  it('renders safe membership and invitation metadata without secret fields', async () => {
    api.getWorkspace.mockResolvedValue(detail);
    render(<WorkspaceDetailPage workspaceId="w-1" />);
    expect(await screen.findByRole('heading', { name: 'Demo' })).toBeTruthy();
    expect(screen.getAllByText(/admin@example.com/).length).toBe(2);
    expect(screen.getByText(/Invitation pending/)).toBeTruthy();
    expect(screen.queryByText('must-not-render')).toBeNull();
  });

  it('displays a one-time invitation URL only after creation and supports copy', async () => {
    api.getWorkspace.mockResolvedValue({ ...detail, invitations: [] });
    api.createInvitation.mockResolvedValue({ invitationUrl: 'https://example.test/redeem/secret', expiresAt: '2026-10-01T00:00:00Z' });
    Object.assign(navigator, { clipboard: { writeText: vi.fn() } });
    render(<WorkspaceDetailPage workspaceId="w-1" />);
    fireEvent.click(await screen.findByRole('button', { name: 'Create invitation' }));
    fireEvent.change(screen.getByLabelText('Invitation email'), { target: { value: 'new@example.com' } });
    fireEvent.change(screen.getByLabelText('Invitee display name'), { target: { value: 'New Admin' } });
    fireEvent.click(screen.getAllByRole('button', { name: 'Create invitation', exact: true })[1]);
    expect(await screen.findByText('https://example.test/redeem/secret')).toBeTruthy();
    expect(screen.getByText(/credential-like secret/i)).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Copy invitation URL' }));
    expect(navigator.clipboard.writeText).toHaveBeenCalledWith('https://example.test/redeem/secret');
  });
});
