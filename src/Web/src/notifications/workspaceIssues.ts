import type { CapabilityDecision, CapabilitySnapshot } from '../capabilities/capabilityTypes';
import type { AppSession } from '../components/TenantContextHeader';
import type { ConnectionHealth } from '../features/overview/OverviewPage';

export type WorkspaceIssue = {
  key: string;
  area: string;
  kind: 'setup' | 'access' | 'service';
  severity: 'warning' | 'info';
  title: string;
  detail: string;
  action?: { label: string; href?: string };
  correlationId?: string;
  audience: 'workspace_admin' | 'affected_user';
  lastCheckedAt: string;
};

export type WorkspaceIssueReport = Omit<WorkspaceIssue, 'audience' | 'lastCheckedAt'>;

export function reportedIssueKey(key: string): string {
  let hash = 2166136261;
  for (let index = 0; index < key.length; index++) hash = Math.imul(hash ^ key.charCodeAt(index), 16777619);
  return `reported:${(hash >>> 0).toString(16)}`;
}

const areaFor = (capability: string) => capability.startsWith('devices.') ? 'devices'
  : capability.startsWith('licenses.') ? 'licenses'
  : capability.startsWith('audit.') ? 'activity'
  : capability.startsWith('workspace.') ? 'workspace'
  : capability.startsWith('pim.') ? 'Your access'
  : 'users';

function safeAction(decision: CapabilityDecision, session: AppSession): WorkspaceIssue['action'] {
  const href = decision.nextStep?.href;
  if (!href || !href.startsWith('/') || href.startsWith('//')) return undefined;
  if (href.startsWith('/settings') && session.workspaceAccess?.canManageSettings !== true) return undefined;
  if (!['/identity', '/settings', '/settings/setup', '/settings/access'].includes(href)) return undefined;
  return { label: href === '/identity' ? 'Open PIM guidance' : 'Open workspace settings', href };
}

export function deriveCapabilityIssues(snapshot: CapabilitySnapshot | null, session: AppSession): WorkspaceIssue[] {
  if (!snapshot || snapshot.workspaceId !== session.workspace.id) return [];
  const grouped = new Map<string, WorkspaceIssue>();
  for (const decision of snapshot.capabilities) {
    if (decision.state === 'allowed' || decision.state === 'hidden' || decision.state === 'disabled') continue;
    const state = decision.state;
    const isReadOnly = state === 'read_only';
    const isConsent = state === 'consent_required';
    const isPim = state.startsWith('pim_');
    const unknown = decision.reasonCode === 'scope_probe_unavailable' || state === 'temporarily_unavailable';
    const area = isReadOnly ? 'Your access' : areaFor(decision.capability);
    const cause = isReadOnly ? 'read_only' : isPim ? state : isConsent ? 'consent' : 'unavailable';
    const key = `${area}:${cause}`;
    if (grouped.has(key)) continue;
    const issue: WorkspaceIssue = {
      key, area, audience: isConsent && session.workspaceAccess?.canManageSettings === true ? 'workspace_admin' : 'affected_user', lastCheckedAt: snapshot.evaluatedAt,
      kind: isConsent ? 'setup' : unknown ? 'service' : 'access',
      severity: isReadOnly ? 'info' : 'warning',
      title: isReadOnly ? 'Read-only access' : isConsent ? 'Microsoft Graph consent required' : isPim ? 'Entra role activation needed' : 'Permission verification unavailable',
      detail: isReadOnly ? 'Your current Entra role limits some actions to reading.'
        : isConsent ? session.workspaceAccess?.canManageSettings === true ? 'Review the workspace connection and delegated consent.' : 'Contact a workspace administrator to review delegated consent.'
        : isPim ? 'Review your PIM eligibility or activation, then refresh your access.'
        : 'Permission verification is unavailable. Try again later.',
    };
    const action = isConsent && session.workspaceAccess?.canManageSettings === true ? { label: 'Open Setup', href: '/settings/setup' } : safeAction(decision, session);
    if (action) issue.action = action;
    grouped.set(key, issue);
  }
  return [...grouped.values()];
}

export function deriveConnectionIssue(health: ConnectionHealth | null, session: AppSession): WorkspaceIssue[] {
  if (!health || health.status === 'connected' || health.status === 'awaiting_invitation') return [];
  const unavailable = health.status === 'temporarily_unavailable' || health.status === 'connection_failed';
  return [{
    key: 'connection:' + health.status, area: 'Connection', kind: unavailable ? 'service' : 'setup', severity: 'warning', audience: session.workspaceAccess?.canManageSettings === true ? 'workspace_admin' : 'affected_user', lastCheckedAt: health.lastVerifiedAt ?? new Date().toISOString(),
    title: unavailable ? 'Connection check unavailable' : health.status === 'permission_incomplete' ? 'Connection permissions incomplete' : 'Workspace consent required',
    detail: unavailable ? 'The workspace connection could not be verified. Try again.' : session.workspaceAccess?.canManageSettings === true ? 'Review the workspace connection.' : 'Contact a workspace administrator to review the connection.',
    ...(session.workspaceAccess?.canManageSettings === true ? { action: { label: 'Open Setup', href: '/settings/setup' } } : {}),
  }];
}

export function sanitizeReportedIssue(issue: WorkspaceIssueReport, session: AppSession): WorkspaceIssue | null {
  if (!/^[a-z0-9:_-]{1,100}$/i.test(issue.key)) return null;
  const area = ['users', 'devices', 'licenses', 'activity', 'services', 'Connection', 'Your access'].includes(issue.area) ? issue.area : 'Workspace';
  const kind = ['setup', 'access', 'service'].includes(issue.kind) ? issue.kind : 'service';
  const title = kind === 'setup' ? 'Setup needs attention' : kind === 'access' ? 'Access needs attention' : 'Data temporarily unavailable';
  const correlationId = typeof issue.correlationId === 'string' && /^[a-zA-Z0-9-]{1,64}$/.test(issue.correlationId) ? issue.correlationId : undefined;
  const href = issue.action?.href;
  const permitted = href === '/settings/setup' ? session.workspaceAccess?.canManageSettings === true : href === '/settings/access' ? session.workspaceAccess?.canManageMembers === true : href === '/identity';
  const detail = (href === '/settings' || href?.startsWith('/settings/')) && !permitted ? 'Contact a workspace administrator to review access.'
    : kind === 'setup' ? session.workspaceAccess?.canManageSettings === true ? 'Review workspace setup and try again.' : 'Contact a workspace administrator to review setup.'
    : kind === 'access' ? 'Review your access and try again.' : 'Try loading this area again.';
  const action = permitted && href ? { label: 'View guidance', href } : undefined;
  return { key: reportedIssueKey(issue.key), area, kind, severity: issue.severity === 'info' ? 'info' : 'warning', title, detail, audience: kind === 'setup' && session.workspaceAccess?.canManageSettings === true ? 'workspace_admin' : 'affected_user', lastCheckedAt: new Date().toISOString(), ...(action ? { action } : {}), ...(correlationId ? { correlationId } : {}) };
}
