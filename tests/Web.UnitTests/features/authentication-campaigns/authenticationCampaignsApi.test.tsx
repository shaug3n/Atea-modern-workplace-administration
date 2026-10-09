import { describe, expect, it, vi } from 'vitest';
import { fetchAuthenticationCampaigns, AuthenticationCampaignsApiError } from '../../../../src/Web/src/features/authentication-campaigns/authenticationCampaignsApi';
import type { AuthenticationCampaignsResponse } from '../../../../src/Web/src/features/authentication-campaigns/authenticationCampaignsApi';

const response: AuthenticationCampaignsResponse = {
  items: [{
    id: 'account-1',
    displayName: 'Ada Lovelace',
    userPrincipalName: 'ada@example.test',
    userType: 'Member',
    methodsRegistered: ['passKeyDeviceBound', 'fido2', 'futureMethod'],
    isMfaRegistered: true,
    isMfaCapable: true,
    isPasswordlessCapable: true,
    isSystemPreferredAuthenticationMethodEnabled: true,
    systemPreferredAuthenticationMethods: ['sms'],
    userPreferredMethodForSecondaryAuthentication: 'push',
    lastUpdatedDateTime: '2026-10-08T10:00:00Z',
    passkeyRegistrationState: 'registered',
    isGenericFido2Registered: true,
    phoneRegistrationState: 'registered',
    phonePreferenceState: 'phone',
    department: 'Engineering',
    officeLocation: 'Oslo',
    companyName: 'Atea',
    directoryJoinState: 'matched',
  }],
  fetchedAt: '2026-10-08T10:01:00Z',
  sourceLastUpdatedFrom: '2026-10-08T09:00:00Z',
  sourceLastUpdatedTo: '2026-10-08T10:00:00Z',
  partialData: true,
  observedRecordCount: 2,
  duplicateRecordCount: 1,
  directoryEnrichmentState: 'partial',
  enrichedAccountCount: 1,
  reportErrorCategory: null,
  directoryErrorCategory: 'unavailable',
};

describe('fetchAuthenticationCampaigns', () => {
  it('uses only the same-origin authorized report route and preserves complete and partial response fields', async () => {
    const api = vi.fn().mockResolvedValue(new Response(JSON.stringify(response), { status: 200 }));

    const result = await fetchAuthenticationCampaigns(api);

    expect(api).toHaveBeenCalledTimes(1);
    expect(api).toHaveBeenCalledWith('/api/authentication-campaigns/registrations', { method: 'GET' });
    expect(api.mock.calls.flatMap(([url]) => [url]).every(url => !String(url).includes('graph.microsoft.com'))).toBe(true);
    expect(result).toEqual(response);
    expect(result.items[0].methodsRegistered).toContain('futureMethod');
    expect(result.partialData).toBe(true);
    expect(result.observedRecordCount).toBe(2);
    expect(result.enrichedAccountCount).toBe(1);
  });

  it('preserves a complete response without manufacturing partial status or tenant totals', async () => {
    const complete = {
      ...response,
      partialData: false,
      observedRecordCount: 1,
      duplicateRecordCount: 0,
      directoryEnrichmentState: 'complete',
      enrichedAccountCount: 1,
      reportErrorCategory: null,
      directoryErrorCategory: null,
    };
    const api = vi.fn().mockResolvedValue(new Response(JSON.stringify(complete), { status: 200 }));

    const result = await fetchAuthenticationCampaigns(api);

    expect(result).toEqual(complete);
    expect(result.partialData).toBe(false);
    expect(result.observedRecordCount).toBe(1);
    expect('tenantTotal' in result).toBe(false);
  });

  it.each([
    [403, { error: { category: 'forbidden' } }, 'forbidden'],
    [403, { error: { category: 'consent_required' } }, 'consent_required'],
    [503, { error: { category: 'unavailable' } }, 'unavailable'],
    [503, { error: { message: 'do not expose provider detail' } }, 'unavailable'],
  ])('throws a safe typed error for status %s without exposing response details', async (status, body, category) => {
    const api = vi.fn().mockResolvedValue(new Response(JSON.stringify(body), { status }));

    const error = await fetchAuthenticationCampaigns(api).catch(reason => reason);

    expect(error).toBeInstanceOf(AuthenticationCampaignsApiError);
    expect(error).toMatchObject({ status, category });
    expect(error.message).not.toContain('provider detail');
    expect(api).toHaveBeenCalledWith('/api/authentication-campaigns/registrations', { method: 'GET' });
  });

  it('treats an unreadable error body as a safe source failure', async () => {
    const api = vi.fn().mockResolvedValue(new Response('not-json', { status: 502 }));

    await expect(fetchAuthenticationCampaigns(api)).rejects.toMatchObject({
      status: 502,
      category: 'unavailable',
    });
  });
});
