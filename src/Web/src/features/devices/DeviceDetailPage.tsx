import { useCallback, useEffect, useRef, useState } from 'react';
import { useApi } from '../../auth/useApi';
import { ConfirmationDialog } from '../../components/ConfirmationDialog';
import type { CapabilityDecision } from '../../capabilities/capabilityTypes';
import type { ApiFetch } from '../users/userDetailApi';
import { executeDeviceAction, fetchBitlockerMetadata, fetchDeviceDetail, fetchLapsMetadata, revealBitlocker, revealLaps, RecoveryFailure, type BitlockerMetadata, type DeviceAction, type LapsMetadata, type ManagedDevice, type RecoveryResponse } from './devicesApi';

type VisibleSecret = { type: 'bitlocker'; value: string } | { type: 'laps'; value: string; accountName?: string | null };

export function DeviceDetailPage({ deviceId, capabilities = [], onNavigate }: { deviceId?: string; capabilities?: CapabilityDecision[]; onNavigate?: (path: string) => void }) {
  const api = useApi() as ApiFetch;
  const id = deviceId ?? deviceIdFromPath();
  const [device, setDevice] = useState<ManagedDevice | null>(null);
  const [detailError, setDetailError] = useState<RecoveryResponse<never> | null>(null);
  const [busy, setBusy] = useState(true);
  const [bitlocker, setBitlocker] = useState<BitlockerMetadata[] | null>(null);
  const [laps, setLaps] = useState<LapsMetadata | null>(null);
  const [metadataError, setMetadataError] = useState<RecoveryResponse<never> | null>(null);
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
    activeRef.current = true;
    let cancelled = false;
    setBusy(true); setDetailError(null); setDevice(null); setBitlocker(null); setLaps(null); setMetadataError(null); clearSecret();
    if (!id) { setDetailError({ status: 'invalid_target' }); setBusy(false); return () => { activeRef.current = false; }; }
    fetchDeviceDetail(api, id).then(value => { if (!cancelled) setDevice(value); })
      .catch(error => { if (!cancelled) setDetailError(failure(error)); })
      .finally(() => { if (!cancelled) setBusy(false); });
    return () => { cancelled = true; activeRef.current = false; generationRef.current += 1; if (clearTimerRef.current) clearTimeout(clearTimerRef.current); clearTimerRef.current = null; setSecret(null); };
  }, [api, id, clearSecret]);

  const loadMetadata = async (type: 'bitlocker' | 'laps') => {
    setMetadataError(null);
    try {
      if (type === 'bitlocker') {
        const result = await fetchBitlockerMetadata(api, id);
        if (activeRef.current) setBitlocker(result.data ?? []);
      } else {
        const result = await fetchLapsMetadata(api, id);
        if (activeRef.current) setLaps(result.data ?? null);
      }
    } catch (error) {
      if (activeRef.current) setMetadataError(failure(error));
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

  const back = () => { clearSecret(); if (onNavigate) onNavigate('/devices'); else window.location.assign('/devices'); };

  return <section className="device-full-page" aria-labelledby="device-detail-title">
    <header className="page-header"><div><button type="button" className="table-action" onClick={back}>← Back to devices</button><p className="eyebrow">Device details</p><h1 id="device-detail-title">{device?.deviceName || 'Device details'}</h1></div></header>
    {busy && <p role="status">Loading device details…</p>}
    {detailError && <div className="permission-panel" role="alert"><p>{recoveryMessage(detailError)}{correlationDetails(detailError)}</p><button type="button" onClick={() => window.location.reload()}>Retry</button></div>}
    {device && <>
      <div className="device-detail-layout">
        <section className="content-panel"><h2>Overview</h2><dl className="detail-list"><Field label="Device ID" value={device.id} /><Field label="Operating system" value={[device.operatingSystem, device.osVersion].filter(Boolean).join(' ')} /><Field label="Owner type" value={device.managedDeviceOwnerType} /><Field label="Primary user ID" value={device.userId} /><Field label="Entra device ID" value={device.azureAdDeviceId} /></dl></section>
        <section className="content-panel"><h2>Security and management</h2><dl className="detail-list"><Field label="Compliance" value={device.complianceState} /><Field label="Management state" value={device.managementState} /><Field label="Last sync" value={device.lastSyncDateTime ? new Date(device.lastSyncDateTime).toLocaleString() : null} /></dl></section>
        <section className="content-panel"><h2>Hardware</h2><dl className="detail-list"><Field label="Manufacturer" value={device.manufacturer} /><Field label="Model" value={device.model} /><Field label="Serial number" value={device.serialNumber} /></dl></section>
      </div>

      <section className="content-panel device-recovery" aria-labelledby="device-recovery-title"><h2 id="device-recovery-title">Recovery data</h2><p>Load recovery records only when needed. Microsoft Graph checks your access to this device.</p>
        <div className="device-recovery__groups">
          <section><h3>BitLocker</h3><button type="button" disabled={!recoveryAllowed('devices.bitlocker.metadata')} onClick={() => void loadMetadata('bitlocker')}>Load BitLocker metadata</button>
            <RecoveryGuidance decision={recoveryDecision('devices.bitlocker.metadata')} />
            {bitlocker && (bitlocker.length === 0 ? <p>No BitLocker recovery record found.</p> : <ul>{bitlocker.map(key => <li key={key.id}><span>Key ID: {key.id}</span>{key.volumeType && <span> · Volume: {key.volumeType}</span>}{key.createdDateTime && <span> · Backed up: {new Date(key.createdDateTime).toLocaleString()}</span>} <button type="button" disabled={!reason.trim() || revealBusy || !recoveryAllowed('devices.bitlocker.reveal')} onClick={() => void reveal('bitlocker', key.id)}>Reveal BitLocker key {key.id}</button></li>)}</ul>)}
            <RecoveryGuidance decision={recoveryDecision('devices.bitlocker.reveal')} />
          </section>
          <section><h3>Windows LAPS</h3><button type="button" disabled={!recoveryAllowed('devices.laps.metadata')} onClick={() => void loadMetadata('laps')}>Load Windows LAPS metadata</button>
            <RecoveryGuidance decision={recoveryDecision('devices.laps.metadata')} />
            {laps && <><p>Windows LAPS metadata loaded</p><dl className="detail-list"><Field label="Device name" value={laps.deviceName} /><Field label="Last backup" value={laps.lastBackupDateTime ? new Date(laps.lastBackupDateTime).toLocaleString() : null} /><Field label="Next refresh" value={laps.refreshDateTime ? new Date(laps.refreshDateTime).toLocaleString() : null} /></dl><button type="button" disabled={!reason.trim() || revealBusy || !recoveryAllowed('devices.laps.reveal')} onClick={() => void reveal('laps')}>Reveal Windows LAPS password</button></>}
            <RecoveryGuidance decision={recoveryDecision('devices.laps.reveal')} />
          </section>
        </div>
        <label htmlFor="recovery-reason">Reason for recovery access</label><input id="recovery-reason" value={reason} maxLength={500} onChange={event => setReason(event.target.value)} placeholder="Incident or support case" />
        {metadataError && <p role="alert">{recoveryMessage(metadataError)}{correlationDetails(metadataError)}</p>}
        {revealError && <p role="alert">{recoveryMessage(revealError)}{correlationDetails(revealError)}</p>}
        {secret && <div className="device-recovery__secret" role="status"><strong>{secret.type === 'laps' ? `Windows LAPS password${secret.accountName ? ` for ${secret.accountName}` : ''}` : 'BitLocker recovery key'}</strong><code>{secret.value}</code><p>Clears automatically after 60 seconds.</p><button type="button" onClick={clearSecret}>Close secret</button></div>}
      </section>

      {canManage && <section className="content-panel"><h2>Remote actions</h2><div className="device-detail-panel__actions">{(['sync', 'remote-lock', 'restart', 'retire', 'wipe'] as DeviceAction[]).map(action => <button key={action} type="button" className={action === 'retire' || action === 'wipe' ? 'button button--danger' : 'button button--secondary'} onClick={() => setActionTarget(action)}>{actionLabel(action)}</button>)}</div>{actionStatus && <p role="status">{actionStatus}</p>}{actionError && <p role="alert">Device action failed. Retry or check permissions.</p>}</section>}
      {actionTarget && <ConfirmationDialog title={actionLabel(actionTarget)} target={device.deviceName || device.id} proposedChange={`${actionLabel(actionTarget)} this managed device.`} requiredCapability="devices.privileged.manage" destructivePhrase={actionTarget === 'sync' ? null : actionTarget === 'remote-lock' ? 'REMOTE LOCK' : actionTarget.toUpperCase()} busy={actionBusy} onConfirm={() => void runAction()} onCancel={() => { if (!actionBusy) setActionTarget(null); }} />}
    </>}
  </section>;
}

function Field({ label, value }: { label: string; value?: string | null }) { return value ? <><dt>{label}</dt><dd>{value}</dd></> : null; }
function RecoveryGuidance({ decision }: { decision?: CapabilityDecision }) {
  if (!decision) return null;
  if (decision.state === 'allowed') return decision.nextStep ? <p>{decision.nextStep.label}</p> : null;
  if (decision.state === 'consent_required') return <p>Recovery access requires a delegated Graph scope: {decision.missingScopes?.join(' or ') || 'ask your tenant administrator to review Setup'}. <a href="/settings/setup">Open Setup</a> for registration and consent guidance.</p>;
  if (decision.state === 'temporarily_unavailable') return <p>Authorization checks are temporarily unavailable. Retry after the checks recover.</p>;
  if (decision.state.startsWith('pim_')) return <p>Activate your eligible Entra role in PIM and retry. <a href="/identity">Open PIM guidance</a>.</p>;
  return <p>Your Microsoft account cannot access this recovery operation.</p>;
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
    case 'graph_forbidden': return `This Microsoft account lacks access to this specific recovery data.${result.guidance === 'pim_activation_required' ? ' Activate your eligible Entra role in PIM and retry.' : ' Check your device ownership or Entra role and scope.'}`;
    case 'throttled': return `Microsoft Graph is throttling recovery requests. Retry${result.retryAfterSeconds ? ` after ${result.retryAfterSeconds} seconds` : ' later'}.`;
    case 'audit_unavailable': return 'The audit record could not be saved, so the secret was not returned. Retry later.';
    case 'reason_required': return 'Enter a reason before revealing recovery data.';
    default: return 'Recovery data is temporarily unavailable. Retry and share the correlation details with support if it continues.';
  }
}
