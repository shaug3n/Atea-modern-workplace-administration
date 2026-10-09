import React, { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';
import { useApi } from '../../auth/useApi';
import type { CapabilityDecision } from '../../capabilities/capabilityTypes';
import { WorkspaceDataState } from '../../components/WorkspaceDataState';
import { formatDateTime, formatRelative } from '../../format/dateTime';
import { humanizeCapability } from '../../format/humanize';
import type { ApiFetch } from '../users/userDetailApi';
import type { BitlockerMetadata, LapsMetadata, ManagedDevice, RecoveryResponse } from './devicesApi';
import { fetchBitlockerMetadata, fetchLapsMetadata, RecoveryFailure, revealBitlocker, revealLaps } from './devicesApi';
import {
  fetchDeviceProtection,
  type Device360ReadStatus,
  type Device360Response,
  type DeviceWindowsProtectionState,
} from './device360Api';
import { devicesMessages } from './devicesMessages';
import './device360.css';

type VisibleSecret = { type: 'bitlocker'; value: string } | { type: 'laps'; value: string; accountName?: string | null };
type ProtectionState = { managedDeviceId: string; response: Device360Response<DeviceWindowsProtectionState> };
type BitlockerState = { managedDeviceId: string; value: BitlockerMetadata[] };
type LapsState = { managedDeviceId: string; value: LapsMetadata };

export function DeviceSecurityTab({ device, managedDeviceId, active, capabilities }: {
  device: ManagedDevice;
  managedDeviceId: string;
  active: boolean;
  capabilities: CapabilityDecision[];
}) {
  return <DeviceSecurityContent key={managedDeviceId} device={device} managedDeviceId={managedDeviceId} active={active} capabilities={capabilities} />;
}

function DeviceSecurityContent({ device, managedDeviceId, active, capabilities }: {
  device: ManagedDevice;
  managedDeviceId: string;
  active: boolean;
  capabilities: CapabilityDecision[];
}) {
  const copy = devicesMessages.deviceSecurity;
  const api = useApi() as ApiFetch;
  const [protectionState, setProtectionState] = useState<ProtectionState | null>(null);
  const [protectionBusy, setProtectionBusy] = useState(false);
  const [protectionRetry, setProtectionRetry] = useState(0);
  const protectionCache = useRef(new Map<string, Device360Response<DeviceWindowsProtectionState>>());
  const [bitlockerState, setBitlockerState] = useState<BitlockerState | null>(null);
  const [lapsState, setLapsState] = useState<LapsState | null>(null);
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
  const activeRef = useRef(active);
  const generationRef = useRef(0);
  const metadataGenerationRef = useRef({ bitlocker: 0, laps: 0 });
  const clearTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
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

  const clearSensitive = useCallback(() => {
    clearSecret();
    setReason('');
    setRevealError(null);
    setRevealBusy(false);
  }, [clearSecret]);

  useLayoutEffect(() => {
    activeRef.current = active;
    if (!active) {
      metadataGenerationRef.current.bitlocker += 1;
      metadataGenerationRef.current.laps += 1;
      setBitlockerBusy(false);
      setLapsBusy(false);
      clearSensitive();
    }
    return () => {
      activeRef.current = false;
      metadataGenerationRef.current.bitlocker += 1;
      metadataGenerationRef.current.laps += 1;
      clearSensitive();
    };
  }, [active, clearSensitive]);

  useEffect(() => {
    if (!active || !isWindows(device.operatingSystem)) return;
    const cached = protectionCache.current.get(managedDeviceId);
    if (reusableProtection(cached)) {
      setProtectionState({ managedDeviceId, response: cached });
      setProtectionBusy(false);
      return;
    }
    let current = true;
    setProtectionBusy(true);
    void fetchDeviceProtection(api, managedDeviceId).then(response => {
      if (!current || !activeRef.current) return;
      setProtectionState({ managedDeviceId, response });
      if (reusableProtection(response)) protectionCache.current.set(managedDeviceId, response);
    }).finally(() => {
      if (current && activeRef.current) setProtectionBusy(false);
    });
    return () => { current = false; };
  }, [active, api, device.operatingSystem, managedDeviceId, protectionRetry]);

  const protection = protectionState?.managedDeviceId === managedDeviceId ? protectionState.response : null;
  const bitlocker = bitlockerState?.managedDeviceId === managedDeviceId ? bitlockerState.value : null;
  const laps = lapsState?.managedDeviceId === managedDeviceId ? lapsState.value : null;
  const canManageSettings = capabilities.some(decision => decision.capability === 'workspace.settings.manage' && decision.state === 'allowed');
  const blocked = (name: CapabilityDecision['capability']) => {
    const decision = recoveryDecision(name);
    return decision && decision.state !== 'allowed' ? decision : undefined;
  };
  const sharedBlock = (() => {
    const first = blocked('devices.bitlocker.metadata');
    const second = blocked('devices.laps.metadata');
    return first && second && first.state === second.state ? first : undefined;
  })();
  const blockerProps = (name: CapabilityDecision['capability'], shared = false) => ({
    decision: recoveryDecision(name), capability: name, canManageSettings, suppressed: shared,
  });
  const describedBy = (name: CapabilityDecision['capability'], shared = false) => shared
    ? 'recovery-shared-blocker'
    : blocked(name) ? `blocker-${name}` : undefined;

  const loadMetadata = async (type: 'bitlocker' | 'laps') => {
    const requestDeviceId = managedDeviceId;
    const requestGeneration = ++metadataGenerationRef.current[type];
    if (type === 'bitlocker') {
      setBitlockerBusy(true); setBitlockerError(null); setBitlockerState(null); setBitlockerFetchedAt(null);
    } else {
      setLapsBusy(true); setLapsError(null); setLapsState(null); setLapsFetchedAt(null);
    }
    const isCurrent = () => activeRef.current && requestDeviceId === managedDeviceId && metadataGenerationRef.current[type] === requestGeneration;
    try {
      if (type === 'bitlocker') {
        const result = await fetchBitlockerMetadata(api, requestDeviceId);
        if (isCurrent()) {
          setBitlockerState({ managedDeviceId, value: result.data ?? [] });
          setBitlockerFetchedAt(new Date().toISOString());
        }
      } else {
        const result = await fetchLapsMetadata(api, requestDeviceId);
        if (isCurrent()) {
          setLapsState(result.data ? { managedDeviceId, value: result.data } : null);
          setLapsFetchedAt(new Date().toISOString());
        }
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
    if (!reason.trim() || revealBusy || !activeRef.current) return;
    setRevealBusy(true); setRevealError(null); clearSecret();
    const requestGeneration = generationRef.current;
    try {
      if (type === 'bitlocker' && keyId) {
        const result = await revealBitlocker(api, managedDeviceId, keyId, reason.trim());
        if (activeRef.current && generationRef.current === requestGeneration && result.data) showSecret({ type, value: result.data.key });
      } else if (type === 'laps') {
        const result = await revealLaps(api, managedDeviceId, reason.trim());
        if (activeRef.current && generationRef.current === requestGeneration && result.data) showSecret({ type, value: result.data.password, accountName: result.data.accountName });
      }
    } catch (error) {
      if (activeRef.current && generationRef.current === requestGeneration) setRevealError(failure(error));
    } finally {
      if (activeRef.current) setRevealBusy(false);
    }
  };

  const retryProtection = () => {
    protectionCache.current.delete(managedDeviceId);
    setProtectionState(null);
    setProtectionRetry(value => value + 1);
  };

  const protectionResult = protection && <ProtectionResult response={protection} onRetry={retryProtection} />;
  const protectionData = protection?.data;
  const protectionUnreported = (protection?.status === 'succeeded' || protection?.status === 'partial')
    && (!protectionData || Object.values(protectionData).every(value => value === null));

  if (!active) return null;

  return <div className="device360-security" aria-busy={protectionBusy ? 'true' : undefined}>
    <div className="device360-section-heading">
      <div><h2>{copy.title}</h2><p>{copy.intro}</p></div>
    </div>
    <section className="content-panel" aria-labelledby="device-encryption-title">
      <h3 id="device-encryption-title">{copy.encryptionTitle}</h3>
      {device.isEncrypted == null
        ? <WorkspaceDataState kind="empty" compact message={copy.encryptionUnavailable} />
        : <p role="status">{device.isEncrypted ? copy.encryptionEnabled : copy.encryptionDisabled}</p>}
    </section>

    <section className="content-panel" aria-labelledby="windows-protection-title" aria-busy={protectionBusy ? 'true' : undefined}>
      <h3 id="windows-protection-title">{copy.protectionTitle}</h3>
      <p>{copy.protectionIntro}</p>
      {!device.operatingSystem?.trim()
        ? <WorkspaceDataState kind="empty" compact message={copy.protectionPlatformUnknown} />
        : !isWindows(device.operatingSystem)
          ? <WorkspaceDataState kind="unavailable" compact message={copy.protectionNotWindows} />
          : <>
            {protectionBusy && !protection && <p role="status">{copy.protectionLoading}</p>}
            {protection && <ProtectionFreshness response={protection} />}
            {protectionResult}
            {protectionUnreported && <WorkspaceDataState kind="empty" compact message={copy.protectionUnreported} />}
            {protectionData && !protectionUnreported && (protection.status === 'succeeded' || protection.status === 'partial') && <>
              {(protection.status === 'partial' || protection.partialData) && <WorkspaceDataState kind="partial" compact message={copy.protectionPartial} />}
              <dl className="detail-list">
                <ProtectionField label={copy.antiMalwareVersion} value={protectionData.antiMalwareVersion} />
                <ProtectionField label={copy.controlledConfigurationEnabled} value={protectionData.controlledConfigurationEnabled} />
                <ProtectionField label={copy.deviceState} value={protectionData.deviceState} />
                <ProtectionField label={copy.engineVersion} value={protectionData.engineVersion} />
                <ProtectionField label={copy.fullScanOverdue} value={protectionData.fullScanOverdue} />
                <ProtectionField label={copy.fullScanRequired} value={protectionData.fullScanRequired} />
                <ProtectionField label={copy.isVirtualMachine} value={protectionData.isVirtualMachine} />
                <ProtectionField label={copy.lastFullScanDateTime} value={protectionData.lastFullScanDateTime} dateTime />
                <ProtectionField label={copy.lastFullScanSignatureVersion} value={protectionData.lastFullScanSignatureVersion} />
                <ProtectionField label={copy.lastQuickScanDateTime} value={protectionData.lastQuickScanDateTime} dateTime />
                <ProtectionField label={copy.lastQuickScanSignatureVersion} value={protectionData.lastQuickScanSignatureVersion} />
                <ProtectionField label={copy.malwareProtectionEnabled} value={protectionData.malwareProtectionEnabled} />
                <ProtectionField label={copy.networkInspectionSystemEnabled} value={protectionData.networkInspectionSystemEnabled} />
                <ProtectionField label={copy.productStatus} value={protectionData.productStatus} />
                <ProtectionField label={copy.quickScanOverdue} value={protectionData.quickScanOverdue} />
                <ProtectionField label={copy.realTimeProtectionEnabled} value={protectionData.realTimeProtectionEnabled} />
                <ProtectionField label={copy.rebootRequired} value={protectionData.rebootRequired} />
                <ProtectionField label={copy.signatureUpdateOverdue} value={protectionData.signatureUpdateOverdue} />
                <ProtectionField label={copy.signatureVersion} value={protectionData.signatureVersion} />
                <ProtectionField label={copy.tamperProtectionEnabled} value={protectionData.tamperProtectionEnabled} />
              </dl>
            </>}
          </>}
    </section>

    <section className="content-panel device-recovery" aria-labelledby="device-recovery-title">
      <h3 id="device-recovery-title">{copy.recoveryTitle}</h3>
      <p>{copy.recoveryIntro}</p>
      {sharedBlock && <div id="recovery-shared-blocker"><WorkspaceDataState kind="permission" compact message={`${copy.recoveryDenied}${sharedBlock.state === 'temporarily_unavailable' ? copy.recoveryAuthorizationUnavailable : ''}`} action={blockerLink(sharedBlock, canManageSettings)} /></div>}
      <div className="device-recovery__groups">
        <section><h4>{copy.bitlocker}</h4><RecoveryStamp label={copy.bitlockerMetadata} fetchedAt={bitlockerFetchedAt} />
          <button type="button" className="button button--secondary" aria-describedby={describedBy('devices.bitlocker.metadata', Boolean(sharedBlock))} disabled={bitlockerBusy || !recoveryAllowed('devices.bitlocker.metadata')} onClick={() => void loadMetadata('bitlocker')}>{copy.loadBitlockerMetadata}</button>
          <RecoveryGuidance {...blockerProps('devices.bitlocker.metadata', Boolean(sharedBlock))} />
          {bitlockerBusy && <p role="status">{copy.loadingBitlockerMetadata}</p>}
          {bitlocker && (bitlocker.length === 0 ? <p>{copy.noBitlockerRecord}</p> : <ul>{bitlocker.map(key => <li key={key.id}><span>{copy.keyId}: {key.id}</span>{key.volumeType && <span> · {copy.volume}: {key.volumeType}</span>}{key.createdDateTime && <span> · {copy.backedUp}: {formatDateTime(key.createdDateTime)}</span>} <button type="button" className="button button--secondary" aria-describedby={describedBy('devices.bitlocker.reveal')} disabled={!reason.trim() || revealBusy || !recoveryAllowed('devices.bitlocker.reveal')} onClick={() => void reveal('bitlocker', key.id)}>{copy.revealBitlocker(key.id)}</button></li>)}</ul>)}
          {bitlockerError && <p role="alert">{recoveryMessage(bitlockerError)}{correlationDetails(bitlockerError)}</p>}
          <RecoveryGuidance {...blockerProps('devices.bitlocker.reveal')} />
        </section>
        <section><h4>{copy.laps}</h4><RecoveryStamp label={copy.lapsMetadata} fetchedAt={lapsFetchedAt} />
          <button type="button" className="button button--secondary" aria-describedby={describedBy('devices.laps.metadata', Boolean(sharedBlock))} disabled={lapsBusy || !recoveryAllowed('devices.laps.metadata')} onClick={() => void loadMetadata('laps')}>{copy.loadLapsMetadata}</button>
          <RecoveryGuidance {...blockerProps('devices.laps.metadata', Boolean(sharedBlock))} />
          {lapsBusy && <p role="status">{copy.loadingLapsMetadata}</p>}
          {laps && <><p>{copy.secretLapsMetadata}</p><dl className="detail-list"><Field label={copy.deviceName} value={laps.deviceName} /><Field label={copy.lastBackup} value={laps.lastBackupDateTime ? formatDateTime(laps.lastBackupDateTime) : null} /><Field label={copy.nextRefresh} value={laps.refreshDateTime ? formatDateTime(laps.refreshDateTime) : null} /></dl><button type="button" className="button button--secondary" aria-describedby={describedBy('devices.laps.reveal')} disabled={!reason.trim() || revealBusy || !recoveryAllowed('devices.laps.reveal')} onClick={() => void reveal('laps')}>{copy.revealLaps}</button></>}
          {lapsError && <p role="alert">{recoveryMessage(lapsError)}{correlationDetails(lapsError)}</p>}
          <RecoveryGuidance {...blockerProps('devices.laps.reveal')} />
        </section>
      </div>
      <div className="device-recovery__reason"><label htmlFor="recovery-reason">{copy.reasonLabel}</label><input id="recovery-reason" value={reason} maxLength={500} aria-describedby="recovery-reason-hint" onChange={event => setReason(event.target.value)} placeholder={copy.reasonPlaceholder} /><p id="recovery-reason-hint" className="field-hint">{copy.reasonHint}</p></div>
      {revealError && <p role="alert">{recoveryMessage(revealError)}{correlationDetails(revealError)}</p>}
      {secret && <div className="device-recovery__secret" role="status"><strong>{secret.type === 'laps' ? copy.secretLaps(secret.accountName) : copy.secretBitlocker}</strong><code>{secret.value}</code><p>{copy.revealSecretClears}</p><button type="button" className="button button--secondary" onClick={clearSecret}>{copy.closeSecret}</button></div>}
    </section>
  </div>;
}

function ProtectionFreshness({ response }: { response: Device360Response<DeviceWindowsProtectionState> }) {
  const copy = devicesMessages.deviceSecurity;
  const reportedAt = response.data?.lastReportedDateTime;
  return <p className="device360-freshness">
    {reportedAt ? <><span>{copy.protectionSourceTime}: </span><time dateTime={reportedAt}>{formatDateTime(reportedAt)}</time></> : copy.protectionSourceTimeUnavailable}
    {' · '}{copy.protectionRetrievedAt}: {response.retrievedAt ? formatDateTime(response.retrievedAt) : copy.retrievalUnavailable}.
  </p>;
}

function ProtectionResult({ response, onRetry }: { response: Device360Response<DeviceWindowsProtectionState>; onRetry: () => void }) {
  const copy = devicesMessages.deviceSecurity;
  if (response.status === 'succeeded' || response.status === 'partial') return null;
  const messages: Record<Device360ReadStatus, string> = {
    succeeded: copy.protectionUnreported,
    partial: copy.protectionPartial,
    unsupported: copy.protectionUnsupported,
    no_reported_policies: copy.protectionUnreported,
    invalid_target: copy.protectionInvalidTarget,
    device_not_found: copy.protectionDeviceNotFound,
    capability_required: copy.protectionCapabilityRequired,
    missing_scope: copy.protectionMissingScope,
    consent_required: copy.protectionConsentRequired,
    graph_forbidden: copy.protectionForbidden,
    throttled: copy.protectionThrottled,
    temporarily_unavailable: copy.protectionTemporarilyUnavailable,
    failed: copy.protectionFailed,
  };
  const permitted = ['graph_forbidden', 'missing_scope', 'consent_required', 'capability_required'].includes(response.status);
  return <WorkspaceDataState
    kind={permitted ? 'permission' : 'unavailable'}
    compact
    message={messages[response.status]}
    onRetry={response.status === 'unsupported' ? undefined : onRetry}
  />;
}

function ProtectionField({ label, value, dateTime = false }: { label: string; value: string | boolean | null; dateTime?: boolean }) {
  const copy = devicesMessages.deviceSecurity;
  const display = value === null ? copy.fieldUnavailable : typeof value === 'boolean' ? value ? 'Yes' : 'No' : dateTime ? formatDateTime(value) : value;
  return <div><dt>{label}</dt><dd>{dateTime && typeof value === 'string' ? <time dateTime={value}>{display}</time> : display}</dd></div>;
}

function isWindows(operatingSystem?: string | null) { return operatingSystem?.trim().toLowerCase().includes('windows') ?? false; }
function reusableProtection(response: Device360Response<DeviceWindowsProtectionState> | null | undefined): response is Device360Response<DeviceWindowsProtectionState> {
  return Boolean(response && ['succeeded', 'partial'].includes(response.status) && response.data !== null);
}
function blockerLink(decision: CapabilityDecision, canManageSettings: boolean) {
  return decision.state === 'consent_required' && canManageSettings ? { label: devicesMessages.deviceSecurity.openSetup, href: '/settings#connection' } : { label: devicesMessages.deviceSecurity.openPimGuidance, href: '/identity' };
}
function RecoveryStamp({ label, fetchedAt }: { label: string; fetchedAt: string | null }) {
  const copy = devicesMessages.deviceSecurity;
  return <p className="data-freshness">{fetchedAt ? copy.secretRetrieved(label, formatRelative(fetchedAt)) : copy.notLoadedYet}</p>;
}
function Field({ label, value }: { label: string; value?: string | null }) { return value ? <><dt>{label}</dt><dd>{value}</dd></> : null; }
function RecoveryGuidance({ decision, capability, canManageSettings, suppressed }: { decision?: CapabilityDecision; capability: string; canManageSettings: boolean; suppressed?: boolean }) {
  const copy = devicesMessages.deviceSecurity;
  if (!decision || suppressed) return null;
  if (decision.state === 'allowed') return decision.nextStep ? <p><a href={decision.nextStep.href || '/identity'}>{decision.nextStep.label}</a></p> : null;
  if (decision.state === 'temporarily_unavailable') return <p id={`blocker-${capability}`}>{copy.recoveryChecksUnavailable}</p>;
  const link = blockerLink(decision, canManageSettings);
  return <p id={`blocker-${capability}`}>{humanizeCapability(capability)} isn't available to you. <a href={link.href}>{link.label}</a></p>;
}
function failure(error: unknown): RecoveryResponse<never> { return error instanceof RecoveryFailure ? error.result : { status: 'temporarily_unavailable' }; }
function correlationDetails(result: RecoveryResponse<never>) { return `${result.graphCorrelationId ? ` Correlation ID: ${result.graphCorrelationId}.` : ''}${result.graphRequestId ? ` Graph request ID: ${result.graphRequestId}.` : ''}`; }
function recoveryMessage(result: RecoveryResponse<never>) {
  const copy = devicesMessages.deviceSecurity;
  switch (result.status) {
    case 'device_not_found': return copy.recoveryDeviceNotFound;
    case 'entra_device_missing': return copy.recoveryEntraDeviceMissing;
    case 'recovery_not_found': return copy.recoveryNotFound;
    case 'missing_scope': return copy.recoveryMissingScope(result.error || 'for this operation');
    case 'consent_required': return copy.recoveryConsentRequired;
    case 'graph_forbidden': return `${copy.recoveryGraphForbidden}${result.guidance === 'pim_activation_required' ? copy.recoveryPimRequired : result.guidance === 'laps_password_role_required' ? copy.recoveryLapsRoleRequired : copy.recoveryRoleGuidance}`;
    case 'throttled': return copy.recoveryThrottled(result.retryAfterSeconds);
    case 'audit_unavailable': return copy.recoveryAuditUnavailable;
    case 'reason_required': return copy.recoveryReasonRequired;
    default: return copy.recoveryUnavailable;
  }
}
