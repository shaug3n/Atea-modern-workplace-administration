import { useEffect, useRef, useState, type KeyboardEvent } from 'react';
import { useApi } from '../../auth/useApi';
import type { CapabilityDecision } from '../../capabilities/capabilityTypes';
import type { ApiFetch } from '../users/userDetailApi';
import { fetchDeviceDetail, RecoveryFailure, type ManagedDevice, type RecoveryResponse } from './devicesApi';
import { StatusBadge } from '../../components/StatusBadge';
import { formatDateTime, formatRelative } from '../../format/dateTime';
import { WorkspacePageHeader } from '../../components/WorkspacePageHeader';
import { WorkspaceDataState } from '../../components/WorkspaceDataState';
import { useWorkspaceIssueReporter } from '../../notifications/WorkspaceNotifications';
import { InfoTip } from '../../components/InfoTip';
import { devicesMessages } from './devicesMessages';
import { Device360OverviewTab, reportedOwnershipLabel, reportedUserDomain } from './Device360OverviewTab';
import { fetchDeviceCompliancePolicies, type Device360Response, type DeviceCompliancePolicyState } from './device360Api';
import { DeviceConfigurationTab } from './DeviceConfigurationTab';
import { DeviceAppsTab } from './DeviceAppsTab';
import { DeviceSecurityTab } from './DeviceSecurityTab';
import { DeviceActionsTab } from './DeviceActionsTab';

type Device360Tab = keyof typeof devicesMessages.device360Tabs;
const emptyPolicies: Device360Response<DeviceCompliancePolicyState[]> = {
  status: 'succeeded',
  data: [],
  retrievedAt: null,
  partialData: false,
  error: null,
  retryAfterSeconds: null,
  graphCorrelationId: null,
  graphRequestId: null,
};

export function DeviceDetailPage({ deviceId, capabilities = [], onNavigate }: { deviceId?: string; capabilities?: CapabilityDecision[]; onNavigate?: (path: string) => void }) {
  const id = deviceId ?? deviceIdFromPath();
  return <DeviceDetailContent key={id} id={id} capabilities={capabilities} onNavigate={onNavigate} />;
}

function DeviceDetailContent({ id, capabilities, onNavigate }: { id: string; capabilities: CapabilityDecision[]; onNavigate?: (path: string) => void }) {
  const api = useApi() as ApiFetch;
  const issueReporter = useWorkspaceIssueReporter();
  const [refreshVersion, setRefreshVersion] = useState(0);
  const [device, setDevice] = useState<ManagedDevice | null>(null);
  const [deviceFetchedAt, setDeviceFetchedAt] = useState<string | null>(null);
  const [detailError, setDetailError] = useState<RecoveryResponse<never> | null>(null);
  const [busy, setBusy] = useState(true);
  const [activeTab, setActiveTab] = useState<Device360Tab>('overview');
  const [policies, setPolicies] = useState<Device360Response<DeviceCompliancePolicyState[]> | null>(null);
  const [policiesBusy, setPoliciesBusy] = useState(false);
  const [policyRefreshVersion, setPolicyRefreshVersion] = useState(0);
  const policyGenerationRef = useRef(0);
  const tabRefs = useRef<Partial<Record<Device360Tab, HTMLButtonElement>>>({});

  useEffect(() => {
    let cancelled = false;
    setBusy(true); setDetailError(null); setDevice(null); setDeviceFetchedAt(null); setActiveTab('overview');
    setPolicies(null); setPoliciesBusy(false); setPolicyRefreshVersion(0);
    if (!id) { setDetailError({ status: 'invalid_target' }); setBusy(false); return () => undefined; }
    fetchDeviceDetail(api, id).then(value => { if (!cancelled) { setDevice(value); setDeviceFetchedAt(new Date().toISOString()); issueReporter.clear('devices:detail:read'); } })
      .catch(error => { if (!cancelled) { setDetailError(failure(error)); issueReporter.report({ key: 'devices:detail:read', area: 'devices', kind: 'service', severity: 'warning', title: 'Device details unavailable', detail: 'Try loading device details again.' }); } })
      .finally(() => { if (!cancelled) setBusy(false); });
    return () => { cancelled = true; };
  }, [api, id, refreshVersion, issueReporter]);

  useEffect(() => {
    if (!device || activeTab !== 'overview' || policies) return;
    let cancelled = false;
    const requestDeviceId = device.id;
    const requestGeneration = ++policyGenerationRef.current;
    setPoliciesBusy(true);
    fetchDeviceCompliancePolicies(api, requestDeviceId).then(result => {
      if (!cancelled && activeTab === 'overview' && requestDeviceId === device.id && policyGenerationRef.current === requestGeneration) setPolicies(result);
    }).finally(() => {
      if (!cancelled && activeTab === 'overview' && requestDeviceId === device.id && policyGenerationRef.current === requestGeneration) setPoliciesBusy(false);
    });
    return () => { cancelled = true; policyGenerationRef.current += 1; };
  }, [api, device, activeTab, policies, policyRefreshVersion]);

  const goBack = (path: string) => { if (onNavigate) onNavigate(path); else window.location.assign(path); };
  const title = device ? (device.deviceName || 'Unnamed device') : 'Device details';
  const complianceTone = device?.complianceState?.toLowerCase() === 'compliant' ? 'success' : device?.complianceState?.toLowerCase() === 'noncompliant' ? 'warning' : 'neutral';
  const tabNames = Object.keys(devicesMessages.device360Tabs) as Device360Tab[];
  const handleTabKeyDown = (event: KeyboardEvent<HTMLButtonElement>, current: Device360Tab) => {
    const index = tabNames.indexOf(current);
    const next = event.key === 'ArrowRight' ? tabNames[(index + 1) % tabNames.length]
      : event.key === 'ArrowLeft' ? tabNames[(index - 1 + tabNames.length) % tabNames.length]
        : event.key === 'Home' ? tabNames[0]
          : event.key === 'End' ? tabNames[tabNames.length - 1]
            : undefined;
    if (!next) return;
    event.preventDefault();
    setActiveTab(next);
    tabRefs.current[next]?.focus();
  };
  const retryPolicies = () => { setPolicies(null); setPoliciesBusy(true); setPolicyRefreshVersion(version => version + 1); };

  return <div className="device-full-page">
    <WorkspacePageHeader title={title} backLink={{ label: 'Back to Devices', href: '/devices', onNavigate: goBack }}
      meta={device ? <>
        <StatusBadge tone={complianceTone} label={`${devicesMessages.device360.complianceLabel}: ${device.complianceState ? humanizeCompliance(device.complianceState) : devicesMessages.device360.complianceUnknown}`} />
        <StatusBadge tone="neutral" label={device.lastSyncDateTime ? devicesMessages.device360.activityReported : devicesMessages.device360.activityUnknown} detail={device.lastSyncDateTime ? formatRelative(device.lastSyncDateTime) : undefined} />
        <StatusBadge tone={device.managedDeviceOwnerType?.toLowerCase() === 'personal' ? 'warning' : 'neutral'} label={`${devicesMessages.device360.ownershipLabel}: ${reportedOwnershipLabel(device.managedDeviceOwnerType)}`} />
        <InfoTip label={devicesMessages.device360.activityHelpLabel} content={devicesMessages.device360.activityHelp} />
        <div className="device360-header-identity">
          <span>{devicesMessages.device360.primaryUserPrefix}: {device.userId && device.userDisplayName
            ? <a href={`/users/${encodeURIComponent(device.userId)}`} onClick={event => { if (onNavigate) { event.preventDefault(); onNavigate(`/users/${encodeURIComponent(device.userId!)}`); } }}>{device.userDisplayName}</a>
            : device.userId ? devicesMessages.device360.primaryUserUnavailable : devicesMessages.device360.noPrimaryUser}</span>
          <span>{devicesMessages.device360.userDomainPrefix}: {reportedUserDomain(device.userPrincipalName)}</span>
          <span>{devicesMessages.device360.lastCheckInPrefix}: {device.lastSyncDateTime ? formatDateTime(device.lastSyncDateTime) : devicesMessages.device360.unknownValue}</span>
        </div>
        {deviceFetchedAt && <span className="device360-core-retrieval">{devicesMessages.device360.coreRetrieved} <time dateTime={deviceFetchedAt}>{formatDateTime(deviceFetchedAt)}</time> · {devicesMessages.device360.source}</span>}
      </> : undefined} />
    {busy && <WorkspaceDataState state="loading" message="Loading device details…" />}
    {detailError && <div className="permission-panel"><WorkspaceDataState state="unavailable" message="Device details are unavailable. Check Notifications for details." onRetry={() => setRefreshVersion(version => version + 1)} /></div>}
    {device && <>
      <div className="device360-tabs" role="tablist" aria-label={devicesMessages.device360.tablistLabel}>
        {tabNames.map(tab => <button
          key={tab}
          ref={element => { tabRefs.current[tab] = element ?? undefined; }}
          id={`device360-tab-${tab}`}
          type="button"
          role="tab"
          aria-selected={activeTab === tab}
          aria-controls={`device360-panel-${tab}`}
          tabIndex={activeTab === tab ? 0 : -1}
          onClick={() => setActiveTab(tab)}
          onKeyDown={event => handleTabKeyDown(event, tab)}
        >{devicesMessages.device360Tabs[tab]}</button>)}
      </div>
      {tabNames.map(tab => <section
        key={tab}
        id={`device360-panel-${tab}`}
        className="device360-tabpanel"
        role="tabpanel"
        aria-labelledby={`device360-tab-${tab}`}
        aria-busy={activeTab === tab && tab === 'overview' && policiesBusy ? 'true' : undefined}
        hidden={activeTab !== tab}
        tabIndex={0}
      >
        {activeTab === tab && tab === 'overview' && <Device360OverviewTab device={device} policies={policies ?? emptyPolicies} policyLoading={policiesBusy || !policies} onRetryPolicies={retryPolicies} onNavigate={onNavigate} />}
        {tab === 'configuration' && <DeviceConfigurationTab managedDeviceId={device.id} active={activeTab === 'configuration'} />}
        {tab === 'apps' && <DeviceAppsTab managedDeviceId={device.id} active={activeTab === 'apps'} />}
        {tab === 'security' && <DeviceSecurityTab device={device} managedDeviceId={device.id} active={activeTab === 'security'} capabilities={capabilities} />}
        {activeTab === tab && tab === 'actions' && <DeviceActionsTab device={device} capabilities={capabilities} />}
      </section>)}
    </>}
  </div>;
}
function humanizeCompliance(state: string) {
  const copy = devicesMessages.device360Overview;
  const lower = state.toLowerCase();
  return lower === 'compliant' ? copy.compliant : lower === 'noncompliant' ? copy.noncompliant : lower === 'ingraceperiod' ? copy.gracePeriod : lower === 'unknown' ? copy.unknownCompliance : state;
}
function deviceIdFromPath() { try { return decodeURIComponent(window.location.pathname.slice('/devices/'.length)); } catch { return ''; } }
function failure(error: unknown): RecoveryResponse<never> { return error instanceof RecoveryFailure ? error.result : { status: 'temporarily_unavailable' }; }
