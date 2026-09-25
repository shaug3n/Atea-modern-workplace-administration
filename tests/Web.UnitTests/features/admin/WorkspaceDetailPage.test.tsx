import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { WorkspaceDetailPage } from '../../../../src/Web/src/features/admin/WorkspaceDetailPage';

const api = vi.hoisted(() => ({ getWorkspace: vi.fn(), addMembership: vi.fn(), createInvitation: vi.fn(), reissueInvitation: vi.fn(), revokeInvitation: vi.fn() }));
vi.mock('../../../../src/Web/src/features/admin/adminApi', () => ({ adminApi: api }));
afterEach(() => { cleanup(); vi.clearAllMocks(); });

const detail = { id: 'w-1', tenantId: '11111111-1111-1111-1111-111111111111', displayName: 'Demo', connectionStatus: 'awaiting_invitation', lastVerifiedAt: null, connectionFailureCategory: null, memberships: [{ id: 'm-1', tenantObjectId: '22222222-2222-2222-2222-222222222222', email: 'admin@example.com', platformRole: 'CustomerAdmin', isAteaOperator: false }], invitations: [{ id: 'i-1', email: 'admin@example.com', displayName: 'Admin', expiresAt: '2026-10-01T00:00:00Z', redeemedAt: null, nonceHash: 'must-not-render' }] };

describe('WorkspaceDetailPage', () => {
  it('does not expose manual object-ID membership creation in customer workspace recovery', async () => {
    api.getWorkspace.mockResolvedValue(detail);
    render(<WorkspaceDetailPage workspaceId="w-1" />);
    expect(await screen.findByRole('heading', { name: 'Demo' })).toBeTruthy();
    expect(screen.queryByLabelText('Entra object ID')).toBeNull();
    expect(screen.queryByRole('button', { name: 'Add membership' })).toBeNull();
    expect(screen.getByText(/first administrator is in the workspace/i)).toBeTruthy();
  });

  it('renders safe membership and invitation metadata without secret fields', async () => {
    api.getWorkspace.mockResolvedValue(detail);
    render(<WorkspaceDetailPage workspaceId="w-1" />);
    expect(await screen.findByRole('heading', { name: 'Demo' })).toBeTruthy();
    expect(screen.getAllByText(/admin@example.com/).length).toBeGreaterThanOrEqual(2);
    expect(screen.getByText('Pending')).toBeTruthy();
    expect(screen.queryByText('must-not-render')).toBeNull();
  });

  it('displays the first-admin one-time invitation and supports copy', async () => {
    api.getWorkspace.mockResolvedValue({ ...detail, invitations: [] });
    Object.assign(navigator, { clipboard: { writeText: vi.fn() } });
    const invitation = { workspace: { id: 'w-1', tenantId: detail.tenantId, displayName: detail.displayName, connectionStatus: detail.connectionStatus }, invitationUrl: 'https://example.test/redeem/secret', expiresAt: '2026-10-01T00:00:00Z' };
    render(<WorkspaceDetailPage workspaceId="w-1" firstAdminInvitation={invitation} />);
    expect(await screen.findByText('https://example.test/redeem/secret')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Copy invitation link' }));
    expect(navigator.clipboard.writeText).toHaveBeenCalledWith('https://example.test/redeem/secret');
  });
});
