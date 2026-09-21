export type InvitationRedemption = { status: string; workspaceId: string; workspaceName: string; nextStep: string };

export async function redeemInvitation(nonce: string, getApiToken: () => Promise<string>): Promise<InvitationRedemption> {
  const token = await getApiToken();
  const response = await fetch(`/api/invitations/${encodeURIComponent(nonce)}/redeem`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${token}` }
  });
  if (!response.ok) throw new Error(response.status === 400 ? 'invitation_invalid_or_expired' : 'invitation_redemption_failed');
  const value = await response.json() as Partial<InvitationRedemption>;
  if (typeof value.status !== 'string' || typeof value.workspaceId !== 'string' || typeof value.workspaceName !== 'string' || typeof value.nextStep !== 'string') throw new Error('invitation_redemption_failed');
  return value as InvitationRedemption;
}
