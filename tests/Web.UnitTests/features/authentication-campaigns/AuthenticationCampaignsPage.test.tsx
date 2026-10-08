import React from 'react';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AuthenticationCampaignsPage } from '../../../../src/Web/src/features/authentication-campaigns/AuthenticationCampaignsPage';
import type { AuthenticationCampaignsRegistration, AuthenticationCampaignsResponse } from '../../../../src/Web/src/features/authentication-campaigns/authenticationCampaignsApi';
import type { AppSession } from '../../../../src/Web/src/components/TenantContextHeader';
import type { CapabilityDecision } from '../../../../src/Web/src/capabilities/capabilityTypes';

const allowed: CapabilityDecision[] = [{
  capability: 'authentication.campaigns.view',
  state: 'allowed',
  reasonCode: 'active_role',
}];

const baseRegistration: AuthenticationCampaignsRegistration = {
  id: 'member-1',
  displayName: 'Ada Lovelace',
  userPrincipalName: 'ada@example.test',
  userType: 'Member',
  methodsRegistered: ['passKeyDeviceBound'],
  isMfaRegistered: true,
  isMfaCapable: true,
  isPasswordlessCapable: true,
  isSystemPreferredAuthenticationMethodEnabled: null,
  systemPreferredAuthenticationMethods: null,
  userPreferredMethodForSecondaryAuthentication: null,
  lastUpdatedDateTime: '2026-10-08T10:00:00Z',
  passkeyRegistrationState: 'registered',
  isGenericFido2Registered: false,
  phoneRegistrationState: 'not_registered',
  phonePreferenceState: 'unknown',
  department: 'Engineering',
  officeLocation: 'Oslo',
  companyName: 'Atea',
  directoryJoinState: 'matched',
};

function account(overrides: Partial<AuthenticationCampaignsRegistration>): AuthenticationCampaignsRegistration {
  return { ...baseRegistration, ...overrides };
}

function makeResponse(items: AuthenticationCampaignsRegistration[], overrides: Partial<AuthenticationCampaignsResponse> = {}): AuthenticationCampaignsResponse {
  return {
    items,
    fetchedAt: '2026-10-08T10:01:00Z',
    sourceLastUpdatedFrom: '2026-10-08T09:00:00Z',
    sourceLastUpdatedTo: '2026-10-08T10:00:00Z',
    partialData: false,
    observedRecordCount: items.length,
    duplicateRecordCount: 0,
    directoryEnrichmentState: 'complete',
    enrichedAccountCount: items.filter(item => item.directoryJoinState === 'matched').length,
    reportErrorCategory: null,
    directoryErrorCategory: null,
    ...overrides,
  };
}

function renderPage(items: AuthenticationCampaignsRegistration[], props: Partial<React.ComponentProps<typeof AuthenticationCampaignsPage>> = {}) {
  const loadRegistrations = props.loadRegistrations ?? vi.fn().mockResolvedValue(makeResponse(items));
  render(<AuthenticationCampaignsPage
    loadRegistrations={loadRegistrations}
    capabilities={allowed}
    session={undefined}
    onNavigate={vi.fn()}
    authorizationUnavailable={false}
    onAuthorizationRetry={vi.fn()}
    {...props}
  />);
  return { loadRegistrations };
}

describe('AuthenticationCampaignsPage', () => {
  afterEach(() => cleanup());

  it('opens on Passkeys with registration-first copy and explicit unevaluated eligibility', async () => {
    renderPage([baseRegistration]);

    expect((await screen.findByRole('tab', { name: 'Passkeys' })).getAttribute('aria-selected')).toBe('true');
    expect(screen.getByText('Not evaluated')).not.toBeNull();
    expect(screen.queryByText(/eligible accounts?:\s*\d/i)).toBeNull();
    expect(screen.getByText(/registration is not evidence of sign-in use/i)).not.toBeNull();
    expect(screen.queryByText(/sign-in activity/i)).toBeNull();
    expect(screen.getByText(/requires Entra ID P1 or P2/i).textContent).toContain('does not determine tenant licensing');
  });

  it('keeps explicit passKeyDeviceBound separate from generic FIDO2 and retains unknown method tokens', async () => {
    renderPage([
      account({ id: 'passkey', displayName: 'Passkey Account', methodsRegistered: ['passKeyDeviceBound', 'futureMethod'], passkeyRegistrationState: 'registered', isGenericFido2Registered: false }),
      account({ id: 'fido', displayName: 'FIDO Account', methodsRegistered: ['fido2'], passkeyRegistrationState: 'not_reported', isGenericFido2Registered: true }),
    ]);

    expect(await screen.findByText('Passkey Account')).not.toBeNull();
    expect(screen.getByText(/futureMethod/)).not.toBeNull();
    expect(screen.getByRole('button', { name: /Passkey registrations/i }).textContent).toContain('1');
    expect(screen.getByRole('button', { name: /Generic FIDO2 registrations/i }).textContent).toContain('1');
    fireEvent.click(screen.getByRole('button', { name: /Generic FIDO2 registrations/i }));
    expect(screen.getByText('FIDO Account')).not.toBeNull();
    expect(screen.queryByText('Passkey Account')).toBeNull();
  });

  it('defaults to members, offers guests and unknown types separately, and does not treat unknown types as members', async () => {
    renderPage([
      account({ id: 'member', displayName: 'Member Account', userType: 'Member' }),
      account({ id: 'guest', displayName: 'Guest Account', userType: 'Guest' }),
      account({ id: 'missing', displayName: 'Unspecified Account', userType: null }),
      account({ id: 'future', displayName: 'Future Type Account', userType: 'Partner' }),
    ]);

    expect(await screen.findByText('Member Account')).not.toBeNull();
    expect(screen.queryByText('Guest Account')).toBeNull();
    const population = screen.getByLabelText('Account population');
    expect((population as HTMLSelectElement).value).toBe('member');
    fireEvent.change(population, { target: { value: 'guest' } });
    expect(screen.getByText('Guest Account')).not.toBeNull();
    fireEvent.change(population, { target: { value: 'unknown' } });
    expect(screen.getByText('Unspecified Account')).not.toBeNull();
    expect(screen.getByText('Future Type Account')).not.toBeNull();
    expect(screen.queryByText('Member Account')).toBeNull();
  });

  it('labels KPI populations and denominators and treats a zero denominator as not applicable', async () => {
    renderPage([account({ id: 'guest', userType: 'Guest', passkeyRegistrationState: 'registered' })]);

    await screen.findByText(/Observed report records/);
    const population = screen.getByLabelText('Account population');
    fireEvent.change(population, { target: { value: 'member' } });
    const emptyMembersTile = await screen.findByRole('button', { name: /Passkey registrations/i });
    expect(emptyMembersTile.textContent).toContain('Not applicable');
    expect(emptyMembersTile.textContent).toMatch(/0 of member accounts had known passkey registration/i);
    expect(emptyMembersTile.textContent).toMatch(/0 accounts were excluded because passkey registration state is unknown/i);
    fireEvent.change(population, { target: { value: 'guest' } });
    expect(screen.getByRole('button', { name: /Passkey registrations/i }).textContent).toContain('1');
    expect(screen.getByRole('button', { name: /Passkey registrations/i }).textContent).toMatch(/1 of guest accounts had known passkey registration/i);
  });

  it('shows known and excluded unknown counts for Passkeys and SMS/Phone metrics', async () => {
    renderPage([
      account({ id: 'known-passkey', passkeyRegistrationState: 'registered', isGenericFido2Registered: true, isMfaRegistered: true, phonePreferenceState: 'phone' }),
      account({ id: 'not-reported-passkey', passkeyRegistrationState: 'not_reported', isGenericFido2Registered: false, isMfaRegistered: false, phonePreferenceState: 'not_phone' }),
      account({ id: 'unknown-member-states', passkeyRegistrationState: 'unknown', isGenericFido2Registered: null, isMfaRegistered: null, phonePreferenceState: 'unknown', phoneRegistrationState: 'unknown' }),
      account({ id: 'unknown-population-states', userType: null, passkeyRegistrationState: 'unknown', isGenericFido2Registered: null, isMfaRegistered: null, phonePreferenceState: 'unknown', phoneRegistrationState: 'unknown' }),
    ]);

    const passkeyTile = await screen.findByRole('button', { name: /Passkey registrations/i });
    expect(passkeyTile.textContent).toMatch(/2 of member accounts had known passkey registration/i);
    expect(passkeyTile.textContent).toMatch(/1 account was excluded because passkey registration state is unknown/i);
    expect(screen.getByRole('button', { name: /Generic FIDO2 registrations/i }).textContent).toMatch(/1 account was excluded because generic FIDO2 registration state is unknown/i);
    expect(screen.getByRole('button', { name: /MFA-registered accounts/i }).textContent).toMatch(/1 account was excluded because MFA registration state is unknown/i);

    fireEvent.click(screen.getByRole('tab', { name: 'SMS/Phone' }));
    const phonePreferenceTile = screen.getByRole('button', { name: /Phone-preference candidates/i });
    expect(phonePreferenceTile.textContent).toMatch(/2 of member accounts had known phone preference/i);
    expect(phonePreferenceTile.textContent).toMatch(/1 account was excluded because phone preference state is unknown/i);
    expect(screen.getByRole('button', { name: /All phone-registered accounts/i }).textContent).toMatch(/1 account was excluded because phone registration state is unknown/i);
    fireEvent.change(screen.getByLabelText('Account population'), { target: { value: 'unknown' } });
    const zeroDenominatorPhoneTile = screen.getByRole('button', { name: /Phone-preference candidates/i });
    expect(zeroDenominatorPhoneTile.textContent).toContain('Not applicable');
    expect(zeroDenominatorPhoneTile.textContent).toMatch(/0 of unknown or unspecified user types had known phone preference/i);
    expect(zeroDenominatorPhoneTile.textContent).toMatch(/1 account was excluded because phone preference state is unknown/i);
  });

  it('uses reported preference candidates by default and keeps all phone-registered accounts as a separate filter', async () => {
    renderPage([
      account({ id: 'system-phone', displayName: 'System Phone', isSystemPreferredAuthenticationMethodEnabled: true, systemPreferredAuthenticationMethods: ['sms'], userPreferredMethodForSecondaryAuthentication: 'push', phonePreferenceState: 'phone', phoneRegistrationState: 'registered' }),
      account({ id: 'user-phone', displayName: 'User Phone', isSystemPreferredAuthenticationMethodEnabled: false, systemPreferredAuthenticationMethods: ['push'], userPreferredMethodForSecondaryAuthentication: 'voiceMobile', phonePreferenceState: 'phone', phoneRegistrationState: 'not_registered' }),
      account({ id: 'no-fallback', displayName: 'Unknown Preference', isSystemPreferredAuthenticationMethodEnabled: null, userPreferredMethodForSecondaryAuthentication: 'sms', phonePreferenceState: 'unknown', phoneRegistrationState: 'registered' }),
      ...['push', 'oath', 'none'].map(value => account({ id: value, displayName: `Non-phone ${value}`, isSystemPreferredAuthenticationMethodEnabled: false, userPreferredMethodForSecondaryAuthentication: value, phonePreferenceState: 'not_phone' })),
    ]);

    fireEvent.click(await screen.findByRole('tab', { name: 'SMS/Phone' }));
    expect(screen.getByText('System Phone')).not.toBeNull();
    expect(screen.getByText('User Phone')).not.toBeNull();
    expect(screen.getByText(/System preferred: sms/)).not.toBeNull();
    expect(screen.getByText(/User preferred: voiceMobile/)).not.toBeNull();
    expect(screen.queryByText('Unknown Preference')).toBeNull();
    expect(screen.getByText(/does not prove exclusive phone dependence/i)).not.toBeNull();
    fireEvent.click(screen.getByRole('button', { name: /All phone-registered accounts/i }));
    expect(screen.getByText('Unknown Preference')).not.toBeNull();
    expect(screen.getByText('Preference source unknown')).not.toBeNull();
    expect(screen.queryByText(/User preferred: sms/)).toBeNull();
    expect(screen.queryByText('User Phone')).toBeNull();
    for (const value of ['push', 'oath', 'none']) expect(screen.queryByText(`Non-phone ${value}`)).toBeNull();
  });

  it('shows the exact unconfigured deadline hero and the candidate CTA switches to SMS/Phone', async () => {
    renderPage([baseRegistration]);

    fireEvent.click(await screen.findByRole('tab', { name: 'SMS/Phone' }));
    expect(screen.getByRole('heading', { name: 'Deadline not configured' })).not.toBeNull();
    const heroReviewLink = screen.getByRole('link', { name: 'Review SMS/Phone candidates' });
    expect(heroReviewLink.getAttribute('href')).toBe('#authentication-campaigns-account-results');
    const resultsHeading = screen.getByRole('heading', { name: 'SMS/Phone account results' });
    expect(resultsHeading.getAttribute('tabindex')).toBe('-1');
    fireEvent.click(heroReviewLink);
    expect(document.activeElement).toBe(resultsHeading);
    fireEvent.click(screen.getByRole('tab', { name: 'Passkeys' }));
    fireEvent.click(screen.getByRole('button', { name: 'Review SMS/Phone candidates' }));
    expect(screen.getByRole('tab', { name: 'SMS/Phone' }).getAttribute('aria-selected')).toBe('true');
  });

  it('provides collapsible department, office, and company rollups with unknown and unavailable buckets', async () => {
    renderPage([
      account({ id: 'known', displayName: 'Known', department: 'Engineering', officeLocation: 'Oslo', companyName: 'Atea', directoryJoinState: 'matched' }),
      account({ id: 'null-fields', displayName: 'Unknown attributes', department: null, officeLocation: null, companyName: null, directoryJoinState: 'matched' }),
      account({ id: 'failed-join', displayName: 'Failed join', department: null, officeLocation: null, companyName: null, directoryJoinState: 'unavailable' }),
    ], { loadRegistrations: vi.fn().mockResolvedValue(makeResponse([
      account({ id: 'known', displayName: 'Known', department: 'Engineering', officeLocation: 'Oslo', companyName: 'Atea', directoryJoinState: 'matched' }),
      account({ id: 'null-fields', displayName: 'Unknown attributes', department: null, officeLocation: null, companyName: null, directoryJoinState: 'matched' }),
      account({ id: 'failed-join', displayName: 'Failed join', department: null, officeLocation: null, companyName: null, directoryJoinState: 'unavailable' }),
    ], { partialData: true, directoryEnrichmentState: 'partial', enrichedAccountCount: 2 })) });

    await screen.findByText('Engineering');
    const department = screen.getAllByText('Department').find(element => element.tagName === 'SUMMARY')!;
    const group = department.closest('details');
    expect(group?.hasAttribute('open')).toBe(false);
    fireEvent.click(department);
    expect(group?.hasAttribute('open')).toBe(true);
    expect(within(group as HTMLElement).getByText('Engineering')).not.toBeNull();
    expect(within(group as HTMLElement).getByText('Unknown')).not.toBeNull();
    expect(within(group as HTMLElement).getByText('Unavailable')).not.toBeNull();
    expect(screen.getAllByText(/2 enriched accounts of 3 observed accounts/i).length).toBeGreaterThan(0);
  });

  it('searches the normalized response, reports filtered totals, paginates locally, and gates user links on both grants', async () => {
    const items = Array.from({ length: 12 }, (_, index) => account({
      id: `account-${index}`,
      displayName: `Account ${index}`,
      userPrincipalName: `account${index}@example.test`,
    }));
    const session: AppSession = {
      user: { displayName: 'Admin' },
      workspace: { id: 'workspace', name: 'Workspace', moduleAccess: ['users'], enabledModules: ['users'] },
    };
    const capabilities: CapabilityDecision[] = [...allowed, { capability: 'users.view', state: 'allowed', reasonCode: 'active_role' }];
    renderPage(items, { session, capabilities });

    await screen.findByText('Account 0');
    const search = screen.getByLabelText('Search accounts');
    fireEvent.change(search, { target: { value: 'account 1' } });
    expect(await screen.findByText('Account 1')).not.toBeNull();
    expect(await screen.findByText(/3 results/i)).not.toBeNull();
    expect(screen.queryByText('Account 2')).toBeNull();
    fireEvent.change(search, { target: { value: '' } });
    expect(await screen.findByText('Account 0')).not.toBeNull();
    expect((screen.getByRole('button', { name: 'Next page' }) as HTMLButtonElement).disabled).toBe(false);
    fireEvent.click(screen.getByRole('button', { name: 'Next page' }));
    expect(screen.getByText('Account 10')).not.toBeNull();

    cleanup();
    const missingCapability = [...allowed];
    const deniedSession = { ...session, workspace: { ...session.workspace, moduleAccess: [], enabledModules: [] } };
    render(<AuthenticationCampaignsPage loadRegistrations={vi.fn().mockResolvedValue(makeResponse(items))} capabilities={missingCapability} session={deniedSession} onNavigate={vi.fn()} />);
    await screen.findByText('Account 0');
    expect(screen.queryByRole('link', { name: 'Account 0' })).toBeNull();
    cleanup();
    render(<AuthenticationCampaignsPage loadRegistrations={vi.fn().mockResolvedValue(makeResponse(items))} capabilities={allowed} session={session} onNavigate={vi.fn()} />);
    await screen.findByText('Account 0');
    expect(screen.queryByRole('link', { name: 'Account 0' })).toBeNull();
  });

  it('distinguishes loading, successful empty, no-match, denied, unavailable, partial, stale, and unknown freshness states', async () => {
    let resolve!: (value: AuthenticationCampaignsResponse) => void;
    const loader = vi.fn(() => new Promise<AuthenticationCampaignsResponse>(done => { resolve = done; }));
    const rendered = render(<AuthenticationCampaignsPage loadRegistrations={loader} capabilities={allowed} />);
    await waitFor(() => expect(loader).toHaveBeenCalledOnce());
    expect(rendered.container.querySelector('[role="tabpanel"][aria-busy="true"]')).not.toBeNull();
    resolve(makeResponse([], { observedRecordCount: 0 }));
    expect(await screen.findByText(/no accounts were returned/i)).not.toBeNull();
    fireEvent.change(screen.getByLabelText('Search accounts'), { target: { value: 'nobody' } });
    expect(screen.getByText(/no accounts match/i)).not.toBeNull();

    const noSourceTime = makeResponse([baseRegistration], { sourceLastUpdatedFrom: null, sourceLastUpdatedTo: null });
    cleanup();
    const { rerender, container } = render(<AuthenticationCampaignsPage loadRegistrations={vi.fn().mockResolvedValue(noSourceTime)} capabilities={allowed} />);
    expect(await screen.findByText(/source freshness is unknown/i)).not.toBeNull();
    expect(screen.queryByText(/Data is fresh/)).toBeNull();
    expect(screen.queryByText(/Latest source update is within 36 hours/i)).toBeNull();
    expect(container.querySelector('.data-freshness')).toBeNull();

    const staleTime = new Date(Date.now() - 37 * 60 * 60 * 1000).toISOString();
    const recentTime = new Date(Date.now() - 60 * 60 * 1000).toISOString();
    rerender(<AuthenticationCampaignsPage loadRegistrations={vi.fn().mockResolvedValue(makeResponse([baseRegistration], { sourceLastUpdatedFrom: staleTime, sourceLastUpdatedTo: null }))} capabilities={allowed} />);
    expect(await screen.findByText(/source freshness is unknown/i)).not.toBeNull();
    expect(screen.queryByText(/Latest source update is within 36 hours/i)).toBeNull();
    expect(screen.getByText(/source records are older than 36 hours/i)).not.toBeNull();

    rerender(<AuthenticationCampaignsPage loadRegistrations={vi.fn().mockResolvedValue(makeResponse([baseRegistration], { sourceLastUpdatedFrom: staleTime, sourceLastUpdatedTo: recentTime, partialData: true, reportErrorCategory: 'unavailable', directoryErrorCategory: 'unavailable' }))} capabilities={allowed} />);
    expect(await screen.findByText(/source records are older than 36 hours/i)).not.toBeNull();
    expect(screen.queryByText(/source report may be stale/i)).toBeNull();
    expect(screen.getByText(/Latest source update is within 36 hours/i)).not.toBeNull();
    expect(screen.getByText(/Source report timestamp range/)).not.toBeNull();
    expect(screen.getByText(/partial results/i)).not.toBeNull();

    rerender(<AuthenticationCampaignsPage loadRegistrations={vi.fn().mockResolvedValue(makeResponse([baseRegistration], { sourceLastUpdatedFrom: staleTime, sourceLastUpdatedTo: staleTime, partialData: true, reportErrorCategory: 'unavailable', directoryErrorCategory: 'unavailable' }))} capabilities={allowed} />);
    expect(await screen.findByText(/source records are older than 36 hours/i)).not.toBeNull();
    expect(screen.getAllByText(/source report may be stale/i).length).toBeGreaterThan(0);
    expect(screen.getByText(/Source report timestamp range/)).not.toBeNull();
    expect(screen.getAllByText(/latest source update is more than 36 hours old/i).length).toBeGreaterThan(0);
  });

  it('renders access diagnostics and never requests registrations when authorization is unavailable', async () => {
    const loadRegistrations = vi.fn();
    render(<AuthenticationCampaignsPage
      loadRegistrations={loadRegistrations}
      capabilities={[{ capability: 'authentication.campaigns.view', state: 'pim_activation_required', reasonCode: 'role_inactive' }]}
      authorizationUnavailable
      onAuthorizationRetry={vi.fn()}
    />);

    expect(await screen.findByText(/An active Reports Reader, Security Reader, Security Administrator, or Global Reader role is required/i)).not.toBeNull();
    expect(screen.getByText(/Microsoft Graph report permission/i)).not.toBeNull();
    expect(screen.getByText(/Directory enrichment uses User.Read.All/i)).not.toBeNull();
    expect(screen.getByText(/P1 or P2/i)).not.toBeNull();
    expect(loadRegistrations).not.toHaveBeenCalled();
  });

  it.each([
    [403, 'Report access denied'],
    [503, 'Registration report unavailable'],
  ])('shows report errors as %s states without claiming an empty report', async (status, title) => {
    render(<AuthenticationCampaignsPage
      loadRegistrations={vi.fn().mockRejectedValue(Object.assign(new Error('source failed'), { status, category: 'unavailable' }))}
      capabilities={allowed}
    />);

    expect(await screen.findByText(title)).not.toBeNull();
    expect(screen.queryByText(/no accounts were returned/i)).toBeNull();
  });

  it('keeps keyboard-operable tabs, labeled filters, semantic account tables, and non-color status text', async () => {
    renderPage([baseRegistration]);

    const passkeys = await screen.findByRole('tab', { name: 'Passkeys' });
    await screen.findByRole('table', { name: /account registration results/i });
    expect(screen.getByLabelText('Account population')).not.toBeNull();
    passkeys.focus();
    fireEvent.keyDown(passkeys, { key: 'ArrowRight' });
    expect(screen.getByRole('tab', { name: 'SMS/Phone' }).getAttribute('aria-selected')).toBe('true');
    expect(screen.getByText('Deadline not configured')).not.toBeNull();
  });
});
