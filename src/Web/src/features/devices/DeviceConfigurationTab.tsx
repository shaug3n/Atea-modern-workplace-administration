import { useEffect, useMemo, useRef, useState } from 'react';
import { useApi } from '../../auth/useApi';
import { StatusBadge } from '../../components/StatusBadge';
import { StatusPillFilter } from '../../components/StatusPillFilter';
import { WorkspaceDataState } from '../../components/WorkspaceDataState';
import { formatDateTime } from '../../format/dateTime';
import type { ApiFetch } from '../users/userDetailApi';
import { devicesMessages } from './devicesMessages';
import {
  fetchDeviceConfigurationAssignments,
  fetchDeviceReportedConfiguration,
  type Device360ReadStatus,
  type Device360Response,
  type DeviceConfigurationAssignmentTarget,
  type DeviceConfigurationState,
} from './device360Api';

type ConfigurationView = 'reported' | 'assignment';
type ConfigurationCacheEntry = {
  reported?: Device360Response<DeviceConfigurationState[]>;
  assignments?: Device360Response<DeviceConfigurationAssignmentTarget[]>;
};
type DeviceResponse<T> = { managedDeviceId: string; response: Device360Response<T> };
const PAGE_SIZE = 10;

export function DeviceConfigurationTab({ managedDeviceId, active }: { managedDeviceId: string; active: boolean }) {
  const copy = devicesMessages.deviceConfiguration;
  const api = useApi() as ApiFetch;
  const [view, setView] = useState<ConfigurationView>('reported');
  const [reportedState, setReportedState] = useState<DeviceResponse<DeviceConfigurationState[]> | null>(null);
  const [assignmentsState, setAssignmentsState] = useState<DeviceResponse<DeviceConfigurationAssignmentTarget[]> | null>(null);
  const [reportedLoading, setReportedLoading] = useState(false);
  const [assignmentsLoading, setAssignmentsLoading] = useState(false);
  const [reportedRetry, setReportedRetry] = useState(0);
  const [assignmentsRetry, setAssignmentsRetry] = useState(0);
  const [selectedStates, setSelectedStates] = useState<string[]>([]);
  const [showTechnicalIds, setShowTechnicalIds] = useState(false);
  const [page, setPage] = useState(0);
  const cache = useRef(new Map<string, ConfigurationCacheEntry>());
  const cacheFor = (id: string) => cache.current.get(id) ?? {};
  const reported = reportedState?.managedDeviceId === managedDeviceId ? reportedState.response : null;
  const assignments = assignmentsState?.managedDeviceId === managedDeviceId ? assignmentsState.response : null;

  useEffect(() => {
    if (!active) return;
    const cached = cacheFor(managedDeviceId).reported;
    if (reusable(cached)) {
      setReportedState({ managedDeviceId, response: cached });
      setReportedLoading(false);
      return;
    }
    let current = true;
    setReportedLoading(true);
    void fetchDeviceReportedConfiguration(api, managedDeviceId).then(result => {
      if (!current) return;
      setReportedState({ managedDeviceId, response: result });
      if (reusable(result)) cache.current.set(managedDeviceId, { ...cacheFor(managedDeviceId), reported: result });
    }).finally(() => {
      if (current) setReportedLoading(false);
    });
    return () => { current = false; };
  }, [active, api, managedDeviceId, reportedRetry]);

  useEffect(() => {
    if (!active || view !== 'assignment') return;
    const cached = cacheFor(managedDeviceId).assignments;
    if (reusable(cached)) {
      setAssignmentsState({ managedDeviceId, response: cached });
      setAssignmentsLoading(false);
      return;
    }
    if (!reported?.data?.length || !['succeeded', 'partial'].includes(reported.status)) return;
    let current = true;
    setAssignmentsLoading(true);
    void fetchDeviceConfigurationAssignments(api, managedDeviceId).then(result => {
      if (!current) return;
      setAssignmentsState({ managedDeviceId, response: result });
      if (reusable(result)) cache.current.set(managedDeviceId, { ...cacheFor(managedDeviceId), assignments: result });
    }).finally(() => {
      if (current) setAssignmentsLoading(false);
    });
    return () => { current = false; };
  }, [active, api, assignmentsRetry, managedDeviceId, reported, view]);

  useEffect(() => {
    setView('reported');
    setSelectedStates([]);
    setShowTechnicalIds(false);
    setPage(0);
  }, [managedDeviceId]);

  const reportedRows = Array.isArray(reported?.data) ? reported.data : [];
  const statusOptions = useMemo(() => {
    const counts = new Map<string, number>();
    for (const row of reportedRows) {
      const value = row.state ?? '__unknown__';
      counts.set(value, (counts.get(value) ?? 0) + 1);
    }
    return [...counts].map(([value, count]) => ({
      value,
      label: value === '__unknown__' ? copy.stateUnknown : humanizeState(value),
      count,
      selected: selectedStates.includes(value),
    }));
  }, [reportedRows, selectedStates]);

  const filteredRows = selectedStates.length === 0
    ? reportedRows
    : reportedRows.filter(row => selectedStates.includes(row.state ?? '__unknown__'));
  const pageCount = Math.max(1, Math.ceil(filteredRows.length / PAGE_SIZE));
  const visibleRows = filteredRows.slice(page * PAGE_SIZE, (page + 1) * PAGE_SIZE);
  const retryReported = () => {
    const entry = cacheFor(managedDeviceId);
    delete entry.reported;
    cache.current.set(managedDeviceId, entry);
    setReportedState(null);
    setReportedRetry(value => value + 1);
  };
  const retryAssignments = () => {
    const entry = cacheFor(managedDeviceId);
    delete entry.assignments;
    cache.current.set(managedDeviceId, entry);
    setAssignmentsState(null);
    setAssignmentsRetry(value => value + 1);
  };
  const toggleState = (value: string) => {
    setSelectedStates(current => current.includes(value) ? current.filter(item => item !== value) : [...current, value]);
    setPage(0);
  };

  return <div className="device360-configuration" aria-busy={active && (reportedLoading || assignmentsLoading) ? 'true' : undefined}>
    <div className="device360-section-heading">
      <div>
        <h2>{copy.title}</h2>
        <p>{copy.intro}</p>
      </div>
      <button type="button" className="button button--secondary" onClick={() => setShowTechnicalIds(value => !value)}>
        {showTechnicalIds ? copy.hideTechnicalIds : copy.showTechnicalIds}
      </button>
    </div>
    <div className="device360-view-switch" role="group" aria-label={copy.evidenceLabel}>
      <button type="button" className="button button--secondary" aria-pressed={view === 'reported'} onClick={() => setView('reported')}>{copy.reportedView}</button>
      <button type="button" className="button button--secondary" aria-pressed={view === 'assignment'} onClick={() => setView('assignment')}>{copy.assignmentView}</button>
    </div>
    <WorkspaceDataState kind="partial" compact message={copy.coverageWarning} />
    {view === 'reported'
      ? <section aria-labelledby="device360-configuration-reported">
        <h3 id="device360-configuration-reported">{copy.reportedTitle}</h3>
        {reportedLoading && !reported && <p role="status">{copy.loadingReported}</p>}
        {reported && <ResponseFreshness retrievedAt={reported.retrievedAt} />}
        {reported && <ResponseState response={reported} onRetry={retryReported} section="reported" />}
        {reported && (reported.status === 'succeeded' || reported.status === 'partial') && Array.isArray(reported.data) && <>
          {(reported.status === 'partial' || reported.partialData) && <WorkspaceDataState kind="partial" compact message={copy.reportedPartial} />}
          {reportedRows.length === 0
            ? <WorkspaceDataState kind="empty" compact message={copy.emptyReported} />
            : <>
              <StatusPillFilter label={copy.filterLabel} options={statusOptions} onToggle={toggleState} />
              {filteredRows.length === 0
                ? <WorkspaceDataState kind="empty" compact message={copy.noFilterResults} action={{ label: copy.clearFilters, onClick: () => { setSelectedStates([]); setPage(0); } }} />
                : <div className="device360-table-scroll" role="region" aria-label={copy.reportedTableLabel} tabIndex={0}>
                  <table className="device360-table">
                    <thead><tr><th scope="col">{copy.configurationColumn}</th><th scope="col">{copy.reportedStateColumn}</th><th scope="col">{copy.platformColumn}</th><th scope="col">{copy.settingsColumn}</th><th scope="col">{copy.versionColumn}</th>{showTechnicalIds && <th scope="col">{copy.configurationIdColumn}</th>}</tr></thead>
                    <tbody>{visibleRows.map(row => <tr key={row.id}>
                      <td>{row.displayName || copy.nameUnavailable}</td>
                      <td><StatusBadge tone={stateTone(row.state)} label={row.state ? humanizeState(row.state) : copy.stateUnknown} detail={row.state ?? undefined} /></td>
                      <td>{row.platformType || copy.platformUnavailable}</td>
                      <td>{row.settingCount ?? copy.settingCountUnavailable}</td>
                      <td>{row.version ?? copy.versionUnavailable}</td>
                      {showTechnicalIds && <td>{row.id}</td>}
                    </tr>)}</tbody>
                  </table>
                </div>}
              {filteredRows.length > PAGE_SIZE && <nav className="device360-pagination" aria-label="Reported configuration pages">
                <span>{page * PAGE_SIZE + 1}–{Math.min((page + 1) * PAGE_SIZE, filteredRows.length)} of {filteredRows.length}</span>
                <button type="button" className="button button--secondary" disabled={page === 0} onClick={() => setPage(value => Math.max(0, value - 1))}>Previous page</button>
                <button type="button" className="button button--secondary" disabled={page >= pageCount - 1} onClick={() => setPage(value => Math.min(pageCount - 1, value + 1))}>Next page</button>
              </nav>}
            </>}
        </>}
      </section>
      : <section aria-labelledby="device360-configuration-assignment">
        <h3 id="device360-configuration-assignment">{copy.assignmentsTitle}</h3>
        <p>{copy.assignmentBoundaries}</p>
        {reportedLoading && !reported && <p role="status">{copy.loadingReportedForAssignments}</p>}
        {reported && (reported.status === 'no_reported_policies' || ((reported.status === 'succeeded' || reported.status === 'partial') && !reported.data?.length))
          ? <WorkspaceDataState kind="empty" compact message={copy.noReportedPoliciesAssignment} />
          : reported && !['succeeded', 'partial'].includes(reported.status)
            ? <><ResponseState response={reported} onRetry={retryReported} section="reported" /><WorkspaceDataState kind="unavailable" compact message={copy.assignmentNoPolicies} /></>
            : reported?.data?.length ? <>
              {reported.status === 'partial' || reported.partialData ? <WorkspaceDataState kind="partial" compact message={copy.assignmentPartial} /> : null}
              {assignmentsLoading && !assignments && <p role="status">{copy.loadingAssignments}</p>}
              {assignments && <ResponseFreshness retrievedAt={assignments.retrievedAt} />}
              {assignments && <ResponseState response={assignments} onRetry={retryAssignments} section="assignment" />}
              {assignments && (assignments.status === 'succeeded' || assignments.status === 'partial') && Array.isArray(assignments.data) && <>
                {(assignments.status === 'partial' || assignments.partialData) && <WorkspaceDataState kind="partial" compact message={copy.assignmentsPartial} />}
                {assignments.data.length === 0
                  ? <WorkspaceDataState kind="empty" compact message={copy.noTargets} />
                  : <div className="device360-table-scroll" role="region" aria-label={copy.assignmentTargetsLabel} tabIndex={0}>
                    <table className="device360-table">
                      <thead><tr><th scope="col">{copy.policyColumn}</th><th scope="col">{copy.assignmentColumn}</th><th scope="col">{copy.targetColumn}</th><th scope="col">{copy.filterMetadataColumn}</th>{showTechnicalIds && <th scope="col">{copy.technicalIdsColumn}</th>}</tr></thead>
                      <tbody>{assignments.data.map(target => <tr key={target.assignmentId}>
                        <td>{target.configurationName || copy.nameUnavailable}</td>
                        <td>{humanizeState(target.assignmentKind)}</td>
                        <td>{humanizeState(target.targetType)}{target.groupId && <span> · {copy.groupId} {showTechnicalIds ? target.groupId : copy.hidden}</span>}</td>
                        <td>{target.filterId ? `${target.filterType ? humanizeState(target.filterType) : 'Filter'} · ${showTechnicalIds ? target.filterId : copy.idHidden}` : copy.filterAbsent}</td>
                        {showTechnicalIds && <td>{target.assignmentId} · {target.configurationId}</td>}
                      </tr>)}</tbody>
                    </table>
                  </div>}
              </>}
            </> : null}
      </section>}
  </div>;
}

function ResponseFreshness({ retrievedAt }: { retrievedAt: string | null }) {
  const copy = devicesMessages.deviceConfiguration;
  return <p className="device360-freshness">{copy.sourceTimestamp} {copy.retrievalTime}: {retrievedAt ? formatDateTime(retrievedAt) : copy.retrievalUnavailable}.</p>;
}

function ResponseState({ response, onRetry, section }: { response: Device360Response<unknown>; onRetry: () => void; section: 'reported' | 'assignment' }) {
  const copy = devicesMessages.deviceConfiguration;
  if (response.status === 'succeeded' || response.status === 'partial') {
    if (response.data !== null) return null;
    return <WorkspaceDataState kind="unavailable" compact message={copy.unavailableState} onRetry={onRetry} />;
  }
  if (section === 'reported' && response.status === 'no_reported_policies') {
    return <WorkspaceDataState kind="empty" compact message={copy.noReportedPoliciesAssignment} />;
  }
  return <WorkspaceDataState
    kind={response.status === 'graph_forbidden' || response.status === 'missing_scope' || response.status === 'consent_required' || response.status === 'capability_required' ? 'permission' : 'unavailable'}
    compact
    message={statusMessage(response.status)}
    onRetry={response.status === 'unsupported' ? undefined : onRetry}
  />;
}

function statusMessage(status: Device360ReadStatus) {
  const copy = devicesMessages.deviceConfiguration;
  switch (status) {
    case 'unsupported': return copy.unsupported;
    case 'graph_forbidden': return copy.forbidden;
    case 'missing_scope': return copy.missingScope;
    case 'consent_required': return copy.consentRequired;
    case 'capability_required': return copy.capabilityRequired;
    case 'device_not_found': return copy.deviceNotFound;
    case 'invalid_target': return copy.invalidTarget;
    case 'throttled': return copy.throttled;
    case 'temporarily_unavailable': return copy.temporarilyUnavailable;
    default: return copy.failed;
  }
}

function reusable<T>(response: Device360Response<T> | null | undefined): response is Device360Response<T> {
  return Boolean(response && ['succeeded', 'partial', 'no_reported_policies'].includes(response.status));
}

function humanizeState(value: string) {
  const spaced = value.replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/[_-]+/g, ' ').trim();
  return spaced ? spaced[0].toUpperCase() + spaced.slice(1) : 'Unknown';
}

function stateTone(state: string | null) {
  switch (state?.toLowerCase()) {
    case 'compliant': return 'success';
    case 'noncompliant':
    case 'conflict': return 'warning';
    case 'error': return 'danger';
    case 'pending': return 'info';
    default: return 'neutral';
  }
}
