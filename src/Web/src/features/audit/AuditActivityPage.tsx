import React, { useCallback, useEffect, useState } from 'react';
import { useApi } from '../../auth/useApi';
import { messages } from '../../app/messages';
import { WorkspacePageHeader } from '../../components/WorkspacePageHeader';
import { WorkspaceDataState } from '../../components/WorkspaceDataState';
import { DataFreshness } from '../../components/DataFreshness';
import { DateTime } from '../../components/DateTime';
import { StatusBadge } from '../../components/StatusBadge';
import { TechnicalDetails } from '../../components/TechnicalDetails';
import { humanizeActivityAction } from '../../format/humanize';
import { ResponsiveDataView } from '../../components/ResponsiveDataView';
import { useWorkspaceIssueReporter } from '../../notifications/WorkspaceNotifications';

export type AuditFreshness = 'fresh' | 'stale' | 'unavailable';

export type AuditEvent = {
  id: string;
  workspaceId: string;
  tenantId: string;
  actorTenantId: string;
  actorObjectId: string;
  action: string;
  targetType: string;
  targetId: string;
  outcome: string;
  timestamp: string;
  correlationId: string | null;
  graphCorrelationId: string | null;
  graphRequestId: string | null;
  pimRequestId: string | null;
  failureCategory: string | null;
  safeMetadataJson: string;
};

export type AuditEventsResponse = {
  items: AuditEvent[];
  fetchedAt: string;
  freshness: AuditFreshness;
  partialData: boolean;
  authoritativeSourceNotice: string;
  nextContinuationToken?: string | null;
};

export type AuditFilters = {
  actorObjectId: string;
  action: string;
  outcome: string;
};

export type AuditEventsLoader = (filters?: AuditFilters & { continuationToken?: string | null }) => Promise<AuditEventsResponse>;

export function AuditActivityPage({ loadAuditEvents, authorizationUnavailable = false, onAuthorizationRetry }: { loadAuditEvents?: AuditEventsLoader; authorizationUnavailable?: boolean; onAuthorizationRetry?: () => Promise<void> }) {
  if (loadAuditEvents) {
    return <LoadedAuditActivityPage loadAuditEvents={loadAuditEvents} authorizationUnavailable={authorizationUnavailable} onAuthorizationRetry={onAuthorizationRetry} />;
  }

  return <AuthenticatedAuditActivityPage authorizationUnavailable={authorizationUnavailable} onAuthorizationRetry={onAuthorizationRetry} />;
}

function AuthenticatedAuditActivityPage({ authorizationUnavailable, onAuthorizationRetry }: { authorizationUnavailable: boolean; onAuthorizationRetry?: () => Promise<void> }) {
  const api = useApi();
  const loadAuditEvents = useCallback((filters?: AuditFilters & { continuationToken?: string | null }) => fetchAuditEvents(api, filters), [api]);
  return <LoadedAuditActivityPage loadAuditEvents={loadAuditEvents} authorizationUnavailable={authorizationUnavailable} onAuthorizationRetry={onAuthorizationRetry} />;
}

function LoadedAuditActivityPage({ loadAuditEvents, authorizationUnavailable, onAuthorizationRetry }: { loadAuditEvents: AuditEventsLoader; authorizationUnavailable: boolean; onAuthorizationRetry?: () => Promise<void> }) {
  const issueReporter = useWorkspaceIssueReporter();
  const [result, setResult] = useState<AuditEventsResponse | null>(null);
  const [filters, setFilters] = useState<AuditFilters>({ actorObjectId: '', action: '', outcome: '' });
  const [continuationToken, setContinuationToken] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  const [retry, setRetry] = useState(0);

  useEffect(() => {
    if (authorizationUnavailable) return;
    let cancelled = false;
    setLoading(true);
    setFailed(false);
    loadAuditEvents({ ...filters, continuationToken })
      .then((response) => {
        if (!cancelled) {
          setResult(response);
          if (response.partialData || response.freshness === 'unavailable') issueReporter.report({ key: 'activity:read', area: 'activity', kind: 'service', severity: 'warning', title: 'Activity data unavailable', detail: 'Try loading activity again.' });
          else issueReporter.clear('activity:read');
        }
      })
      .catch(() => {
        if (!cancelled) { setFailed(true); issueReporter.report({ key: 'activity:read', area: 'activity', kind: 'service', severity: 'warning', title: 'Activity data unavailable', detail: 'Try loading activity again.' }); }
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
    });
    return () => { cancelled = true; };
  }, [loadAuditEvents, filters, continuationToken, retry, issueReporter, authorizationUnavailable]);

  const state = loading ? 'loading' : failed ? 'error' : result && result.items.length === 0 ? 'empty' : 'ready';

  return (
    <section className="audit-page" aria-label="Audit activity page">
      <WorkspacePageHeader eyebrow={messages.auditEyebrow} title={messages.auditTitle} description={messages.auditIntro}
        meta={!authorizationUnavailable && !loading && !failed && result ? <DataFreshness fetchedAt={result.fetchedAt} freshness={result.freshness} partialData={result.partialData} source="Atea Unified Workplace" onRefresh={() => setRetry(value => value + 1)} /> : undefined} />
      <p className="audit-note" role="note">Shows actions taken in Atea Unified Workplace. For a complete record, use Microsoft 365 audit logs.</p>

      <AuditFiltersBar
        filters={filters}
        onChange={(nextFilters) => {
          setFilters(nextFilters);
          setContinuationToken(null);
        }}
      />

      {!authorizationUnavailable && !loading && !failed && result?.partialData && <p className="workspace-partial-notice" role="status">Partial results. Check Notifications for details.</p>}

      {authorizationUnavailable && <WorkspaceDataState state="unavailable" message="Data cannot be shown right now. Check Notifications for details." onRetry={onAuthorizationRetry ? () => void onAuthorizationRetry() : undefined} />}
      {!authorizationUnavailable && state === 'loading' && (
        <div className="async-state async-state--loading" role="status" aria-live="polite">
          <span className="async-state__bar" />
          <span className="async-state__bar" />
          <span className="async-state__bar" />
          <span>{messages.auditLoading}</span>
        </div>
      )}
      {!authorizationUnavailable && state === 'error' && <WorkspaceDataState state="unavailable" message={messages.auditUnavailable} onRetry={() => setRetry(value => value + 1)} />}
      {!authorizationUnavailable && state === 'empty' && <WorkspaceDataState state="empty" message={messages.auditNoResults} />}
      {!authorizationUnavailable && state === 'ready' && result && (
        <>
          <AuditEventsTable events={result.items} />
          {result.nextContinuationToken && (
            <button type="button" className="secondary-button audit-next" onClick={() => setContinuationToken(result.nextContinuationToken ?? null)}>
              {messages.auditNextPage}
            </button>
          )}
        </>
      )}
    </section>
  );
}

function AuditFiltersBar({ filters, onChange }: { filters: AuditFilters; onChange: (filters: AuditFilters) => void }) {
  return (
    <form className="audit-filters" onSubmit={(event) => event.preventDefault()}>
      <label>
        Person
        <input
          value={filters.actorObjectId}
          placeholder={messages.auditActorPlaceholder}
          onChange={(event) => onChange({ ...filters, actorObjectId: event.target.value })}
        />
      </label>
      <label>
        {messages.auditActionFilter}
        <input
          value={filters.action}
          placeholder={messages.auditActionPlaceholder}
          onChange={(event) => onChange({ ...filters, action: event.target.value })}
        />
      </label>
      <label>
        {messages.auditOutcomeFilter}
        <select value={filters.outcome} onChange={(event) => onChange({ ...filters, outcome: event.target.value })}>
          <option value="">{messages.auditAnyOutcome}</option>
          <option value="succeeded">Succeeded</option>
          <option value="denied">Denied</option>
          <option value="failed">Failed</option>
        </select>
      </label>
    </form>
  );
}

async function fetchAuditEvents(api: (path: string, init?: RequestInit) => Promise<Response>, filters?: AuditFilters & { continuationToken?: string | null }) {
  const query = new URLSearchParams({ pageSize: '25' });
  if (filters?.actorObjectId) query.set('actorObjectId', filters.actorObjectId);
  if (filters?.action) query.set('action', filters.action);
  if (filters?.outcome) query.set('outcome', filters.outcome);
  if (filters?.continuationToken) query.set('continuationToken', filters.continuationToken);

  const response = await api(`/api/audit/events?${query.toString()}`);
  if (!response.ok) {
    throw new Error('audit_activity_unavailable');
  }

  return await response.json() as AuditEventsResponse;
}

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function targetSummary(event: AuditEvent) {
  const id = event.targetId?.trim();
  if (id && !GUID.test(id)) return id;
  return `Unnamed ${event.targetType || 'item'}`;
}

function outcomeBadge(event: AuditEvent) {
  const outcome = event.outcome.toLowerCase();
  const tone = outcome === 'succeeded' ? 'success' : outcome === 'denied' ? 'warning' : outcome === 'failed' ? 'danger' : 'neutral';
  return <StatusBadge tone={tone} label={outcome ? outcome.charAt(0).toUpperCase() + outcome.slice(1) : 'Unknown'} />;
}

function eventDetails(event: AuditEvent) {
  return <TechnicalDetails summary="Details" items={[
    { label: 'Correlation ID', value: correlationText(event) === messages.auditNoCorrelation ? null : correlationText(event) },
    { label: 'Actor object ID', value: event.actorObjectId },
    { label: 'Target ID', value: event.targetId },
    { label: 'Failure category', value: event.failureCategory },
    { label: messages.auditMetadataColumn, value: event.safeMetadataJson && event.safeMetadataJson !== '{}' ? event.safeMetadataJson : null },
  ]} />;
}

function AuditEventsTable({ events }: { events: AuditEvent[] }) {
  return (
    <ResponsiveDataView items={events} keyOf={event => event.id} label="Audit activity" renderCompact={event => <>
      <strong>{humanizeActivityAction(event.action)}</strong>
      <span><DateTime value={event.timestamp} relative /> · {outcomeBadge(event)}</span>
      <span>{targetSummary(event)}</span>
      {eventDetails(event)}
    </>} renderTable={() => <div className="audit-table-wrap">
      <table className="audit-table" aria-label={messages.auditTableLabel}>
        <thead>
          <tr>
            <th scope="col">When</th>
            <th scope="col">Person</th>
            <th scope="col">{messages.auditActionColumn}</th>
            <th scope="col">{messages.auditTargetColumn}</th>
            <th scope="col">Result</th>
            <th scope="col"><span className="sr-only">Details</span></th>
          </tr>
        </thead>
        <tbody>
          {events.map((event) => (
            <tr key={event.id}>
              <td data-label="When"><DateTime value={event.timestamp} relative /></td>
              <td data-label="Person">Workspace user</td>
              <td data-label={messages.auditActionColumn}>{humanizeActivityAction(event.action)}</td>
              <td data-label={messages.auditTargetColumn}>{targetSummary(event)}</td>
              <td data-label="Result">{outcomeBadge(event)}</td>
              <td data-label="Details">{eventDetails(event)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>} />
  );
}

function correlationText(event: AuditEvent) {
  return [event.correlationId, event.graphCorrelationId, event.graphRequestId, event.pimRequestId].filter(Boolean).join(' / ') || messages.auditNoCorrelation;
}
