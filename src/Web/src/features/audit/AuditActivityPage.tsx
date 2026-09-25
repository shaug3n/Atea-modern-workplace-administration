import React, { useCallback, useEffect, useState } from 'react';
import { useApi } from '../../auth/useApi';
import { messages } from '../../app/messages';
import { WorkspacePageHeader } from '../../components/WorkspacePageHeader';
import { WorkspaceDataState } from '../../components/WorkspaceDataState';
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

export function AuditActivityPage({ loadAuditEvents }: { loadAuditEvents?: AuditEventsLoader }) {
  if (loadAuditEvents) {
    return <LoadedAuditActivityPage loadAuditEvents={loadAuditEvents} />;
  }

  return <AuthenticatedAuditActivityPage />;
}

function AuthenticatedAuditActivityPage() {
  const api = useApi();
  const loadAuditEvents = useCallback((filters?: AuditFilters & { continuationToken?: string | null }) => fetchAuditEvents(api, filters), [api]);
  return <LoadedAuditActivityPage loadAuditEvents={loadAuditEvents} />;
}

function LoadedAuditActivityPage({ loadAuditEvents }: { loadAuditEvents: AuditEventsLoader }) {
  const issueReporter = useWorkspaceIssueReporter();
  const [result, setResult] = useState<AuditEventsResponse | null>(null);
  const [filters, setFilters] = useState<AuditFilters>({ actorObjectId: '', action: '', outcome: '' });
  const [continuationToken, setContinuationToken] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  const [retry, setRetry] = useState(0);

  useEffect(() => {
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
  }, [loadAuditEvents, filters, continuationToken, retry, issueReporter]);

  const state = loading ? 'loading' : failed ? 'error' : result && result.items.length === 0 ? 'empty' : 'ready';

  return (
    <section className="audit-page" aria-label="Audit activity page">
      <WorkspacePageHeader eyebrow={messages.auditEyebrow} title={messages.auditTitle} description={messages.auditIntro} />

      <AuditFiltersBar
        filters={filters}
        onChange={(nextFilters) => {
          setFilters(nextFilters);
          setContinuationToken(null);
        }}
      />

      {!loading && !failed && result && (
        <AuditFreshnessBanner
          fetchedAt={result.fetchedAt}
          freshness={result.freshness}
          partialData={result.partialData}
          notice={result.authoritativeSourceNotice}
        />
      )}
      {!loading && !failed && result?.partialData && <p className="workspace-partial-notice" role="status">Partial results. Check Notifications for details.</p>}

      {state === 'loading' && (
        <div className="async-state async-state--loading" role="status" aria-live="polite">
          <span className="async-state__bar" />
          <span className="async-state__bar" />
          <span className="async-state__bar" />
          <span>{messages.auditLoading}</span>
        </div>
      )}
      {state === 'error' && <WorkspaceDataState state="unavailable" message={messages.auditUnavailable} onRetry={() => setRetry(value => value + 1)} />}
      {state === 'empty' && <WorkspaceDataState state="empty" message={messages.auditNoResults} />}
      {state === 'ready' && result && (
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
        {messages.auditActorFilter}
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
          <option value="succeeded">succeeded</option>
          <option value="denied">denied</option>
          <option value="failed">failed</option>
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

function AuditFreshnessBanner({ fetchedAt, freshness, partialData, notice }: { fetchedAt?: string | null; freshness: AuditFreshness; partialData: boolean; notice: string }) {
  const tone = freshness === 'fresh' && !partialData ? 'success' : freshness === 'stale' ? 'warning' : 'danger';
  const label = freshness === 'fresh' && !partialData
    ? messages.auditFresh
    : freshness === 'stale'
      ? messages.auditStale
      : messages.auditDataUnavailable;
  const fetched = fetchedAt ? new Date(fetchedAt).toLocaleString() : messages.auditNeverFetched;

  return (
    <div className="data-freshness" data-tone={tone} role={tone === 'success' ? 'status' : 'alert'}>
      <span>{label}</span>
      <span>{messages.auditFetchedAt}: {fetched}</span>
      <span>{notice}</span>
    </div>
  );
}

function AuditEventsTable({ events }: { events: AuditEvent[] }) {
  return (
    <ResponsiveDataView items={events} keyOf={event => event.id} label="Audit activity" renderCompact={event => <>
      <strong>{event.action}</strong><span>{formatDate(event.timestamp)} · {event.outcome}</span>
      <span>{event.targetId || event.targetType}</span><code className="audit-reference">{correlationText(event)}</code>
    </>} renderTable={() => <div className="audit-table-wrap">
      <table className="audit-table" aria-label={messages.auditTableLabel}>
        <thead>
          <tr>
            <th scope="col">{messages.auditTimeColumn}</th>
            <th scope="col">{messages.auditActionColumn}</th>
            <th scope="col">{messages.auditTargetColumn}</th>
            <th scope="col">{messages.auditOutcomeColumn}</th>
            <th scope="col">{messages.auditCorrelationColumn}</th>
            <th scope="col">{messages.auditMetadataColumn}</th>
          </tr>
        </thead>
        <tbody>
          {events.map((event) => (
            <tr key={event.id}>
              <td data-label={messages.auditTimeColumn}>{formatDate(event.timestamp)}</td>
              <td data-label={messages.auditActionColumn}>{event.action}</td>
              <td data-label={messages.auditTargetColumn}>{event.targetId || event.targetType}</td>
              <td data-label={messages.auditOutcomeColumn}>{event.failureCategory ? `${event.outcome} (${event.failureCategory})` : event.outcome}</td>
              <td data-label={messages.auditCorrelationColumn}>{correlationText(event)}</td>
              <td data-label={messages.auditMetadataColumn}><code>{event.safeMetadataJson || '{}'}</code></td>
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

function formatDate(value: string) {
  return Number.isNaN(Date.parse(value)) ? value : new Date(value).toLocaleString();
}
