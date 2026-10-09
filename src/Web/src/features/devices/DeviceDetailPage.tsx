import { useCallback, useEffect, useRef, useState, type KeyboardEvent } from 'react';
import { useApi } from '../../auth/useApi';
import { messages } from '../../app/messages';
import { ConfirmationDialog } from '../../components/ConfirmationDialog';
import type { CapabilityDecision } from '../../capabilities/capabilityTypes';
import type { ApiFetch } from '../users/userDetailApi';
import { executeDeviceAction, fetchBitlockerMetadata, fetchDeviceDetail, fetchLapsMetadata, revealBitlocker, revealLaps, RecoveryFailure, type BitlockerMetadata, type DeviceAction, type LapsMetadata, type ManagedDevice, type RecoveryResponse } from './devicesApi';
import { ActionGroup } from '../../components/ActionGroup';
import { StatusBadge } from '../../components/StatusBadge';
import { formatDateTime, formatRelative } from '../../format/dateTime';
import { humanizeCapability } from '../../format/humanize';
import { WorkspacePageHeader } from '../../components/WorkspacePageHeader';
import { WorkspaceDataState } from '../../components/WorkspaceDataState';
import { useWorkspaceIssueReporter } from '../../notifications/WorkspaceNotifications';
import { InfoTip } from '../../components/InfoTip';
import { devicesMessages } from './devicesMessages';
import { Device360OverviewTab, reportedOwnershipLabel, reportedUserDomain } from './Device360OverviewTab';
import { fetchDeviceCompliancePolicies, type Device360Response, type DeviceCompliancePolicyState } from './device360Api';
import { DeviceConfigurationTab } from './DeviceConfigurationTab';
import { DeviceAppsTab } from './DeviceAppsTab';

type VisibleSecret = { type: 'bitlocker'; value: string } | { type: 'laps'; value: string; accountName?: string | null };
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
  const [bitlocker, setBitlocker] = useState<BitlockerMetadata[] | null>(null);
  const [laps, setLaps] = useState<LapsMetadata | null>(null);
  const [bitlockerFetchedAt, setBitlockerFetchedAt] = useState<string | null>(null);
  const [lapsFetchedAt, setLapsFetchedAt] = useState<string | null>(null);
  const [bitlockerError, setBitlockerError] = useState<RecoveryResponse<never> | null>(null);
  const [lapsError, setLapsError] = useState<RecoveryResponse<never> | null>(null);
  const [bitlockerBusy, setBitlockerBusy] = useState(false);
  const [lapsBusy, setLapsBusy] = useState(false);
  const [reason, setReason] = useState('');
  const [secret, setSecret] = useState<VisibleSecret | null>(null);
  const [revealError, setRevealError] = useState<RecoveryResponse<never> | null>(null);
  const [revealBusy, setRevealBusy] = useState(false);
  const [actionTarget, setActionTarget] = useState<DeviceAction | null>(null);
  const [actionStatus, setActionStatus] = useState<string | null>(null);
  const [actionBusy, setActionBusy] = useState(false);
  const [actionError, setActionError] = useState(false);
  const activeRef = useRef(true);
  const generationRef = useRef(0);
  const metadataGenerationRef = useRef({ bitlocker: 0, laps: 0 });
  const clearTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const canManage = capabilities.some(decision => decision.capability === 'devices.privileged.manage' && decision.state === 'allowed');
  const recoveryDecision = (name: CapabilityDecision['capability']) => capabilities.find(decision => decision.capability === name);
  const recoveryAllowed = (name: CapabilityDecision['capability']) => {
    const decision = recoveryDecision(name);
    return !decision || decision.state === 'allowed';
  };

  const clearSecret = useCallback(() => {
    generationRef.current += 1;
    if (clearTimerRef.current) clearTimeout(clearTimerRef.current);
    clearTimerRef.current = null;
    setSecret(null);
  }, []);

  useEffect(() => {
    if (activeTab !== 'security') clearSecret();
    if (activeTab !== 'actions') setActionTarget(null);
  }, [activeTab, clearSecret]);

  useEffect(() => {
    activeRef.current = true;
    let cancelled = false;
    setBusy(true); setDetailError(null); setDevice(null); setDeviceFetchedAt(null); setActiveTab('overview');
    setPolicies(null); setPoliciesBusy(false); setPolicyRefreshVersion(0);
    setBitlocker(null); setLaps(null); setBitlockerFetchedAt(null); setLapsFetchedAt(null);
    setBitlockerError(null); setLapsError(null); setBitlockerBusy(false); setLapsBusy(false); setReason(''); clearSecret();
    if (!id) { setDetailError({ status: 'invalid_target' }); setBusy(false); return () => { activeRef.current = false; }; }
    fetchDeviceDetail(api, id).then(value => { if (!cancelled) { setDevice(value); setDeviceFetchedAt(new Date().toISOString()); issueReporter.clear('devices:detail:read'); } })
      .catch(error => { if (!cancelled) { setDetailError(failure(error)); issueReporter.report({ key: 'devices:detail:read', area: 'devices', kind: 'service', severity: 'warning', title: 'Device details unavailable', detail: 'Try loading device details again.' }); } })
      .finally(() => { if (!cancelled) setBusy(false); });
    return () => { cancelled = true; activeRef.current = false; generationRef.current += 1;
      metadataGenerationRef.current.bitlocker += 1; metadataGenerationRef.current.laps += 1;
      if (clearTimerRef.current) clearTimeout(clearTimerRef.current); clearTimerRef.current = null; setSecret(null); };
  }, [api, id, clearSecret, refreshVersion, issueReporter]);

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

  const loadMetadata = async (type: 'bitlocker' | 'laps') => {
    const requestDeviceId = id;
    const requestGeneration = ++metadataGenerationRef.current[type];
    if (type === 'bitlocker') { setBitlockerBusy(true); setBitlockerError(null); setBitlocker(null); setBitlockerFetchedAt(null); }
    else { setLapsBusy(true); setLapsError(null); setLaps(null); setLapsFetchedAt(null); }
    const isCurrent = () => activeRef.current && requestDeviceId === id && metadataGenerationRef.current[type] === requestGeneration;
    try {
      if (type === 'bitlocker') {
        const result = await fetchBitlockerMetadata(api, requestDeviceId);
        if (isCurrent()) { setBitlocker(result.data ?? []); setBitlockerFetchedAt(new Date().toISOString()); }
      } else {
        const result = await fetchLapsMetadata(api, requestDeviceId);
        if (isCurrent()) { setLaps(result.data ?? null); setLapsFetchedAt(new Date().toISOString()); }
      }
    } catch (error) {
      if (isCurrent()) {
        if (type === 'bitlocker') setBitlockerError(failure(error));
        else setLapsError(failure(error));
      }
    } finally {
      if (isCurrent()) {
        if (type === 'bitlocker') setBitlockerBusy(false);
        else setLapsBusy(false);
      }
    }
  };

  const showSecret = (value: VisibleSecret) => {
    clearSecret();
    setSecret(value);
    clearTimerRef.current = setTimeout(() => { if (activeRef.current) clearSecret(); }, 60_000);
  };

  const reveal = async (type: 'bitlocker' | 'laps', keyId?: string) => {
    if (!reason.trim() || revealBusy) return;
    setRevealBusy(true); setRevealError(null); clearSecret();
    const requestGeneration = generationRef.current;
    try {
      if (type === 'bitlocker' && keyId) {
        const result = await revealBitlocker(api, id, keyId, reason.trim());
        if (activeRef.current && generationRef.current === requestGeneration && result.data) showSecret({ type, value: result.data.key });
      } else if (type === 'laps') {
        const result = await revealLaps(api, id, reason.trim());
        if (activeRef.current && generationRef.current === requestGeneration && result.data) showSecret({ type, value: result.data.password, accountName: result.data.accountName });
      }
    } catch (error) {
      if (activeRef.current && generationRef.current === requestGeneration) setRevealError(failure(error));
    } finally {
      if (activeRef.current) setRevealBusy(false);
    }
  };

  const runAction = async () => {
    if (!actionTarget || !device || actionBusy) return;
    setActionBusy(true); setActionError(false); setActionStatus(null);
    try { await executeDeviceAction(api, device.id, actionTarget); setActionStatus(`${actionTarget} request submitted.`); setActionTarget(null); }
    catch { setActionError(true); }
    finally { setActionBusy(false); }
  };

  const canManageSettings = capabilities.some(decision => decision.capability === 'workspace.settings.manage' && decision.state === 'allowed');
  const blocked = (name: CapabilityDecision['capability']) => { const decision = recoveryDecision(name); return decision && decision.state !== 'allowed' ? decision : undefined; };
  const sharedBlock = (() => {
    const first = blocked('devices.bitlocker.metadata'); const second = blocked('devices.laps.metadata');
    return first && second && first.state === second.state ? first : undefined;
  })();
  const goBack = (path: string) => { clearSecret(); if (onNavigate) onNavigate(path); else window.location.assign(path); };
  const title = device ? (device.deviceName || 'Unnamed device') : 'Device details';
  const complianceTone = device?.complianceState?.toLowerCase() === 'compliant' ? 'success' : device?.complianceState?.toLowerCase() === 'noncompliant' ? 'warning' : 'neutral';
  const blockerProps = (name: CapabilityDecision['capability'], shared = false) => ({ decision: recoveryDecision(name), capability: name, canManageSettings, suppressed: shared });
  const describedBy = (name: CapabilityDecision['capability'], shared = false) => shared ? 'recovery-shared-blocker' : blocked(name) ? `blocker-${name}` : undefined;
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

      {activeTab === tab && tab === 'security' && <section className="content-panel device-recovery" aria-labelledby="device-recovery-title"><h2 id="device-recovery-title">Recovery data</h2><p>Load recovery records only when needed. Microsoft Graph checks your access to this device.</p>
        {sharedBlock && <div id="recovery-shared-blocker"><WorkspaceDataState kind="permission" compact message={`Recovery data isn't available to you.${sharedBlock.state === 'temporarily_unavailable' ? ' Authorization checks are temporarily unavailable. Retry after the checks recover.' : ''}`} action={blockerLink(sharedBlock, canManageSettings)} /></div>}
        <div className="device-recovery__groups">
          <section><h3>BitLocker</h3><RecoveryStamp label="BitLocker metadata" fetchedAt={bitlockerFetchedAt} /><button type="button" className="button button--secondary" aria-describedby={describedBy('devices.bitlocker.metadata', Boolean(sharedBlock))} disabled={bitlockerBusy || !recoveryAllowed('devices.bitlocker.metadata')} onClick={() => void loadMetadata('bitlocker')}>Load BitLocker metadata</button>
            <RecoveryGuidance {...blockerProps('devices.bitlocker.metadata', Boolean(sharedBlock))} />
            {bitlockerBusy && <p role="status">Loading BitLocker metadata…</p>}
            {bitlocker && (bitlocker.length === 0 ? <p>No BitLocker recovery record found.</p> : <ul>{bitlocker.map(key => <li key={key.id}><span>Key ID: {key.id}</span>{key.volumeType && <span> · Volume: {key.volumeType}</span>}{key.createdDateTime && <span> · Backed up: {formatDateTime(key.createdDateTime)}</span>} <button type="button" className="button button--secondary" aria-describedby={describedBy('devices.bitlocker.reveal')} disabled={!reason.trim() || revealBusy || !recoveryAllowed('devices.bitlocker.reveal')} onClick={() => void reveal('bitlocker', key.id)}>Reveal BitLocker key {key.id}</button></li>)}</ul>)}
            {bitlockerError && <p role="alert">{recoveryMessage(bitlockerError)}{correlationDetails(bitlockerError)}</p>}
            <RecoveryGuidance {...blockerProps('devices.bitlocker.reveal')} />
          </section>
          <section><h3>Windows LAPS</h3><RecoveryStamp label="Windows LAPS metadata" fetchedAt={lapsFetchedAt} /><button type="button" className="button button--secondary" aria-describedby={describedBy('devices.laps.metadata', Boolean(sharedBlock))} disabled={lapsBusy || !recoveryAllowed('devices.laps.metadata')} onClick={() => void loadMetadata('laps')}>Load Windows LAPS metadata</button>
            <RecoveryGuidance {...blockerProps('devices.laps.metadata', Boolean(sharedBlock))} />
            {lapsBusy && <p role="status">Loading Windows LAPS metadata…</p>}
            {laps && <><p>Windows LAPS metadata loaded</p><dl className="detail-list"><Field label="Device name" value={laps.deviceName} /><Field label="Last backup" value={laps.lastBackupDateTime ? formatDateTime(laps.lastBackupDateTime) : null} /><Field label="Next refresh" value={laps.refreshDateTime ? formatDateTime(laps.refreshDateTime) : null} /></dl><button type="button" className="button button--secondary" aria-describedby={describedBy('devices.laps.reveal')} disabled={!reason.trim() || revealBusy || !recoveryAllowed('devices.laps.reveal')} onClick={() => void reveal('laps')}>Reveal Windows LAPS password</button></>}
            {lapsError && <p role="alert">{recoveryMessage(lapsError)}{correlationDetails(lapsError)}</p>}
            <RecoveryGuidance {...blockerProps('devices.laps.reveal')} />
          </section>
        </div>
        <div className="device-recovery__reason"><label htmlFor="recovery-reason">Reason for recovery access</label><input id="recovery-reason" value={reason} maxLength={500} aria-describedby="recovery-reason-hint" onChange={event => setReason(event.target.value)} placeholder="Incident or support case" /><p id="recovery-reason-hint" className="field-hint">Required before revealing a key or password. It is saved in the audit record.</p></div>
        {revealError && <p role="alert">{recoveryMessage(revealError)}{correlationDetails(revealError)}</p>}
        {secret && <div className="device-recovery__secret" role="status"><strong>{secret.type === 'laps' ? `Windows LAPS password${secret.accountName ? ` for ${secret.accountName}` : ''}` : 'BitLocker recovery key'}</strong><code>{secret.value}</code><p>Clears automatically after 60 seconds.</p><button type="button" className="button button--secondary" onClick={clearSecret}>Close secret</button></div>}
      </section>}

      {activeTab === tab && tab === 'actions' && <div className="device360-actions">
      {canManage ? <>
        <ActionGroup title="Device actions" description="These requests are sent to the device through Intune.">
          <div className="device-actions">{(['sync', 'restart', 'remote-lock'] as DeviceAction[]).map(action => <div key={action} className="device-actions__item"><button type="button" className="button button--secondary" onClick={() => setActionTarget(action)}>{actionLabel(action)}</button><span>{actionDescription[action]}</span></div>)}</div>
        </ActionGroup>
        {actionStatus && <p role="status">{actionStatus}</p>}{actionError && <p role="alert">Device action failed. Retry or check permissions.</p>}
        <ActionGroup tone="danger" title="Danger zone">
          <div className="device-actions">{(['retire', 'wipe'] as DeviceAction[]).map(action => <div key={action} className="device-actions__item"><button type="button" className="button button--danger" onClick={() => setActionTarget(action)}>{actionLabel(action)}</button><span>{actionDescription[action]}</span></div>)}</div>
        </ActionGroup>
      </> : <WorkspaceDataState kind="permission" message={devicesMessages.device360.actionsCapabilityRequired} />}
      {actionTarget && <ConfirmationDialog title={actionLabel(actionTarget)} target={title} proposedChange={`${actionLabel(actionTarget)} this managed device.`} requiredCapability="devices.privileged.manage" confirmLabel={messages.deviceActionConfirm[actionTarget].label} consequence={messages.deviceActionConfirm[actionTarget].consequence} tone={messages.deviceActionConfirm[actionTarget].tone as 'default' | 'danger'} destructivePhrase={actionTarget === 'sync' ? null : actionTarget === 'remote-lock' ? 'REMOTE LOCK' : actionTarget.toUpperCase()} busy={actionBusy} onConfirm={() => void runAction()} onCancel={() => { if (!actionBusy) setActionTarget(null); }} />}
      </div>}
      </section>)}
    </>}
  </div>;
}

const actionDescription: Record<DeviceAction, string> = {
  sync: 'Ask the device to check in with Intune now.',
  restart: 'Restarts the device. Unsaved work may be lost.',
  'remote-lock': 'Locks the screen until the user enters their passcode.',
  retire: 'Removes company data and management. This cannot be undone.',
  wipe: 'Erases everything and resets the device. This cannot be undone.'
};
function humanizeCompliance(state: string) {
  const copy = devicesMessages.device360Overview;
  const lower = state.toLowerCase();
  return lower === 'compliant' ? copy.compliant : lower === 'noncompliant' ? copy.noncompliant : lower === 'ingraceperiod' ? copy.gracePeriod : lower === 'unknown' ? copy.unknownCompliance : state;
}
function blockerLink(decision: CapabilityDecision, canManageSettings: boolean) {
  return decision.state === 'consent_required' && canManageSettings ? { label: 'Open setup', href: '/settings#connection' } : { label: 'Open PIM guidance', href: '/identity' };
}
function RecoveryStamp({ label, fetchedAt }: { label: string; fetchedAt: string | null }) {
  return <p className="data-freshness">{fetchedAt ? `${label} retrieved ${formatRelative(fetchedAt)}` : 'Not loaded yet.'}</p>;
}

function Field({ label, value }: { label: string; value?: string | null }) { return value ? <><dt>{label}</dt><dd>{value}</dd></> : null; }
function RecoveryGuidance({ decision, capability, canManageSettings, suppressed }: { decision?: CapabilityDecision; capability: string; canManageSettings: boolean; suppressed?: boolean }) {
  if (!decision || suppressed) return null;
  if (decision.state === 'allowed') return decision.nextStep ? <p><a href={decision.nextStep.href || '/identity'}>{decision.nextStep.label}</a></p> : null;
  if (decision.state === 'temporarily_unavailable') return <p id={`blocker-${capability}`}>Authorization checks are temporarily unavailable. Retry after the checks recover.</p>;
  const link = blockerLink(decision, canManageSettings);
  return <p id={`blocker-${capability}`}>{humanizeCapability(capability)} isn't available to you. <a href={link.href}>{link.label}</a></p>;
}
function deviceIdFromPath() { try { return decodeURIComponent(window.location.pathname.slice('/devices/'.length)); } catch { return ''; } }
function actionLabel(action: DeviceAction) { return ({ sync: 'Sync device', 'remote-lock': 'Remote lock', restart: 'Restart device', retire: 'Retire device', wipe: 'Wipe device' } as const)[action]; }
function failure(error: unknown): RecoveryResponse<never> { return error instanceof RecoveryFailure ? error.result : { status: 'temporarily_unavailable' }; }
function correlationDetails(result: RecoveryResponse<never>) { return `${result.graphCorrelationId ? ` Correlation ID: ${result.graphCorrelationId}.` : ''}${result.graphRequestId ? ` Graph request ID: ${result.graphRequestId}.` : ''}`; }
function recoveryMessage(result: RecoveryResponse<never>) {
  switch (result.status) {
    case 'device_not_found': return 'This managed device was not found in the current workspace tenant.';
    case 'entra_device_missing': return 'This managed device has no Entra device ID for recovery lookup.';
    case 'recovery_not_found': return 'No recovery record was found for this device.';
    case 'missing_scope': return `The API app registration needs the delegated Microsoft Graph scope ${result.error || 'for this operation'}. Ask the Atea app owner to add it, then request tenant consent.`;
    case 'consent_required': return 'The delegated recovery scope needs tenant administrator consent. Open Setup and grant consent, then sign in again.';
    case 'graph_forbidden': return `This Microsoft account lacks access to this specific recovery data.${result.guidance === 'pim_activation_required' ? ' Activate your eligible Entra role in PIM and retry.' : result.guidance === 'laps_password_role_required' ? ' Global Reader can view Windows LAPS metadata but cannot reveal passwords. A Cloud Device Administrator, Intune Service Administrator, or supported custom role may request the password; Microsoft Graph checks the target.' : ' Check your device ownership or Entra role and scope.'}`;
    case 'throttled': return `Microsoft Graph is throttling recovery requests. Retry${result.retryAfterSeconds ? ` after ${result.retryAfterSeconds} seconds` : ' later'}.`;
    case 'audit_unavailable': return 'The audit record could not be saved, so the secret was not returned. Retry later.';
    case 'reason_required': return 'Enter a reason before revealing recovery data.';
    default: return 'Recovery data is temporarily unavailable. Retry and share the correlation details with support if it continues.';
  }
}
