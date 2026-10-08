import React from 'react';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from '../../src/Web/src/app/App';
import type { AppSession } from '../../src/Web/src/components/TenantContextHeader';
import type { CapabilityDecision, CapabilitySnapshot } from '../../src/Web/src/capabilities/capabilityTypes';
import type {
  AuthenticationCampaignsRegistration,
  AuthenticationCampaignsResponse,
} from '../../src/Web/src/features/authentication-campaigns/authenticationCampaignsApi';

const apiFetch = vi.hoisted(() => vi.fn());

vi.mock('../../src/Web/src/auth/useApi', () => ({ useApi: () => apiFetch }));
vi.mock('../../src/Web/src/auth/AuthProvider', () => ({
  useAuth: () => ({
    account: { username: 'admin@example.test' },
    getApiToken: vi.fn().mockResolvedValue('api-token'),
    signIn: vi.fn(),
    switchAccount: vi.fn(),
  }),
}));

type ApiRequest = { path: string; method: string };
type FixtureOptions = {
  session?: AppSession;
  capabilities?: CapabilitySnapshot;
  report?: AuthenticationCampaignsResponse;
  reportStatus?: number;
};

const reportPath = '/api/authentication-campaigns/registrations';

function makeSession(overrides: Partial<AppSession['workspace']> = {}): AppSession {
  return {
    user: { displayName: 'Workspace administrator', userPrincipalName: 'admin@example.test' },
    workspace: {
      id: 'workspace-1',
      name: 'Regression workspace',
      moduleAccess: ['users', 'authentication-campaigns'],
      enabledModules: ['users', 'authentication-campaigns'],
      ...overrides,
    },
  };
}

function makeCapabilities(decision: CapabilityDecision = {
  capability: 'authentication.campaigns.view',
  state: 'allowed',
  reasonCode: 'active_role',
}): CapabilitySnapshot {
  return {
    workspaceId: 'workspace-1',
    evaluatedAt: new Date().toISOString(),
    sourceState: 'graph_authoritative',
    capabilities: [
      decision,
      { capability: 'users.view', state: 'allowed', reasonCode: 'active_role' },
    ],
  };
}

function makeRegistration(
  id: string,
  overrides: Partial<AuthenticationCampaignsRegistration> = {},
): AuthenticationCampaignsRegistration {
  return {
    id,
    displayName: id,
    userPrincipalName: `${id.toLocaleLowerCase().replaceAll(' ', '.')}@example.test`,
    userType: 'Member',
    methodsRegistered: ['microsoftAuthenticator'],
    isMfaRegistered: true,
    isMfaCapable: true,
    isPasswordlessCapable: false,
    isSystemPreferredAuthenticationMethodEnabled: false,
    systemPreferredAuthenticationMethods: null,
    userPreferredMethodForSecondaryAuthentication: 'microsoftAuthenticator',
    lastUpdatedDateTime: new Date().toISOString(),
    passkeyRegistrationState: 'not_reported',
    isGenericFido2Registered: false,
    phoneRegistrationState: 'not_registered',
    phonePreferenceState: 'not_phone',
    department: null,
    officeLocation: null,
    companyName: null,
    directoryJoinState: 'matched',
    ...overrides,
  };
}

function makeReport(
  overrides: Partial<AuthenticationCampaignsResponse> = {},
): AuthenticationCampaignsResponse {
  const now = Date.now();
  return {
    items: [
      makeRegistration('Ada Lovelace', {
        id: 'member-ada',
        userPrincipalName: 'ada@example.test',
        methodsRegistered: ['passkey', 'phone'],
        passkeyRegistrationState: 'registered',
        isGenericFido2Registered: false,
        isMfaRegistered: true,
        phoneRegistrationState: 'registered',
        phonePreferenceState: 'phone',
        isSystemPreferredAuthenticationMethodEnabled: true,
        systemPreferredAuthenticationMethods: ['phone'],
        department: 'Finance',
        officeLocation: 'Oslo',
        companyName: 'Example Works',
      }),
      makeRegistration('Grace Hopper', {
        id: 'member-grace',
        userPrincipalName: 'grace@example.test',
        passkeyRegistrationState: 'not_reported',
        isGenericFido2Registered: true,
        isMfaRegistered: false,
        phoneRegistrationState: 'registered',
        phonePreferenceState: 'not_phone',
        department: 'Engineering',
        officeLocation: 'Bergen',
        companyName: 'Example Works',
      }),
      makeRegistration('Katherine Johnson', {
        id: 'member-katherine',
        userPrincipalName: 'katherine@example.test',
        passkeyRegistrationState: 'not_reported',
        isGenericFido2Registered: false,
        phoneRegistrationState: 'not_registered',
        phonePreferenceState: 'phone',
        directoryJoinState: 'matched',
        department: 'Finance',
        officeLocation: 'Oslo',
        companyName: 'Example Works',
      }),
      makeRegistration('Carmen Guest', {
        id: 'guest-carmen',
        userPrincipalName: 'carmen.guest@example.test',
        userType: 'Guest',
        directoryJoinState: 'matched',
        department: 'Partners',
      }),
      makeRegistration('Unclassified account', {
        id: 'unknown-null-type',
        userPrincipalName: 'unknown@example.test',
        userType: null,
        directoryJoinState: 'partial',
      }),
      makeRegistration('Automation Identity', {
        id: 'unknown-service-type',
        userPrincipalName: 'automation@example.test',
        userType: 'ServicePrincipal',
        directoryJoinState: 'unavailable',
      }),
    ],
    fetchedAt: new Date(now - 60_000).toISOString(),
    sourceLastUpdatedFrom: new Date(now - 60 * 60_000).toISOString(),
    sourceLastUpdatedTo: new Date(now - 30 * 60_000).toISOString(),
    partialData: false,
    observedRecordCount: 7,
    duplicateRecordCount: 1,
    directoryEnrichmentState: 'partial',
    enrichedAccountCount: 4,
    reportErrorCategory: null,
    directoryErrorCategory: null,
    ...overrides,
  };
}

function configureApi(options: FixtureOptions = {}) {
  const requests: ApiRequest[] = [];
  const session = options.session ?? makeSession();
  const capabilities = options.capabilities ?? makeCapabilities();
  const report = options.report ?? makeReport();

  apiFetch.mockImplementation(async (input: string, init?: RequestInit) => {
    const path = String(input);
    const method = (init?.method ?? 'GET').toUpperCase();
    requests.push({ path, method });
    expect(new URL(path, window.location.origin).origin).toBe(window.location.origin);

    if (path === '/api/session') return Response.json(session);
    if (path === '/api/capabilities') return Response.json(capabilities);
    if (path === '/api/user-preferences/theme') return Response.json({ theme: 'light' });
    if (path === '/api/workspaces/current/connection-health') {
      return Response.json({ status: 'connected', lastVerifiedAt: null });
    }
    if (path === reportPath) {
      if (options.reportStatus && options.reportStatus >= 400) {
        return Response.json({ error: { category: 'unavailable' } }, { status: options.reportStatus });
      }
      return Response.json(report);
    }
    throw new Error(`Unexpected fixture API route: ${path}`);
  });

  return requests;
}

function visit(path = '/authentication-campaigns') {
  window.history.replaceState({}, '', path);
  return render(<App />);
}

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
  apiFetch.mockReset();
  window.history.replaceState({}, '', '/');
});

beforeEach(() => {
  apiFetch.mockReset();
});

describe('authentication campaign owned-page journey', () => {
  it('opens from sidebar navigation and switches between passkey and SMS/Phone views', async () => {
    window.history.replaceState({}, '', '/identity');
    const requests = configureApi();
    const browserFetch = vi.spyOn(window, 'fetch');
    render(<App />);

    const navigation = await screen.findByRole('navigation', { name: 'Primary navigation' });
    fireEvent.click(within(navigation).getByRole('link', { name: 'Authentication campaigns' }));

    expect(await screen.findByRole('tab', { name: 'Passkeys', selected: true })).toBeTruthy();
    expect(await screen.findByRole('link', { name: 'Ada Lovelace' })).toBeTruthy();
    expect(window.location.pathname).toBe('/authentication-campaigns');
    expect(requests.filter(request => request.path === reportPath)).toHaveLength(1);

    fireEvent.click(screen.getByRole('tab', { name: 'SMS/Phone' }));
    expect(screen.getByRole('tab', { name: 'SMS/Phone', selected: true })).toBeTruthy();
    fireEvent.click(screen.getByRole('tab', { name: 'Passkeys' }));
    expect(screen.getByRole('tab', { name: 'Passkeys', selected: true })).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Review SMS/Phone candidates' }));
    expect(screen.getByRole('tab', { name: 'SMS/Phone', selected: true })).toBeTruthy();
    fireEvent.click(screen.getByRole('link', { name: 'Review SMS/Phone candidates' }));
    expect(screen.getByRole('heading', { name: 'SMS/Phone account results' })).toBeTruthy();

    expect(requests.map(request => request.path)).toEqual([
      '/api/session',
      '/api/capabilities',
      '/api/user-preferences/theme',
      '/api/workspaces/current/connection-health',
      reportPath,
    ]);
    expect(requests.every(request => request.method === 'GET')).toBe(true);
    expect(requests.some(request => /graph\.microsoft\.com|\/pim|authentication\/methods|\/policies/i.test(request.path))).toBe(false);
    expect(browserFetch).not.toHaveBeenCalled();
  });

  it.each([
    ['missing module assignment', { moduleAccess: ['users'], enabledModules: ['users', 'authentication-campaigns'] }, /You don't have access to Authentication campaigns/],
    ['disabled module', { moduleAccess: ['users', 'authentication-campaigns'], enabledModules: ['users'] }, /Authentication campaigns is turned off for this workspace/],
  ])('suppresses report access for %s', async (_caseName, workspace, expectedMessage) => {
    const requests = configureApi({ session: makeSession(workspace) });
    visit();

    expect(await screen.findByText(expectedMessage)).toBeTruthy();
    expect(screen.queryByRole('link', { name: 'Authentication campaigns' })).toBeNull();
    expect(requests.some(request => request.path === reportPath)).toBe(false);
  });

  it.each([
    ['denied capability', { capability: 'authentication.campaigns.view', state: 'hidden', reasonCode: 'role_missing' }],
    ['inactive PIM eligibility', {
      capability: 'authentication.campaigns.view',
      state: 'pim_activation_required',
      reasonCode: 'pim_activation_required',
      pim: { state: 'eligible_inactive', activationUrl: 'https://entra.microsoft.com/pim' },
    }],
  ] as const)('does not fetch registrations for %s', async (_caseName, decision) => {
    const requests = configureApi({ capabilities: makeCapabilities(decision) });
    visit();

    expect(await screen.findByText('Authentication campaign access is unavailable')).toBeTruthy();
    expect(screen.getByText(decision.state)).toBeTruthy();
    expect(requests.some(request => request.path === reportPath)).toBe(false);
  });

  it('keeps observed report counts, directory rollups, and source/fetch freshness visible', async () => {
    const requests = configureApi();
    visit();

    await screen.findByRole('link', { name: 'Ada Lovelace' });
    const panel = await screen.findByRole('tabpanel', { name: 'Passkeys' });
    expect(within(panel).getByText('3 results')).toBeTruthy();
    expect(screen.getByText(/6 observed accounts from 7 report records/)).toBeTruthy();
    expect(screen.getByText(/4 enriched accounts of 6 observed accounts \(partial\)/)).toBeTruthy();
    expect(screen.getByText(/1 duplicate report records were excluded/)).toBeTruthy();
    expect(screen.getByText(/Latest source update is within 36 hours/)).toBeTruthy();
    expect(screen.getByText(/Source report timestamp range/).textContent).toContain('Fetch time is shown separately.');
    expect(screen.getByRole('status').textContent).toContain('Updated');

    fireEvent.click(screen.getByText('Department', { selector: 'summary' }));
    const departmentRollup = screen.getByRole('table', { name: 'Department rollup' });
    expect(within(departmentRollup).getByText('Finance')).toBeTruthy();
    expect(within(departmentRollup).getByText('Engineering')).toBeTruthy();

    expect(requests.every(request => request.method === 'GET')).toBe(true);
  });

  it('keeps passkey, generic FIDO2, phone-preference, and phone-registration populations separate', async () => {
    configureApi();
    visit();
    await screen.findByRole('link', { name: 'Ada Lovelace' });

    fireEvent.click(screen.getByRole('button', { name: /Passkey registrations/ }));
    expect(await screen.findByText('1 result')).toBeTruthy();
    expect(screen.getByRole('link', { name: 'Ada Lovelace' })).toBeTruthy();
    expect(screen.queryByRole('link', { name: 'Grace Hopper' })).toBeNull();

    fireEvent.click(screen.getByRole('button', { name: /Generic FIDO2 registrations/ }));
    expect(await screen.findByText('1 result')).toBeTruthy();
    expect(screen.getByRole('link', { name: 'Grace Hopper' })).toBeTruthy();
    expect(screen.queryByRole('link', { name: 'Ada Lovelace' })).toBeNull();

    fireEvent.click(screen.getByRole('tab', { name: 'SMS/Phone' }));
    expect(await screen.findByText('2 results')).toBeTruthy();
    expect(screen.getByRole('link', { name: 'Katherine Johnson' })).toBeTruthy();
    expect(screen.queryByRole('link', { name: 'Grace Hopper' })).toBeNull();

    fireEvent.click(screen.getByRole('button', { name: /All phone-registered accounts/ }));
    expect(await screen.findByText('2 results')).toBeTruthy();
    expect(screen.getByRole('link', { name: 'Grace Hopper' })).toBeTruthy();
    expect(screen.queryByRole('link', { name: 'Katherine Johnson' })).toBeNull();
  });

  it('keeps guest and unknown user types as distinct selectable populations', async () => {
    configureApi();
    visit();
    await screen.findByRole('link', { name: 'Ada Lovelace' });
    const population = screen.getByRole('combobox', { name: 'Account population' });

    fireEvent.change(population, { target: { value: 'guest' } });
    expect(await screen.findByText('1 result')).toBeTruthy();
    expect(screen.getByRole('link', { name: 'Carmen Guest' })).toBeTruthy();
    expect(screen.queryByRole('link', { name: 'Ada Lovelace' })).toBeNull();

    fireEvent.change(population, { target: { value: 'unknown' } });
    expect(await screen.findByText('2 results')).toBeTruthy();
    expect(screen.getByRole('link', { name: 'Unclassified account' })).toBeTruthy();
    expect(screen.getByRole('link', { name: 'Automation Identity' })).toBeTruthy();
    expect(screen.queryByRole('link', { name: 'Carmen Guest' })).toBeNull();
  });

  it('distinguishes an unavailable registration report from an empty report', async () => {
    configureApi({ reportStatus: 503 });
    visit();

    expect(await screen.findByText('Registration report unavailable')).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Retry report' })).toBeTruthy();
    expect(screen.queryByText(/observed accounts from/)).toBeNull();

    cleanup();
    configureApi({
      report: makeReport({
        items: [],
        observedRecordCount: 0,
        duplicateRecordCount: 0,
        directoryEnrichmentState: 'unavailable',
        enrichedAccountCount: 0,
      }),
    });
    visit();

    expect(await screen.findByText('No accounts were returned in the registration report.')).toBeTruthy();
    expect(screen.queryByText('Registration report unavailable')).toBeNull();
  });

  it('shows partial and stale source data as separate report states', async () => {
    configureApi({
      report: makeReport({
        partialData: true,
        reportErrorCategory: 'throttled',
        directoryErrorCategory: 'forbidden',
      }),
    });
    visit();

    expect(await screen.findByText(/Partial results are shown/)).toBeTruthy();
    expect(screen.getByText(/Report: throttled\. Directory: forbidden/)).toBeTruthy();
    expect(screen.getByText(/Latest source update is within 36 hours/)).toBeTruthy();

    cleanup();
    configureApi({
      report: makeReport({
        sourceLastUpdatedFrom: '2020-01-01T00:00:00Z',
        sourceLastUpdatedTo: '2020-01-02T00:00:00Z',
      }),
    });
    visit();

    expect(await screen.findByText(/source report may be stale/i)).toBeTruthy();
    expect(screen.getByText(/Some source records are older than 36 hours/)).toBeTruthy();
  });

  it('keeps unknown source freshness distinct from its API fetch time', async () => {
    configureApi({
      report: makeReport({ sourceLastUpdatedFrom: null, sourceLastUpdatedTo: null }),
    });
    visit();

    expect(await screen.findByText(/Source freshness is unknown/)).toBeTruthy();
    expect(screen.getByText(/Fetched/)).toBeTruthy();
    expect(screen.queryByText(/Latest source update is within 36 hours/)).toBeNull();
  });

  it('preserves account facts, labels, and user navigation in wide and narrow data views', async () => {
    const setCompactMedia = (matches: boolean) => {
      vi.stubGlobal('matchMedia', (query: string) => ({
        matches,
        media: query,
        onchange: null,
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
        addListener: vi.fn(),
        removeListener: vi.fn(),
        dispatchEvent: vi.fn(),
      }));
    };

    setCompactMedia(false);
    configureApi();
    visit();
    const wideRegion = await screen.findByRole('region', { name: 'Account registration results' });
    const wideTable = within(wideRegion).getByRole('table', { name: 'Account registration results' });
    expect(within(wideTable).getByRole('columnheader', { name: 'Passkey registration' })).toBeTruthy();
    expect(within(wideTable).getByRole('columnheader', { name: 'Generic FIDO2 registration' })).toBeTruthy();
    expect(within(wideTable).getByRole('link', { name: 'Ada Lovelace' }).getAttribute('href')).toBe('/users/member-ada');
    expect(within(wideTable).getAllByText('Registered').length).toBeGreaterThan(0);
    expect(screen.getByRole('button', { name: /Passkey registrations/ })).toBeTruthy();
    expect(screen.getByRole('combobox', { name: 'Account population' })).toBeTruthy();

    cleanup();
    setCompactMedia(true);
    configureApi();
    visit();
    const compactList = await screen.findByRole('list', { name: 'Account registration results' });
    const adaCard = within(compactList).getByRole('link', { name: 'Ada Lovelace' }).closest('article');
    expect(adaCard).not.toBeNull();
    expect(within(adaCard!).getByText('ada@example.test')).toBeTruthy();
    expect(within(adaCard!).getAllByText('Registered').length).toBeGreaterThan(0);
    expect(within(adaCard!).getByText('No')).toBeTruthy();
    expect(within(compactList).getByRole('link', { name: 'Ada Lovelace' }).getAttribute('href')).toBe('/users/member-ada');
    expect(screen.getByRole('button', { name: /Passkey registrations/ })).toBeTruthy();
    expect(screen.getByRole('combobox', { name: 'Account population' })).toBeTruthy();
  });
});
