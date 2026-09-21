import { fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import { describe, expect, it, vi } from 'vitest';
import { InvitationRedemptionPage } from '../../../../src/Web/src/features/invitations/InvitationRedemptionPage';

vi.mock('../../../../src/Web/src/auth/AuthProvider', () => ({ useAuth: () => ({ account: { username: 'customer@example.com' }, getApiToken: vi.fn() }) }));

describe('InvitationRedemptionPage', () => {
  it('redeems without rendering the nonce and navigates to overview', async () => {
    const redeem = vi.fn().mockResolvedValue({ status: 'consent_required', workspaceId: 'w-1', workspaceName: 'Demo', nextStep: '/overview' });
    render(<InvitationRedemptionPage nonce="secret-nonce" redeem={redeem} />);
    expect(screen.getByText('customer@example.com')).toBeTruthy();
    expect(screen.queryByText('secret-nonce')).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: 'Redeem invitation' }));
    expect(await screen.findByText('Demo')).toBeTruthy();
    expect(redeem).toHaveBeenCalledWith('secret-nonce');
  });
});
