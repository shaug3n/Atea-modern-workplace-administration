import React, { useEffect, useMemo, useRef, useState } from 'react';
import { messages } from '../../app/messages';
import { useApi } from '../../auth/useApi';
import { DataFreshness } from '../../components/DataFreshness';
import { ConfirmationDialog } from '../../components/ConfirmationDialog';
import { executeDeviceAction, fetchDevices, type DeviceAction, type DeviceFilters, type DevicesResponse, type ManagedDevice } from './devicesApi';
import type { ApiFetch } from '../users/userDetailApi';
import { isPimCapabilityState, type CapabilityDecision } from '../../capabilities/capabilityTypes';
import { PermissionState } from '../../components/PermissionState';
import { ActionMenu } from '../../components/ActionMenu';
import { useFocusContainment } from '../../components/useFocusContainment';
import { downloadCsv, exportStatus } from '../exports/csvExport';
import { ResponsiveDataView } from '../../components/ResponsiveDataView';
import { WorkspacePageHeader } from '../../components/WorkspacePageHeader';
import { WorkspaceDataState } from '../../components/WorkspaceDataState';
import { useWorkspaceIssueReporter } from '../../notifications/WorkspaceNotifications';

const emptyFilters: DeviceFilters = { search: '', complianceState: '', operatingSystem: '' };

export function DevicesPage({ loadDevices, capabilities = [], moduleAssigned = true, moduleEnabled = true, onNavigate, authorizationUnavailable = false, onAuthorizationRetry }: { loadDevices?: (filters: DeviceFilters, continuationToken?: string | null) => Promise<DevicesResponse>; capabilities?: CapabilityDecision[]; moduleAssigned?: boolean; moduleEnabled?: boolean; onNavigate?: (path: string) => void; authorizationUnavailable?: boolean; onAuthorizationRetry?: () => Promise<void> }) {
  const api = useApi();
  const issueReporter = useWorkspaceIssueReporter();
  const [filters, setFilters] = useState(emptyFilters);
  const [continuationToken, setContinuationToken] = useState<string | null>(null);
  const [previousTokens, setPreviousTokens] = useState<string[]>([]);
  const [result, setResult] = useState<DevicesResponse | null>(null);
  const [resultKey, setResultKey] = useState<string | null>(null);
  const [resultRevision, setResultRevision] = useState(-1);
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  const [failedRevision, setFailedRevision] = useState(-1);
  const [refreshVersion, setRefreshVersion] = useState(0);
  const [actionTarget, setActionTarget] = useState<{ device: ManagedDevice; action: DeviceAction } | null>(null);
  const [detailsTarget, setDetailsTarget] = useState<ManagedDevice | null>(null);
  const [actionPending, setActionPending] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);
  const [actionStatus, setActionStatus] = useState<string | null>(null);
  const [exportPending, setExportPending] = useState(false);
  const [exportMessage, setExportMessage] = useState<string | null>(null);
  const [exportError, setExportError] = useState<string | null>(null);
  const actionPendingRef = useRef(false);
  const detailsTriggerRef = useRef<HTMLElement | null>(null);
  const loader = useMemo(() => loadDevices ?? ((nextFilters: DeviceFilters, nextContinuationToken?: string | null) => fetchDevices(api as ApiFetch, nextFilters, nextContinuationToken ?? null)), [api, loadDevices]);
  const viewDecision = capabilities.find((decision) => decision.capability === 'devices.view') ?? { capability: 'devices.view' as const, state: 'hidden' as const, reasonCode: 'capability_not_returned' };
  const privilegedDecision = capabilities.find((decision) => decision.capability === 'devices.privileged.manage') ?? { capability: 'devices.privileged.manage' as const, state: 'hidden' as const, reasonCode: 'capability_not_returned' };
  const viewReadable = viewDecision.state === 'allowed' || viewDecision.state === 'read_only';
  const activeQueryKey = JSON.stringify([filters, continuationToken, refreshVersion]);
  const queryRevisionRef = useRef({ key: activeQueryKey, revision: 0 });
  if (queryRevisionRef.current.key !== activeQueryKey) queryRevisionRef.current = { key: activeQueryKey, revision: queryRevisionRef.current.revision + 1 };
  const activeQueryRevision = queryRevisionRef.current.revision;
  const currentResult = resultKey === activeQueryKey && resultRevision === activeQueryRevision && viewReadable ? result : null;
  const currentFailed = failed && failedRevision === activeQueryRevision;
  useEffect(() => { setActionTarget(null); setDetailsTarget(null); }, [activeQueryKey]);

  if (!moduleEnabled || !moduleAssigned) {
    return <section className="devices-page" aria-label="Devices"><WorkspacePageHeader eyebrow="Devices and endpoints" title="Devices" description="Device management is available to workspace administrators for setup guidance and to members who have been assigned this module." /><section className="permission-panel" role="status"><h2>{moduleEnabled ? 'Device module access needed' : 'Device module is disabled'}</h2><p>{moduleEnabled ? 'Ask a workspace owner to assign you the Devices module. Your Entra role and PIM state will still determine whether device data is readable and which actions you can take.' : 'A workspace owner can enable Devices in Workspace settings. Enabling it does not grant Microsoft Graph scopes or Entra roles.'}</p>{!moduleEnabled && <a href="/settings/modules">Open workspace modules</a>}</section></section>;
  }

  useEffect(() => {
    if (!viewReadable) {
      setLoading(false);
      setFailed(false);
      return;
    }

    let cancelled = false;
    setLoading(true);
    setFailed(false);
    const timeout = window.setTimeout(() => {
      loader(filters, continuationToken)
        .then((response) => { if (!cancelled && queryRevisionRef.current.revision === activeQueryRevision) { setResult(response); setResultKey(activeQueryKey); setResultRevision(activeQueryRevision); if (response.error) issueReporter.report({ key: 'devices:read', area: 'devices', kind: 'service', severity: 'warning', title: 'Device data unavailable', detail: 'Try loading devices again.' }); else issueReporter.clear('devices:read'); } })
        .catch(() => { if (!cancelled && queryRevisionRef.current.revision === activeQueryRevision) { setFailed(true); setFailedRevision(activeQueryRevision); issueReporter.report({ key: 'devices:read', area: 'devices', kind: 'service', severity: 'warning', title: 'Device data unavailable', detail: 'Try loading devices again.' }); } })
        .finally(() => { if (!cancelled && queryRevisionRef.current.revision === activeQueryRevision) setLoading(false); });
    }, 180);
    return () => { cancelled = true; window.clearTimeout(timeout); };
  }, [activeQueryKey, continuationToken, filters, loader, refreshVersion, viewReadable, issueReporter]);

  useEffect(() => {
    setPreviousTokens([]);
    setContinuationToken(null);
  }, [filters.search, filters.complianceState, filters.operatingSystem]);

  useEffect(() => {
    const requestedId = new URLSearchParams(window.location.search).get('device');
    if (!requestedId || !currentResult) return;
    const requestedDevice = currentResult.items.find((device) => device.id === requestedId);
    if (requestedDevice) setDetailsTarget((current) => current ?? requestedDevice);
  }, [currentResult]);

  const runAction = async (device: ManagedDevice, action: DeviceAction) => {
    if (actionPendingRef.current) return;
    actionPendingRef.current = true;
    setActionPending(true);
    setActionError(null);
    setActionStatus(null);
    try {
      await executeDeviceAction(api as ApiFetch, device.id, action);
      setActionStatus(`${device.deviceName || messages.devicesUnknown}: ${messages.devicesActionSubmitted(action)}`);
      setActionTarget(null);
      setRefreshVersion((version) => version + 1);
    } catch {
      setActionError(messages.devicesActionFailed);
    } finally {
      actionPendingRef.current = false;
      setActionPending(false);
    }
  };

  const summary = currentResult ? summarize(currentResult.items) : null;
  const deviceErrorTitle = currentResult?.error?.category === 'not_provisioned' ? messages.devicesNotProvisionedTitle : messages.devicesUnavailable;
  const goNext = () => {
    if (!currentResult?.continuationToken) return;
    setPreviousTokens((tokens) => [...tokens, continuationToken ?? '']);
    setContinuationToken(currentResult.continuationToken ?? null);
  };
  const goPrevious = () => {
    setPreviousTokens((tokens) => {
      const next = [...tokens];
      const previous = next.pop();
      setContinuationToken(previous || null);
      return next;
    });
  };

  const exportDevices = async () => {
    if (!viewReadable || exportPending) return;
    setExportPending(true);
    setExportMessage(null);
    setExportError(null);
    try {
      const params = new URLSearchParams();
      for (const key of ['search', 'complianceState', 'operatingSystem'] as const) {
        if (filters[key].trim()) params.set(key, filters[key].trim());
      }
      setExportMessage(exportStatus(await downloadCsv(api as ApiFetch, `/api/devices/export.csv${params.size ? `?${params}` : ''}`, 'devices.csv')));
    } catch {
      setExportError('Filtered devices export failed. Check your permissions and try again.');
    } finally { setExportPending(false); }
  };

  if (!viewReadable && !authorizationUnavailable) {
    return (
      <section className="devices-page" aria-label="Devices">
        <WorkspacePageHeader eyebrow={messages.devicesEyebrow} title={messages.devicesTitle} description={messages.devicesIntro} />
        <section className="permission-panel" role="status" aria-labelledby="devices-permission-title">
          <h2 id="devices-permission-title">{isPimCapabilityState(viewDecision.state) ? messages.devicesPimRequiredTitle : messages.devicesUnavailable}</h2>
          <PermissionState decision={viewDecision}>
            <p>{messages.devicesPimRequiredBody}</p>
          </PermissionState>
          {viewDecision.state === 'hidden' && <p>{messages.permissionRequiredBody}</p>}
        </section>
      </section>
    );
  }

  return (
    <section className="devices-page" aria-label="Devices">
      <WorkspacePageHeader eyebrow={messages.devicesEyebrow} title={messages.devicesTitle} description={messages.devicesIntro} actions={<div className="page-header__actions">
          <button type="button" onClick={() => setRefreshVersion((version) => version + 1)} disabled={loading}>{messages.usersRefreshAction}</button>
          <button type="button" onClick={() => void exportDevices()} disabled={exportPending}>Export filtered CSV</button>
        </div>} />

      {exportMessage && <p role="status">{exportMessage}</p>}
      {exportError && <p role="alert">{exportError}</p>}

      {!currentFailed && currentResult && <DataFreshness fetchedAt={currentResult.fetchedAt} freshness={currentResult.freshness === 'live' ? 'fresh' : currentResult.freshness === 'stale' ? 'stale' : 'unavailable'} partialData={currentResult.partialData} message={currentResult.error ? 'Some device data could not be loaded.' : undefined} />}
      {!currentFailed && currentResult && !currentResult.error && <div className="device-summary-grid" aria-label="Device summary">
        <SummaryCard label="Managed devices" value={String(currentResult.total)} detail="In the current result" />
        <SummaryCard label="Compliant" value={String(summary?.compliant ?? 0)} detail="Ready for work" />
        <SummaryCard label="Noncompliant" value={String(summary?.noncompliant ?? 0)} detail="Needs attention" />
        <SummaryCard label="Last check-in" value={summary?.lastCheckIn ?? messages.devicesNotSynced} detail="Most recent device signal" />
      </div>}

      <div className="devices-filters" aria-label="Device filters">
        <label>{messages.devicesSearchLabel}<input placeholder={messages.devicesSearchPlaceholder} value={filters.search} onChange={(event) => setFilters({ ...filters, search: event.target.value })} /></label>
        <label>{messages.devicesComplianceLabel}<select value={filters.complianceState} onChange={(event) => setFilters({ ...filters, complianceState: event.target.value })}><option value="">{messages.devicesFilterAny}</option><option value="compliant">Compliant</option><option value="noncompliant">Noncompliant</option><option value="unknown">Unknown</option></select></label>
        <label>{messages.devicesOperatingSystemLabel}<select value={filters.operatingSystem} onChange={(event) => setFilters({ ...filters, operatingSystem: event.target.value })}><option value="">{messages.devicesFilterAny}</option><option>Windows</option><option>macOS</option><option>iOS</option><option>Android</option><option>Linux</option></select></label>
        {(filters.search || filters.complianceState || filters.operatingSystem) && <button type="button" onClick={() => setFilters(emptyFilters)}>Clear filters</button>}
      </div>

      {authorizationUnavailable && <WorkspaceDataState state="unavailable" message="Data cannot be shown right now. Check Notifications for details." onRetry={onAuthorizationRetry ? () => void onAuthorizationRetry() : undefined} />}
      {!authorizationUnavailable && currentFailed && <section className="permission-panel"><h2>{messages.devicesUnavailable}</h2><WorkspaceDataState state="unavailable" message={messages.permissionRequiredBody} onRetry={() => setRefreshVersion((version) => version + 1)} /></section>}
      {!authorizationUnavailable && !currentFailed && currentResult?.error && <section className="permission-panel"><h2>{deviceErrorTitle}</h2><WorkspaceDataState state="unavailable" message="Device data is unavailable. Check Notifications for details." onRetry={() => setRefreshVersion((version) => version + 1)} /></section>}
      {!authorizationUnavailable && !currentFailed && !currentResult && <WorkspaceDataState state="loading" message={messages.devicesLoading} />}
      {!authorizationUnavailable && !currentFailed && !currentResult?.error && currentResult && currentResult.items.length === 0 && <WorkspaceDataState state="empty" message={messages.devicesNoResults} />}
      {!authorizationUnavailable && !currentFailed && !currentResult?.error && currentResult && currentResult.items.length > 0 && <DevicesTable devices={currentResult.items} canManage={privilegedDecision.state === 'allowed'} onOpenDetails={(device, trigger) => { if (onNavigate) { onNavigate(`/devices/${encodeURIComponent(device.id)}`); return; } detailsTriggerRef.current = trigger; setDetailsTarget(device); }} onAction={(device, action) => setActionTarget({ device, action })} />}
      {!authorizationUnavailable && !currentFailed && !currentResult?.error && currentResult && <div className="table-pagination" aria-label={messages.devicesPaginationLabel}><span>{messages.devicesPageLabel(previousTokens.length + 1)}</span><div><button type="button" onClick={goPrevious} disabled={previousTokens.length === 0 || loading}>{messages.devicesPreviousPage}</button><button type="button" onClick={goNext} disabled={!currentResult.continuationToken || loading}>{messages.devicesNextPage}</button></div></div>}

      {privilegedDecision.state !== 'allowed' && privilegedDecision.state !== 'hidden' && <div className="device-permission-state"><PermissionState decision={privilegedDecision}><span>{messages.devicesPrivilegedUnavailable}</span></PermissionState></div>}

      {actionStatus && <p className="action-feedback action-feedback--success" role="status">{actionStatus}</p>}
      {actionError && <p className="action-feedback action-feedback--error" role="alert">{actionError}</p>}
      {currentResult && detailsTarget && <DeviceDetailsPanel device={detailsTarget} canManage={privilegedDecision.state === 'allowed'} restoreFocusRef={detailsTriggerRef} onClose={() => setDetailsTarget(null)} onAction={(action) => setActionTarget({ device: detailsTarget, action })} />}
      {currentResult && actionTarget && <DeviceActionConfirmation target={actionTarget} busy={actionPending} onConfirm={() => void runAction(actionTarget.device, actionTarget.action)} onCancel={() => { if (!actionPending) setActionTarget(null); }} />}
    </section>
  );
}

function SummaryCard({ label, value, detail }: { label: string; value: string; detail: string }) {
  return <article className="summary-card"><span className="summary-card__label">{label}</span><strong>{value}</strong><span className="summary-card__detail">{detail}</span></article>;
}

function DevicesTable({ devices, canManage, onOpenDetails, onAction }: { devices: ManagedDevice[]; canManage: boolean; onOpenDetails: (device: ManagedDevice, trigger: HTMLElement) => void; onAction: (device: ManagedDevice, action: DeviceAction) => void }) {
  return (
    <ResponsiveDataView items={devices} keyOf={device => device.id} label="Devices" renderCompact={device => <>
      <strong>{device.deviceName || messages.devicesUnknown}</strong>
      <dl className="responsive-data-view__details"><div><dt>{messages.devicesDeviceIdLabel}</dt><dd>{device.id}</dd></div><div><dt>{messages.devicesPlatformColumn}</dt><dd>{[device.operatingSystem, device.osVersion].filter(Boolean).join(' ') || messages.devicesUnknown}</dd></div><div><dt>{messages.devicesComplianceColumn}</dt><dd>{device.complianceState || messages.devicesUnknown}</dd></div><div><dt>{messages.devicesOwnerColumn}</dt><dd>{device.managedDeviceOwnerType || messages.devicesUnknown}</dd></div><div><dt>{messages.devicesLastSyncColumn}</dt><dd>{device.lastSyncDateTime ? new Date(device.lastSyncDateTime).toLocaleString() : messages.devicesNotSynced}</dd></div><div><dt>{messages.devicesHardwareColumn}</dt><dd>{[device.manufacturer, device.model, device.serialNumber].filter(Boolean).join(' ') || messages.devicesUnknown}</dd></div></dl>
      <div className="responsive-data-view__actions"><button type="button" className="table-action" aria-label={`${messages.devicesOpenDetails} for ${device.deviceName || device.id}`} onClick={event => onOpenDetails(device, event.currentTarget)}>{messages.devicesOpenDetails}</button>{canManage && <ActionMenu label={`${messages.devicesActionsFor} ${device.deviceName || device.id}`} items={actionItems(device, onAction)} />}</div>
    </>} renderTable={rows => <div className="users-table-wrap">
      <table className="users-table" aria-label={messages.devicesTableLabel}>
        <thead><tr><th scope="col">{messages.devicesNameColumn}</th><th scope="col">{messages.devicesPlatformColumn}</th><th scope="col">{messages.devicesComplianceColumn}</th><th scope="col">{messages.devicesActionsColumn}</th></tr></thead>
        <tbody>{rows.map((device) => <tr key={device.id}>
          <td data-label={messages.devicesNameColumn}><strong>{device.deviceName || messages.devicesUnknown}</strong><small>{device.id}</small></td>
          <td data-label={messages.devicesPlatformColumn}>{device.operatingSystem || messages.devicesUnknown}<small>{device.osVersion || ''}</small></td>
          <td data-label={messages.devicesComplianceColumn}><span className="status-badge" data-tone={tone(device.complianceState)}>{device.complianceState || messages.devicesUnknown}</span></td>
          <td data-label={messages.devicesActionsColumn} className="detail-table__actions"><button type="button" className="table-action" aria-label={`${messages.devicesOpenDetails} for ${device.deviceName || device.id}`} onClick={(event) => onOpenDetails(device, event.currentTarget)}>{messages.devicesOpenDetails}</button>{canManage && <ActionMenu label={`${messages.devicesActionsFor} ${device.deviceName || device.id}`} items={actionItems(device, onAction)} />}</td>
        </tr>)}</tbody>
      </table>
    </div>} />
  );
}

function actionItems(device: ManagedDevice, onAction: (device: ManagedDevice, action: DeviceAction) => void) {
  return [
    { label: messages.devicesSyncAction, onSelect: () => onAction(device, 'sync') },
    { label: messages.devicesRemoteLockAction, onSelect: () => onAction(device, 'remote-lock') },
    { label: messages.devicesRestartAction, onSelect: () => onAction(device, 'restart') },
  ];
}

function DeviceDetailsPanel({ device, canManage, restoreFocusRef, onClose, onAction }: { device: ManagedDevice; canManage: boolean; restoreFocusRef: React.RefObject<HTMLElement | null>; onClose: () => void; onAction: (action: DeviceAction) => void }) {
  const panelRef = useFocusContainment<HTMLElement>(true, onClose, restoreFocusRef);

  return <section ref={panelRef} className="device-detail-panel" role="dialog" aria-modal="true" aria-labelledby="device-detail-title">
    <div className="device-detail-panel__header"><div><p className="eyebrow">{messages.devicesDetailsEyebrow}</p><h2 id="device-detail-title">{device.deviceName || messages.devicesUnknown}</h2><p>{[device.manufacturer, device.model].filter(Boolean).join(' ') || messages.devicesUnknown}</p></div><button type="button" aria-label={messages.devicesCloseDetails} onClick={onClose}>×</button></div>
    <section className="device-detail-group"><h3>Overview</h3><dl className="detail-list device-detail-list"><Detail label={messages.devicesDeviceIdLabel} value={device.id} /><Detail label={messages.devicesPlatformColumn} value={[device.operatingSystem, device.osVersion].filter(Boolean).join(' ') || messages.devicesUnknown} /><Detail label={messages.devicesOwnerColumn} value={device.managedDeviceOwnerType || messages.devicesUnknown} /><Detail label={messages.devicesPrimaryUserLabel} value={device.userId || messages.devicesNoPrimaryUser} /></dl></section>
    <section className="device-detail-group"><h3>Security</h3><dl className="detail-list device-detail-list"><Detail label={messages.devicesComplianceColumn} value={device.complianceState || messages.devicesUnknown} /><Detail label={messages.devicesManagementStateLabel} value={device.managementState || messages.devicesUnknown} /><Detail label={messages.devicesLastSyncColumn} value={device.lastSyncDateTime ? new Date(device.lastSyncDateTime).toLocaleString() : messages.devicesNotSynced} /><Detail label={messages.devicesAzureAdDeviceIdLabel} value={device.azureAdDeviceId || messages.devicesUnknown} /></dl>{canManage && <div className="device-detail-panel__actions"><button type="button" className="button button--primary" onClick={() => onAction('sync')}>{messages.devicesSyncAction}</button><button type="button" className="button button--secondary" onClick={() => onAction('remote-lock')}>{messages.devicesRemoteLockAction}</button><button type="button" className="button button--secondary" onClick={() => onAction('restart')}>{messages.devicesRestartAction}</button></div>}</section>
    <section className="device-detail-group device-detail-group--danger"><h3>Danger zone</h3><dl className="detail-list device-detail-list"><Detail label={messages.devicesSerialNumberLabel} value={device.serialNumber || messages.devicesUnknown} /></dl>{canManage && <div className="device-detail-panel__actions"><button type="button" className="button button--danger" onClick={() => onAction('retire')}>{messages.devicesRetireAction}</button><button type="button" className="button button--danger" onClick={() => onAction('wipe')}>{messages.devicesWipeAction}</button></div>}</section>
  </section>;
}

function DeviceActionConfirmation({ target, busy, onConfirm, onCancel }: { target: { device: ManagedDevice; action: DeviceAction }; busy: boolean; onConfirm: () => void; onCancel: () => void }) {
  const phrase = target.action === 'sync' ? null : target.action === 'remote-lock' ? 'REMOTE LOCK' : target.action.toUpperCase();
  const phraseLabel = phrase ? `Type ${phrase} to confirm` : undefined;
  return <ConfirmationDialog title={messages.devicesActionTitles[target.action]} target={target.device.deviceName || target.device.id} proposedChange={messages.devicesActionDescriptions[target.action]} requiredCapability="devices.privileged.manage" destructivePhrase={phrase} destructivePhraseLabel={phraseLabel} busy={busy} onConfirm={onConfirm} onCancel={onCancel} />;
}

function Detail({ label, value }: { label: string; value: string }) {
  return <div><dt>{label}</dt><dd>{value}</dd></div>;
}

function summarize(devices: ManagedDevice[]) {
  const compliant = devices.filter(device => device.complianceState?.toLowerCase() === 'compliant').length;
  const noncompliant = devices.filter(device => device.complianceState?.toLowerCase() === 'noncompliant').length;
  const checkIns = devices.map(device => device.lastSyncDateTime).filter((value): value is string => Boolean(value)).sort();
  const latest = checkIns.length > 0 ? checkIns[checkIns.length - 1] : undefined;
  return { compliant, noncompliant, lastCheckIn: latest ? new Date(latest).toLocaleString() : null };
}

function tone(value?: string | null) {
  if (value?.toLowerCase() === 'compliant') return 'success';
  if (value?.toLowerCase() === 'noncompliant') return 'danger';
  return 'warning';
}
