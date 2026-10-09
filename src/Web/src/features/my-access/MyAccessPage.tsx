import React from 'react';
import { DataFreshness } from '../../components/DataFreshness';
import { StatusBadge, type StatusTone } from '../../components/StatusBadge';
import { TechnicalDetails } from '../../components/TechnicalDetails';
import { WorkspaceDataState } from '../../components/WorkspaceDataState';
import { WorkspacePageHeader } from '../../components/WorkspacePageHeader';
import { isPimCapabilityState, type CapabilityDecision } from '../../capabilities/capabilityTypes';
import { humanizeCapability } from '../../format/humanize';
import { formatDateTime } from '../../format/dateTime';
import type { AppSession } from '../../components/TenantContextHeader';
import { useAccessTransparency } from './accessContext';
import { summarizeAccess, type AccessActionSummary, type AccessSummaryState } from './accessSummary';
import './myAccessPage.css';

const pimPortalUrl = 'https://entra.microsoft.com/#view/Microsoft_Azure_PIMCommon/ActivationMenuBlade';

const stateLabels: Record<AccessSummaryState, { label: string; tone: StatusTone }> = {
  allowed: { label: 'Allowed', tone: 'success' },
  read_only: { label: 'Read only', tone: 'info' },
  hidden: { label: 'Not available', tone: 'neutral' },
  disabled: { label: 'Disabled', tone: 'warning' },
  consent_required: { label: 'Microsoft consent required', tone: 'warning' },
  pim_activation_required: { label: 'PIM activation required', tone: 'warning' },
  pim_approval_required: { label: 'PIM approval required', tone: 'warning' },
  pim_mfa_required: { label: 'PIM MFA required', tone: 'warning' },
  pim_eligibility_expired: { label: 'PIM eligibility expired', tone: 'warning' },
  temporarily_unavailable: { label: 'Temporarily unavailable', tone: 'danger' },
  mixed: { label: 'Mixed results', tone: 'warning' },
  partial: { label: 'Partial evidence', tone: 'warning' },
  unavailable: { label: 'Unavailable', tone: 'danger' },
  workspace_not_granted: { label: 'Workspace access not granted', tone: 'warning' },
  module_disabled: { label: 'Module disabled', tone: 'neutral' },
  not_applicable: { label: 'Not applicable', tone: 'neutral' },
};

const roleNames: Record<string, string> = {
  '62e90394-69f5-4237-9190-012177145e10': 'Global Administrator',
  'f2ef992c-3afb-46b9-b7cf-a126ee74c451': 'Global Reader',
  'fe930be7-5e62-47db-91af-98c3a49a38b1': 'User Administrator',
  'fdd7a751-b60b-444a-984c-02652fe8fa1c': 'Groups Administrator',
  '4d6ac14f-3453-41d0-bef9-a3e0c569773a': 'License Administrator',
  'e8611ab8-c189-46e8-94e1-60213ab1f814': 'Privileged Role Administrator',
  'c4e39bd9-1100-46d3-8c65-fb160da0071f': 'Authentication Administrator',
  '7be44c8a-adaf-4e2a-84d6-ab2649e08a13': 'Privileged Authentication Administrator',
  '7698a772-787b-4ac8-901f-60d6b08affd2': 'Cloud Device Administrator',
  '3a2c62db-5318-420d-8d74-23affee5d9d5': 'Intune Administrator',
  '729827e3-9c14-49f7-bb1b-9608f156bbb8': 'Helpdesk Administrator',
  '194ae4cb-b126-40b2-bd5b-6091b380977d': 'Security Administrator',
  '5d6b6bb7-de71-4623-b4af-96380a352509': 'Security Reader',
  '4a5d8f65-41da-4de4-8968-e035b65339cf': 'Reports Reader',
};

const pimStateLabels: Record<string, string> = {
  pim_activation_required: 'PIM activation is required before this action can run.',
  pim_approval_required: 'PIM approval is required before this action can run.',
  pim_mfa_required: 'Complete the required MFA step for PIM before continuing.',
  pim_eligibility_expired: 'The API reported that PIM eligibility has expired.',
};

function roleLabel(roleId: string): string {
  return roleNames[roleId] ?? 'Unrecognized Entra role';
}

function statusFor(state: AccessSummaryState) {
  return stateLabels[state] ?? stateLabels.unavailable;
}

function isWorkspaceDecision(action: AccessActionSummary): boolean {
  return action.decision?.reasonCode.startsWith('workspace_platform_') ?? false;
}

function displayedActionState(action: AccessActionSummary, sourceAvailable: boolean): AccessSummaryState {
  return !sourceAvailable && action.decision && !isWorkspaceDecision(action)
    ? 'unavailable'
    : action.state;
}

function apiDecisionLabel(decision: CapabilityDecision): string {
  if (decision.state === 'allowed') return 'The API explicitly allowed this action.';
  if (decision.state === 'read_only') return 'The API reported read-only access for this action.';
  if (decision.state === 'hidden') return 'The API reported this action as hidden.';
  if (decision.state === 'disabled') return 'The API reported this action as disabled.';
  if (decision.state.startsWith('pim_')) return pimStateLabels[decision.state] ?? 'The API reported a PIM restriction.';
  if (decision.state === 'temporarily_unavailable') return 'Microsoft authorization could not be verified for this action.';
  if (decision.state === 'consent_required') return 'The API reported that Microsoft consent is required for this action.';
  return 'The API reported that this action is unavailable.';
}

function supportedDestination(action: AccessActionSummary, canManageSettings: boolean): { href: string; external: boolean; label: string } | null {
  const decision = action.decision;
  if (!decision) return null;

  if (decision.state === 'consent_required') {
    if (canManageSettings && (decision.nextStep?.href === '/onboarding' || decision.nextStep?.href === '/api/workspaces/current/consent/start')) {
      return { href: '/settings#connection', external: false, label: decision.nextStep.label || 'Review Microsoft permission setup' };
    }
    return null;
  }

  if (isPimCapabilityState(decision.state)) {
    const roleId = decision.requiredRoleTemplateId;
    if (!roleId) return null;
    if (decision.nextStep?.href === '/identity') {
      return { href: '/identity', external: false, label: 'Open PIM guidance' };
    }
    if (decision.nextStep?.href === pimPortalUrl || decision.pim?.activationUrl === pimPortalUrl) {
      return { href: pimPortalUrl, external: true, label: 'Open Microsoft Entra PIM' };
    }
    return null;
  }

  if (decision.nextStep?.href === '/workspace-access') {
    return { href: '/workspace-access', external: false, label: decision.nextStep.label || 'Review workspace access' };
  }
  return null;
}

function InternalLink({
  href,
  onNavigate,
  children,
}: {
  href: string;
  onNavigate?: (path: string) => void;
  children: React.ReactNode;
}) {
  return <a href={href} onClick={onNavigate ? event => { event.preventDefault(); onNavigate(href); } : undefined}>{children}</a>;
}

function roleAndPimEvidence(action: AccessActionSummary) {
  const decision = action.decision;
  if (!decision) {
    return (
      <div className="my-access-evidence__layer">
        <dt>Entra role and PIM evidence</dt>
        <dd>Role and PIM evidence was not provided because the API returned no action decision.</dd>
      </div>
    );
  }
  const evidence = decision.roleEvidence;
  const requiredRoleId = decision.requiredRoleTemplateId ?? null;
  const qualifyingRoleIds = [...new Set(evidence?.requiredRoleTemplateIds ?? [])];
  const roleAssignments = evidence?.state === 'available' ? evidence.assignments : [];
  const unknownRoles = [...new Set([
    ...(requiredRoleId && !roleNames[requiredRoleId] ? [requiredRoleId] : []),
    ...qualifyingRoleIds.filter(roleId => !roleNames[roleId]),
    ...roleAssignments.map(assignment => assignment.roleTemplateId).filter(roleId => !roleNames[roleId]),
  ])];

  return (
    <div className="my-access-evidence__layer">
      <dt>Entra role and PIM evidence</dt>
      <dd>
        <div className="my-access-evidence__role-details">
          {requiredRoleId && <p><strong>PIM role requirement:</strong> {roleLabel(requiredRoleId)}. This specific requirement is not evidence that the role is assigned to you.</p>}
          {qualifyingRoleIds.length > 0 && (
            <div>
              <p><strong>Qualifying role alternatives reported by the API:</strong> These alternatives are not all required and do not confirm any role is assigned to you.</p>
              <ul className="my-access-evidence__assignments">
                {qualifyingRoleIds.map(roleId => <li key={roleId}>{roleLabel(roleId)}</li>)}
              </ul>
            </div>
          )}
          {evidence?.state === 'unavailable' && <p>Role assignment evidence is unavailable.</p>}
          {evidence?.state && evidence.state !== 'available' && evidence.state !== 'not_applicable' && evidence.state !== 'unavailable'
            && <p>Role evidence state reported by the API: {evidence.state}. It is not treated as confirmation of an active role.</p>}
          {evidence?.state === 'available' && roleAssignments.length === 0 && <p>The API returned no relevant role assignments for this action.</p>}
          {roleAssignments.length > 0 && (
            <ul className="my-access-evidence__assignments">
              {roleAssignments.map((assignment, index) => (
                <li key={`${assignment.roleTemplateId}-${assignment.assignmentState}-${assignment.scope}-${index}`}>
                  <span>{roleLabel(assignment.roleTemplateId)} — {assignment.assignmentState === 'active' ? 'Active assignment' : assignment.assignmentState === 'eligible' ? 'Eligible assignment' : `Assignment state: ${assignment.assignmentState}`}</span>
                  <span>{assignment.scope === 'tenant' || assignment.scope === 'tenant_wide'
                    ? 'Scope reported: tenant-wide'
                    : assignment.scope.startsWith('/administrativeUnits/')
                      ? `Scope reported: administrative unit (${assignment.scope})`
                      : `Scope reported: ${assignment.scope}`}</span>
                  {assignment.pimState && <span>PIM state reported: {assignment.pimState}</span>}
                </li>
              ))}
            </ul>
          )}
          {roleAssignments.some(assignment => assignment.assignmentState === 'eligible')
            && <p>PIM eligibility is not an active role assignment; activation may still be required.</p>}
          {evidence?.state === 'not_applicable' && !requiredRoleId && qualifyingRoleIds.length === 0 && <p>The API did not report a role requirement for this action.</p>}
          {evidence?.state === 'not_applicable' && requiredRoleId && <p>The API did not provide active assignment evidence for the required role.</p>}
          {!evidence && <p>Active role assignment evidence was not provided.</p>}
          {decision.pim?.state && <p>PIM state reported by the API: {decision.pim.state}</p>}
          {unknownRoles.length > 0 && <TechnicalDetails items={unknownRoles.map((roleId, index) => ({ label: `Role template ID ${index + 1}`, value: roleId }))} />}
        </div>
      </dd>
    </div>
  );
}

function ActionEvidence({
  action,
  sourceAvailable,
  canManageSettings,
  onNavigate,
}: {
  action: AccessActionSummary;
  sourceAvailable: boolean;
  canManageSettings: boolean;
  onNavigate?: (path: string) => void;
}) {
  const decision = action.decision;
  const workspaceDecision = isWorkspaceDecision(action);
  const graphEvidenceUnavailable = !sourceAvailable && !workspaceDecision;
  const status = statusFor(displayedActionState(action, sourceAvailable));
  const destination = supportedDestination(action, canManageSettings);

  const reason = graphEvidenceUnavailable && decision
    ? `${action.reasonLabel} The API returned ${decision.state} (${decision.reasonCode}), but Microsoft authorization evidence is unavailable; this result is not verified.`
    : action.reasonLabel;
  return (
    <li className="my-access-action">
      <div className="my-access-action__heading">
        <h4>{humanizeCapability(action.capability)}</h4>
        <StatusBadge tone={status.tone} label={status.label} />
      </div>
      <p className="my-access-action__reason">{reason}</p>
      {decision && <p>{graphEvidenceUnavailable
        ? 'The returned Microsoft decision cannot be treated as verified authorization.'
        : apiDecisionLabel(decision)}</p>}
      <dl className="my-access-evidence">
        <div className="my-access-evidence__layer">
          <dt>Workspace grant</dt>
          <dd>{action.state === 'workspace_not_granted'
            ? 'The API reports that this module is not granted in the workspace.'
            : action.state === 'module_disabled'
              ? 'The API reports that this module is disabled in the workspace.'
              : action.state === 'unavailable' && !decision && sourceAvailable
                ? 'Workspace module grant evidence was not returned.'
                : action.state === 'unavailable' && !sourceAvailable
                ? 'Workspace module grant evidence is shown above when available; Microsoft authorization evidence could not be verified.'
                  : decision ? 'The API evaluated this action; see the module-level workspace grant evidence above when available.' : 'No action-level result was returned; the module-level workspace gate is shown above when available.'}</dd>
        </div>
        <div className="my-access-evidence__layer">
          <dt>Microsoft authorization and consent</dt>
          <dd>{workspaceDecision
            ? 'The API evaluated this action using workspace authorization evidence; Microsoft authorization was not reported separately for this decision.'
            : !sourceAvailable
              ? decision?.state === 'consent_required' && action.consentEvidence
                ? action.consentEvidence
                : 'Microsoft authorization evidence is unavailable.'
              : !decision
                ? 'Microsoft authorization and consent evidence are unavailable because no action-level decision was returned.'
                : action.consentEvidence ?? (decision.state === 'consent_required'
              ? 'Consent details were not reported separately.'
              : `API reason: ${decision.reasonCode.replace(/_/g, ' ')}.`)}</dd>
          {decision?.missingScopes?.length ? <dd>Missing scopes: <code>{decision.missingScopes.join(', ')}</code></dd> : null}
        </div>
        {roleAndPimEvidence(action)}
      </dl>
      {decision?.state === 'consent_required' && <p className="my-access-action__next-step">Microsoft consent must be reviewed through the workspace’s existing setup. If you cannot manage workspace settings, contact your workspace administrator.</p>}
      {destination && (destination.external
        ? <p className="my-access-action__next-step"><a href={destination.href} target="_blank" rel="noreferrer">{destination.label}<span className="my-access-sr-only"> (opens in a new tab)</span></a></p>
        : <p className="my-access-action__next-step"><InternalLink href={destination.href} onNavigate={onNavigate}>{destination.label}</InternalLink></p>)}
      {graphEvidenceUnavailable && <p className="my-access-action__unavailable">Microsoft authorization evidence is unavailable; this action is not shown as allowed or denied.</p>}
    </li>
  );
}

function AccessGroup({
  moduleKey,
  moduleLabel,
  groupType,
  state,
  actions,
  sourceAvailable,
  canManageSettings,
  onNavigate,
}: {
  moduleKey: string;
  moduleLabel: string;
  groupType: 'read' | 'write';
  state: AccessSummaryState;
  actions: AccessActionSummary[];
  sourceAvailable: boolean;
  canManageSettings: boolean;
  onNavigate?: (path: string) => void;
}) {
  const graphDecisionUnavailable = !sourceAvailable && actions.some(action => action.decision && !isWorkspaceDecision(action));
  const displayedState = graphDecisionUnavailable
    ? state === 'partial' || actions.some(action => action.state === 'partial')
      ? 'partial'
      : new Set(actions.map(action => displayedActionState(action, sourceAvailable))).size > 1
        ? 'mixed'
        : displayedActionState(actions[0], sourceAvailable)
    : state;
  const status = statusFor(displayedState);
  const label = groupType === 'read' ? 'Read access' : 'Write access';
  const headingId = `my-access-${moduleKey}-${groupType}`;
  return (
    <section className="my-access-group" role="group" aria-labelledby={headingId} aria-label={`${moduleLabel} ${groupType} access`}>
      <div className="my-access-group__heading">
        <h3 id={headingId}>{label}</h3>
        <StatusBadge tone={status.tone} label={status.label} />
      </div>
      {actions.length > 0
        ? <ul className="my-access-actions">{actions.map(action => <ActionEvidence key={action.capability} action={action} sourceAvailable={sourceAvailable} canManageSettings={canManageSettings} onNavigate={onNavigate} />)}</ul>
        : <p>{state === 'not_applicable'
          ? 'The API does not currently evaluate actions in this group.'
          : 'Action coverage is not available; no access summary can be made.'}</p>}
    </section>
  );
}

export function MyAccessPage({ session, onNavigate }: { session: AppSession; onNavigate?: (path: string) => void }) {
  const { snapshot, loading, error, refresh } = useAccessTransparency();
  const snapshotMatchesWorkspace = snapshot?.workspaceId === session.workspace.id;
  const scopedSnapshot = snapshotMatchesWorkspace ? snapshot : null;
  const summary = summarizeAccess(scopedSnapshot, session);
  const stale = Boolean(scopedSnapshot && (loading || error));
  const sourceAvailable = summary.sourceState === 'graph_authoritative';
  const canManageSettings = session.workspaceAccess?.canManageSettings === true;
  const workspaceEvidence = scopedSnapshot?.workspaceModules ?? null;

  return (
    <section className="my-access-page" aria-label="My access evidence" aria-busy={loading ? 'true' : undefined}>
      <WorkspacePageHeader
        eyebrow="Access transparency"
        title="My access"
        description={`Review how workspace grants and Microsoft permissions affect actions in ${session.workspace.name}. The API evaluates and enforces each action.`}
        meta={scopedSnapshot
          ? <DataFreshness
            fetchedAt={summary.evaluatedAt}
            freshness={stale ? 'stale' : sourceAvailable ? 'fresh' : 'unavailable'}
            partialData={!sourceAvailable}
            source={summary.sourceState ? `Access API · ${summary.sourceState.replace(/_/g, ' ')}` : 'Access API'}
            labels={{ stale: 'Previous access check', unavailable: 'Evidence unavailable' }}
          />
          : undefined}
        actions={<button type="button" className="button button--secondary" onClick={() => void refresh()} disabled={loading}>{loading ? 'Refreshing…' : 'Refresh access'}</button>}
      />

      {stale && <WorkspaceDataState kind="stale" compact message="Showing a previous access check. It is not current while refresh is in progress or after a refresh failure." />}

      {!scopedSnapshot && loading && (
        <WorkspaceDataState state="loading" message="Loading access information for this workspace…" />
      )}
      {!scopedSnapshot && !loading && (
        <WorkspaceDataState
          state="unavailable"
          title="Access evidence is unavailable"
          message={error
            ? 'Access information could not be loaded. No snapshot is available for this workspace; retry to check again.'
            : 'No access snapshot is available for this workspace. A check from another workspace cannot be shown here.'}
          onRetry={() => void refresh()}
        />
      )}
      {scopedSnapshot && (
        <>
          <section className="my-access-context content-panel" aria-labelledby="my-access-workspace-heading">
            <h2 id="my-access-workspace-heading">Current workspace</h2>
            <p><strong>{session.workspace.name}</strong></p>
            <p>Snapshot workspace ID: <code>{summary.workspaceId}</code></p>
            <p>Evaluation time: {summary.evaluatedAt ? formatDateTime(summary.evaluatedAt) : 'Not provided by the API'}</p>
            <p>Evidence source state: {summary.sourceState ?? 'Not provided by the API'}</p>
            <p>Workspace grants and Microsoft authorization are separate checks. An allowed result applies only to the action explicitly evaluated by the API.</p>
          </section>
          {!sourceAvailable && <WorkspaceDataState kind="partial" title="Microsoft authorization evidence is unavailable" message="Workspace module grant evidence remains separate. Microsoft action and role evidence is not treated as empty, denied, or allowed." />}
          <section className="my-access-modules" aria-label="Access by module">
            {summary.modules.map(module => {
              const moduleGrant = workspaceEvidence?.find(item => item.module === module.key);
              const hasModuleGate = module.key !== 'workspace-administration';
              return (
                <article className="my-access-module content-panel" key={module.key} aria-labelledby={`my-access-module-${module.key}`}>
                  <header className="my-access-module__heading">
                    <div>
                      <h2 id={`my-access-module-${module.key}`}>{module.label}</h2>
                      <p>{hasModuleGate
                        ? moduleGrant
                          ? `Workspace grant: ${moduleGrant.enabled ? moduleGrant.effective ? 'granted' : 'not granted' : 'module disabled'} (${moduleGrant.grantSource.replace(/_/g, ' ')}).`
                          : 'Workspace grant: evidence unavailable.'
                        : 'Workspace grant: no separate module gate is defined for these actions.'}</p>
                    </div>
                    {hasModuleGate && moduleGrant && <StatusBadge
                      tone={moduleGrant.enabled && moduleGrant.effective ? 'success' : 'warning'}
                      label={moduleGrant.enabled ? moduleGrant.effective ? 'Workspace grant shown' : 'Workspace grant missing' : 'Module disabled'}
                    />}
                  </header>
                  <div className="my-access-module__groups">
                    <AccessGroup moduleKey={module.key} moduleLabel={module.label} groupType="read" state={module.read.state} actions={module.read.actions} sourceAvailable={sourceAvailable} canManageSettings={canManageSettings} onNavigate={onNavigate} />
                    <AccessGroup moduleKey={module.key} moduleLabel={module.label} groupType="write" state={module.write.state} actions={module.write.actions} sourceAvailable={sourceAvailable} canManageSettings={canManageSettings} onNavigate={onNavigate} />
                  </div>
                </article>
              );
            })}
          </section>
        </>
      )}
    </section>
  );
}
