import { useEffect, useRef, useState } from 'react';
import { useApi } from '../../auth/useApi';
import { WorkspaceDataState } from '../../components/WorkspaceDataState';
import { formatDateTime } from '../../format/dateTime';
import type { ApiFetch } from '../users/userDetailApi';
import { devicesMessages } from './devicesMessages';
import { fetchDeviceApps, type Device360ReadStatus, type Device360Response, type DeviceDetectedApp } from './device360Api';

type DeviceAppsResponse = { managedDeviceId: string; response: Device360Response<DeviceDetectedApp[]> };
const dnAttribute = String.raw`(?:[A-Za-z][A-Za-z0-9-]*|[0-9]+(?:\.[0-9]+)+)`;
const dnValue = String.raw`(?:\\.|[^,])+`;
const distinguishedNamePattern = new RegExp(
  `^\\s*${dnAttribute}\\s*=\\s*${dnValue}(?:\\s*,\\s*${dnAttribute}\\s*=\\s*${dnValue})+\\s*$`,
  'i',
);

export function DeviceAppsTab({ managedDeviceId, active }: { managedDeviceId: string; active: boolean }) {
  const copy = devicesMessages.deviceApps;
  const api = useApi() as ApiFetch;
  const [resultState, setResultState] = useState<DeviceAppsResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [retryVersion, setRetryVersion] = useState(0);
  const cache = useRef(new Map<string, Device360Response<DeviceDetectedApp[]>>());
  const result = resultState?.managedDeviceId === managedDeviceId ? resultState.response : null;

  useEffect(() => {
    if (!active) return;
    const cached = cache.current.get(managedDeviceId);
    if (reusable(cached)) {
      setResultState({ managedDeviceId, response: cached });
      setLoading(false);
      return;
    }
    let current = true;
    setLoading(true);
    void fetchDeviceApps(api, managedDeviceId).then(response => {
      if (!current) return;
      setResultState({ managedDeviceId, response });
      if (reusable(response)) cache.current.set(managedDeviceId, response);
    }).finally(() => {
      if (current) setLoading(false);
    });
    return () => { current = false; };
  }, [active, api, managedDeviceId, retryVersion]);

  const retry = () => {
    cache.current.delete(managedDeviceId);
    setResultState(null);
    setRetryVersion(value => value + 1);
  };
  const rows = Array.isArray(result?.data) ? result.data : [];

  return <div className="device360-apps" aria-busy={active && loading ? 'true' : undefined}>
    <div className="device360-section-heading">
      <div>
        <h2>{copy.title}</h2>
        <p>{copy.intro}</p>
      </div>
    </div>
    <WorkspaceDataState kind="partial" compact message={copy.coverageWarning} />
    {loading && !result && <p role="status">{copy.loading}</p>}
    {result && <p className="device360-freshness">{copy.sourceTimestamp} {copy.retrievalTime}: {result.retrievedAt ? formatDateTime(result.retrievedAt) : copy.retrievalUnavailable}.</p>}
    {result && <AppsResponseState response={result} onRetry={retry} />}
    {result && (result.status === 'succeeded' || result.status === 'partial') && Array.isArray(result.data) && <>
      {(result.status === 'partial' || result.partialData) && <WorkspaceDataState kind="partial" compact message={copy.partial} />}
      {rows.length === 0
        ? <WorkspaceDataState kind="empty" compact message={copy.empty} />
        : <div className="device360-table-scroll" role="region" aria-label={copy.tableLabel} tabIndex={0}>
          <table className="device360-table">
            <thead><tr><th scope="col">{copy.appColumn}</th><th scope="col">{copy.versionColumn}</th><th scope="col">{copy.platformColumn}</th><th scope="col">{copy.publisherColumn}</th></tr></thead>
            <tbody>{rows.map(app => <tr key={app.id}>
              <td>{app.displayName?.trim() || copy.nameUnavailable}</td>
              <td>{app.version?.trim() || copy.unavailable}</td>
              <td>{app.platform.trim() || copy.unknown}</td>
              <td>{normalizeDetectedAppPublisher(app.publisher) ?? copy.unavailable}</td>
            </tr>)}</tbody>
          </table>
        </div>}
    </>}
  </div>;
}

export function normalizeDetectedAppPublisher(publisher: string | null): string | null {
  const normalized = publisher?.trim();
  if (!normalized) return null;
  if (/^CN\s*=\s*[^,\r\n]+$/i.test(normalized) || distinguishedNamePattern.test(normalized)) return null;
  return normalized;
}

function AppsResponseState({ response, onRetry }: { response: Device360Response<DeviceDetectedApp[]>; onRetry: () => void }) {
  const copy = devicesMessages.deviceApps;
  if (response.status === 'succeeded' || response.status === 'partial') {
    return response.data === null
      ? <WorkspaceDataState kind="unavailable" compact message={copy.unreadable} onRetry={onRetry} />
      : null;
  }
  return <WorkspaceDataState
    kind={response.status === 'graph_forbidden' || response.status === 'missing_scope' || response.status === 'consent_required' || response.status === 'capability_required' ? 'permission' : 'unavailable'}
    compact
    message={appsStatusMessage(response.status)}
    onRetry={response.status === 'unsupported' ? undefined : onRetry}
  />;
}

function appsStatusMessage(status: Device360ReadStatus) {
  const copy = devicesMessages.deviceApps;
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

function reusable(response: Device360Response<DeviceDetectedApp[]> | null | undefined): response is Device360Response<DeviceDetectedApp[]> {
  return Boolean(response && ['succeeded', 'partial'].includes(response.status));
}
