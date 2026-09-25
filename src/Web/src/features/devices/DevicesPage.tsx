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

const emptyFilters: DeviceFilters = { search: '', complianceState: '', operatingSystem: '' };

export function DevicesPage({ loadDevices, capabilities = [], moduleAssigned = true, moduleEnabled = true, onNavigate }: { loadDevices?: (filters: DeviceFilters, continuationToken?: string | null) => Promise<DevicesResponse>; capabilities?: CapabilityDecision[]; moduleAssigned?: boolean; moduleEnabled?: boolean; onNavigate?: (path: string) => void }) {
  const api = useApi();
  const [filters, setFilters] = useState(emptyFilters);
  const [continuationToken, setContinuationToken] = useState<string | null>(null);
  const [previousTokens, setPreviousTokens] = useState<string[]>([]);
  const [result, setResult] = useState<DevicesResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  const [refreshVersion, setRefreshVersion] = useState(0);
  const [actionTarget, setActionTarget] = useState<{ device: ManagedDevice; action: DeviceAction } | null>(null);
  const [detailsTarget, setDetailsTarget] = useState<ManagedDevice | null>(null);
  const [actionPending, setActionPending] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);
  const [actionStatus, setActionStatus] = useState<string | null>(null);
  const actionPendingRef = useRef(false);
  const detailsTriggerRef = useRef<HTMLElement | null>(null);
  const loader = useMemo(() => loadDevices ?? ((nextFilters: DeviceFilters, nextContinuationToken?: string | null) => fetchDevices(api as ApiFetch, nextFilters, nextContinuationToken ?? null)), [api, loadDevices]);
  const viewDecision = capabilities.find((decision) => decision.capability === 'devices.view') ?? { capability: 'devices.view' as const, state: 'hidden' as const, reasonCode: 'capability_not_returned' };
  const privilegedDecision = capabilities.find((decision) => decision.capability === 'devices.privileged.manage') ?? { capability: 'devices.privileged.manage' as const, state: 'hidden' as const, reasonCode: 'capability_not_returned' };
  const viewReadable = viewDecision.state === 'allowed' || viewDecision.state === 'read_only';

  if (!moduleEnabled || !moduleAssigned) {
    return <section className="devices-page" aria-labelledby="devices-page-title"><header className="page-header"><div><p className="eyebrow">Devices and endpoints</p><h1 id="devices-page-title">Devices</h1><p>Device management is available to workspace administrators for setup guidance and to members who have been assigned this module.</p></div></header><section className="permission-panel" role="status"><h2>{moduleEnabled ? 'Device module access needed' : 'Device module is disabled'}</h2><p>{moduleEnabled ? 'Ask a workspace owner to assign you the Devices module. Your Entra role and PIM state will still determine whether device data is readable and which actions you can take.' : 'A workspace owner can enable Devices in Workspace settings. Enabling it does not grant Microsoft Graph scopes or Entra roles.'}</p>{!moduleEnabled && <a href="/settings/modules">Open workspace modules</a>}</section></section>;
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
        .then((response) => { if (!cancelled) setResult(response); })
        .catch(() => { if (!cancelled) setFailed(true); })
        .finally(() => { if (!cancelled) setLoading(false); });
    }, 180);
    return () => { cancelled = true; window.clearTimeout(timeout); };
  }, [continuationToken, filters, loader, refreshVersion, viewReadable]);

  useEffect(() => {
    setPreviousTokens([]);
    setContinuationToken(null);
  }, [filters.search, filters.complianceState, filters.operatingSystem]);

  useEffect(() => {
    const requestedId = new URLSearchParams(window.location.search).get('device');
    if (!requestedId || !result) return;
    const requestedDevice = result.items.find((device) => device.id === requestedId);
    if (requestedDevice) setDetailsTarget((current) => current ?? requestedDevice);
  }, [result]);

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

  const summary = result ? summarize(result.items) : null;
  const deviceErrorTitle = result?.error?.category === 'not_provisioned' ? messages.devicesNotProvisionedTitle : messages.devicesUnavailable;
  const goNext = () => {
    if (!result?.continuationToken) return;
    setPreviousTokens((tokens) => [...tokens, continuationToken ?? '']);
    setContinuationToken(result.continuationToken ?? null);
  };
  const goPrevious = () => {
    setPreviousTokens((tokens) => {
      const next = [...tokens];
      const previous = next.pop();
      setContinuationToken(previous || null);
      return next;
    });
  };

  if (!viewReadable) {
    return (
      <section className="devices-page" aria-labelledby="devices-page-title">
        <header className="page-header">
          <div>
            <p className="eyebrow">{messages.devicesEyebrow}</p>
            <h1 id="devices-page-title">{messages.devicesTitle}</h1>
            <p>{messages.devicesIntro}</p>
          </div>
        </header>
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
    <section className="devices-page" aria-labelledby="devices-page-title">
      <header className="page-header">
        <div>
          <p className="eyebrow">{messages.devicesEyebrow}</p>
          <h1 id="devices-page-title">{messages.devicesTitle}</h1>
          <p>{messages.devicesIntro}</p>
        </div>
        <div className="page-header__actions">
          <button type="button" onClick={() => setRefreshVersion((version) => version + 1)} disabled={loading}>{messages.usersRefreshAction}</button>
        </div>
      </header>

      {result && <DataFreshness fetchedAt={result.fetchedAt} freshness={result.freshness === 'live' ? 'fresh' : result.freshness === 'stale' ? 'stale' : 'unavailable'} partialData={result.partialData} message={result.error?.message} labels={{ fresh: 'Device data is fresh', stale: 'Device data may be stale', unavailable: 'Device data is unavailable' }} />}
      {result && <div className="device-summary-grid" aria-label="Device summary">
        <SummaryCard label="Managed devices" value={String(result.total)} detail="In the current result" />
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

      {failed && <section className="permission-panel" role="alert"><h2>{messages.devicesUnavailable}</h2><p>{messages.permissionRequiredBody}</p><button type="button" onClick={() => setRefreshVersion((version) => version + 1)}>{messages.retry}</button></section>}
      {!failed && result?.error && <section className="permission-panel" role="alert"><h2>{deviceErrorTitle}</h2><p>{result.error.message}</p><button type="button" onClick={() => setRefreshVersion((version) => version + 1)}>{messages.retry}</button></section>}
      {!failed && loading && !result && <div className="async-state async-state--loading" role="status"><span className="async-state__bar" /><span className="async-state__bar" /><span className="async-state__bar" />{messages.devicesLoading}</div>}
      {!failed && !result?.error && !loading && result && result.items.length === 0 && <div className="async-state">{messages.devicesNoResults}</div>}
      {!failed && !result?.error && result && result.items.length > 0 && <DevicesTable devices={result.items} canManage={privilegedDecision.state === 'allowed'} onOpenDetails={(device, trigger) => { if (onNavigate) { onNavigate(`/devices/${encodeURIComponent(device.id)}`); return; } detailsTriggerRef.current = trigger; setDetailsTarget(device); }} onAction={(device, action) => setActionTarget({ device, action })} />}
      {!failed && !result?.error && result && <div className="table-pagination" aria-label={messages.devicesPaginationLabel}><span>{messages.devicesPageLabel(previousTokens.length + 1)}</span><div><button type="button" onClick={goPrevious} disabled={previousTokens.length === 0 || loading}>{messages.devicesPreviousPage}</button><button type="button" onClick={goNext} disabled={!result.continuationToken || loading}>{messages.devicesNextPage}</button></div></div>}

      {privilegedDecision.state !== 'allowed' && privilegedDecision.state !== 'hidden' && <div className="device-permission-state"><PermissionState decision={privilegedDecision}><span>{messages.devicesPrivilegedUnavailable}</span></PermissionState></div>}

      {actionStatus && <p className="action-feedback action-feedback--success" role="status">{actionStatus}</p>}
      {actionError && <p className="action-feedback action-feedback--error" role="alert">{actionError}</p>}
      {detailsTarget && <DeviceDetailsPanel device={detailsTarget} canManage={privilegedDecision.state === 'allowed'} restoreFocusRef={detailsTriggerRef} onClose={() => setDetailsTarget(null)} onAction={(action) => setActionTarget({ device: detailsTarget, action })} />}
      {actionTarget && <DeviceActionConfirmation target={actionTarget} busy={actionPending} onConfirm={() => void runAction(actionTarget.device, actionTarget.action)} onCancel={() => { if (!actionPending) setActionTarget(null); }} />}
    </section>
  );
}

function SummaryCard({ label, value, detail }: { label: string; value: string; detail: string }) {
  return <article className="summary-card"><span className="summary-card__label">{label}</span><strong>{value}</strong><span className="summary-card__detail">{detail}</span></article>;
}

function DevicesTable({ devices, canManage, onOpenDetails, onAction }: { devices: ManagedDevice[]; canManage: boolean; onOpenDetails: (device: ManagedDevice, trigger: HTMLElement) => void; onAction: (device: ManagedDevice, action: DeviceAction) => void }) {
  return (
    <div className="users-table-wrap">
      <table className="users-table" aria-label={messages.devicesTableLabel}>
        <thead><tr><th>{messages.devicesNameColumn}</th><th>{messages.devicesPlatformColumn}</th><th>{messages.devicesComplianceColumn}</th><th>{messages.devicesOwnerColumn}</th><th>{messages.devicesLastSyncColumn}</th><th>{messages.devicesHardwareColumn}</th><th>{messages.devicesActionsColumn}</th></tr></thead>
        <tbody>{devices.map((device) => <tr key={device.id}>
          <td data-label={messages.devicesNameColumn}><strong>{device.deviceName || messages.devicesUnknown}</strong><small>{device.id}</small></td>
          <td data-label={messages.devicesPlatformColumn}>{device.operatingSystem || messages.devicesUnknown}<small>{device.osVersion || ''}</small></td>
          <td data-label={messages.devicesComplianceColumn}><span className="status-badge" data-tone={tone(device.complianceState)}>{device.complianceState || messages.devicesUnknown}</span></td>
          <td data-label={messages.devicesOwnerColumn}>{device.managedDeviceOwnerType || messages.devicesUnknown}<small>{device.userId || ''}</small></td>
          <td data-label={messages.devicesLastSyncColumn}>{device.lastSyncDateTime ? new Date(device.lastSyncDateTime).toLocaleString() : messages.devicesNotSynced}</td>
          <td data-label={messages.devicesHardwareColumn}>{[device.manufacturer, device.model].filter(Boolean).join(' ') || messages.devicesUnknown}<small>{device.serialNumber || ''}</small></td>
          <td data-label={messages.devicesActionsColumn} className="detail-table__actions"><button type="button" className="table-action" aria-label={`${messages.devicesOpenDetails} for ${device.deviceName || device.id}`} onClick={(event) => onOpenDetails(device, event.currentTarget)}>{messages.devicesOpenDetails}</button>{canManage && <ActionMenu label={`${messages.devicesActionsFor} ${device.deviceName || device.id}`} items={actionItems(device, onAction)} />}</td>
        </tr>)}</tbody>
      </table>
    </div>
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
