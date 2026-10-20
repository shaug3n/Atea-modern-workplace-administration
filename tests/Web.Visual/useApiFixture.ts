import { useCallback } from 'react';

type FixtureRequest = { path: string; method: string; body?: unknown };

declare global {
  interface Window {
    __visualApiRequests: FixtureRequest[];
    __visualRequestCounts: Record<string, number>;
  }
}

const fetchedAt = '2026-09-21T12:00:00.000Z';
const modulesEnabled = ['users', 'devices', 'licenses', 'exchange', 'authentication-campaigns', 'about', 'feedback'];

function scenario() {
  return new URLSearchParams(window.location.search).get('fixtureState') ?? 'populated';
}

function source<T>(state: string, data: T | null, scope: string, partialData = false) {
  return { state, fetchedAt, data, scope, partialData };
}

function overviewResponse() {
  const partial = scenario() === 'partial';
  const empty = scenario() === 'empty';
  return {
    effectiveModules: ['users', 'licenses', 'devices'],
    effectiveCapabilities: [
      { capability: 'users.view', state: 'allowed', reasonCode: 'active_role' },
      { capability: 'licenses.view', state: 'allowed', reasonCode: 'active_role' },
      { capability: 'devices.view', state: 'allowed', reasonCode: 'active_role' },
      { capability: 'audit.view', state: 'allowed', reasonCode: 'active_role' },
    ],
    users: source(empty ? 'empty' : partial ? 'stale' : 'fresh', empty ? null : { totalUsers: 120 }, 'tenant_wide_verified', partial),
    licenseCoverage: source(empty ? 'empty' : partial ? 'stale' : 'fresh', empty ? null : { assignedUsers: 90, totalUsers: 120, percentage: 75 }, 'tenant_wide_verified', partial),
    activity: source(empty ? 'empty' : partial ? 'stale' : 'fresh', { items: empty ? [] : [
      { action: 'User created', outcome: 'Succeeded', timestamp: fetchedAt },
      { action: 'License assignment', outcome: 'Denied', timestamp: fetchedAt },
      { action: 'Group membership changed', outcome: 'Succeeded', timestamp: fetchedAt },
    ] }, 'workspace', partial),
  };
}

function auditResponse() {
  const items = scenario() === 'empty' ? [] : [
    {
      id: 'activity-1', workspaceId: 'fixture-workspace', tenantId: 'fixture-tenant', actorTenantId: 'fixture-tenant',
      actorObjectId: 'fixture-actor', action: 'UserCreated', targetType: 'user', targetId: 'fixture-user',
      outcome: 'succeeded', timestamp: fetchedAt, correlationId: 'fixture-correlation', graphCorrelationId: null,
      graphRequestId: null, pimRequestId: null, failureCategory: null, safeMetadataJson: '{}',
    },
    {
      id: 'activity-2', workspaceId: 'fixture-workspace', tenantId: 'fixture-tenant', actorTenantId: 'fixture-tenant',
      actorObjectId: 'fixture-actor', action: 'GroupMemberAdded', targetType: 'group', targetId: 'fixture-group',
      outcome: 'denied', timestamp: '2026-09-20T12:00:00.000Z', correlationId: null, graphCorrelationId: null,
      graphRequestId: null, pimRequestId: null, failureCategory: 'permission_denied', safeMetadataJson: '{}',
    },
  ];
  return {
    items,
    fetchedAt,
    freshness: scenario() === 'partial' ? 'stale' : 'fresh',
    partialData: scenario() === 'partial',
    authoritativeSourceNotice: 'Workspace action audit only; Microsoft 365 audit logs remain authoritative for directory changes.',
    nextContinuationToken: items.length ? 'fixture-next-page' : null,
  };
}

function exchangeResponse(url: URL) {
  const items = scenario() === 'empty' ? [] : [
    { userId: 'mailbox-ada', displayName: 'Ada Lovelace', address: 'ada@example.test' },
    { userId: 'mailbox-grace', displayName: 'Grace Hopper', address: 'grace@example.test' },
  ];
  return {
    items,
    continuationToken: items.length ? 'fixture-exchange-page' : null,
    limitation: 'Directory-backed mail-enabled identities; this is not a complete Exchange inventory.',
  };
}

function count(path: string) {
  window.__visualRequestCounts ??= {};
  window.__visualRequestCounts[path] = (window.__visualRequestCounts[path] ?? 0) + 1;
  return window.__visualRequestCounts[path];
}

async function fixtureApi(path: string, init: RequestInit = {}) {
  if (!path.startsWith('/api/')) throw new Error(`Fixture blocked non-local API path: ${path}`);
  const method = init.method ?? 'GET';
  let body: unknown;
  if (typeof init.body === 'string') {
    try { body = JSON.parse(init.body); } catch { body = '[unparsed body]'; }
  }
  window.__visualApiRequests ??= [];
  window.__visualApiRequests.push({ path, method, body });
  const requestCount = count(`${method} ${path}`);
  const currentScenario = scenario();
  const isRead = method === 'GET';

  if (isRead && currentScenario === 'loading') return await new Promise<Response>(() => {});
  if (isRead && (currentScenario === 'unavailable' || (currentScenario === 'retry' && requestCount === 1))) {
    return Response.json({ title: 'Fixture data unavailable' }, { status: 503 });
  }

  const url = new URL(path, window.location.origin);
  if (url.pathname === '/api/overview') return Response.json(overviewResponse());
  if (url.pathname === '/api/audit/events') return Response.json(auditResponse());
  if (url.pathname === '/api/exchange/mailboxes') return Response.json(exchangeResponse(url));
  if (/^\/api\/exchange\/mailboxes\/[^/]+\/overview$/.test(url.pathname)) {
    if (url.pathname.includes('mailbox-grace')) return Response.json({ error: { category: 'not_authorized', message: 'Fixture verification unavailable.' } }, { status: 403 });
    return Response.json({
      userId: 'mailbox-ada', verificationStatus: 'verified', mailboxType: 'UserMailbox',
      settings: { timeZone: 'Pacific Standard Time', language: 'en-US', dateFormat: 'M/d/yyyy', timeFormat: 'h:mm tt', automaticRepliesStatus: 'disabled' },
    });
  }
  if (url.pathname === '/api/workspaces/current/connection-health') return Response.json({ status: 'permission_incomplete', lastVerifiedAt: null, correlationId: 'fixture-health' });
  if (url.pathname === '/api/workspaces/current/connection-health/check') return Response.json({ status: 'connected', lastVerifiedAt: fetchedAt });
  if (url.pathname === '/api/workspaces/current/consent/start') return Response.json({ error: 'Consent actions are not enabled in this local visual fixture.' }, { status: 503 });
  if (url.pathname === '/api/workspaces/current/settings') {
    const settings = {
      displayName: 'Local Customer Workspace', enabledModules: modulesEnabled, defaultColumns: ['displayName', 'userPrincipalName'],
      defaultFilters: {}, supportInstructions: '', defaultTheme: 'light', access: { state: 'allowed' },
    };
    if (method === 'PATCH') {
      const updated = { ...settings, ...(body as Record<string, unknown>) };
      return Response.json(updated);
    }
    return Response.json(settings);
  }
  if (url.pathname === '/api/workspaces/current/modules') {
    if (method === 'PATCH') {
      const requested = (body as { enabledModules?: string[] } | undefined)?.enabledModules ?? modulesEnabled;
      modulesEnabled.splice(0, modulesEnabled.length, ...requested);
    }
    return Response.json({ enabledModules: [...modulesEnabled], dormantGrantCount: 2 });
  }
  if (url.pathname === '/api/workspaces/current/access') {
    return Response.json({
      memberships: [{ id: 'member-fixture', tenantObjectId: 'fixture-member', email: 'member@example.test', platformRole: 'member', moduleKeys: ['users', 'licenses'], createdAt: fetchedAt }],
      invitations: [{ id: 'invite-fixture', email: 'pending@example.test', displayName: 'Pending Member', role: 'member', moduleKeys: ['users'], expiresAt: '2027-09-21T12:00:00.000Z', redeemedAt: null, revokedAt: null }],
    });
  }
  if (url.pathname === '/api/users' && method === 'POST') {
    return Response.json({ status: 'succeeded', requiredCapability: 'users.create', replayed: false, temporaryCredentialNotice: { temporaryPassword: 'LocalOnly-NotASecret!', forceChangePasswordNextSignIn: true } }, { status: 201 });
  }
  if (url.pathname === '/api/users') {
    return Response.json({
      items: [{ id: 'user-fixture', displayName: 'Ada Lovelace', userPrincipalName: 'ada@example.test', mail: 'ada@example.test', accountEnabled: true, userType: 'Member' }],
      continuationToken: null, fetchedAt, freshness: currentScenario === 'partial' ? 'stale' : 'fresh', partialData: currentScenario === 'partial',
    });
  }
  return new Response(null, { status: 404 });
}

export function useApi() {
  return useCallback(fixtureApi, []);
}
