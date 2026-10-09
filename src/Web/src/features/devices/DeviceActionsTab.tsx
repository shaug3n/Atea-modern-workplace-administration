import React, { useState } from 'react';
import { useApi } from '../../auth/useApi';
import { messages } from '../../app/messages';
import type { CapabilityDecision } from '../../capabilities/capabilityTypes';
import { ActionGroup } from '../../components/ActionGroup';
import { ConfirmationDialog } from '../../components/ConfirmationDialog';
import { WorkspaceDataState } from '../../components/WorkspaceDataState';
import type { ApiFetch } from '../users/userDetailApi';
import { executeDeviceAction, type DeviceAction, type ManagedDevice } from './devicesApi';
import { devicesMessages } from './devicesMessages';
import './device360.css';

const rows = devicesMessages.deviceActions.rows;

export function DeviceActionsTab({ device, capabilities }: { device: ManagedDevice; capabilities: CapabilityDecision[] }) {
  const copy = devicesMessages.deviceActions;
  const api = useApi() as ApiFetch;
  const [actionTarget, setActionTarget] = useState<DeviceAction | null>(null);
  const [actionStatus, setActionStatus] = useState<string | null>(null);
  const [actionBusy, setActionBusy] = useState(false);
  const [actionError, setActionError] = useState(false);
  const canManage = capabilities.some(decision => decision.capability === 'devices.privileged.manage' && decision.state === 'allowed');

  const runAction = async () => {
    if (!actionTarget || actionBusy) return;
    setActionBusy(true); setActionError(false); setActionStatus(null);
    try {
      await executeDeviceAction(api, device.id, actionTarget);
      const label = rows.find(row => row.action === actionTarget)?.label ?? actionTarget;
      setActionStatus(copy.acceptedFeedback(label));
      setActionTarget(null);
    } catch {
      setActionError(true);
    } finally {
      setActionBusy(false);
    }
  };

  const confirmation = actionTarget ? messages.deviceActionConfirm[actionTarget] : null;
  const destructivePhrase = actionTarget === 'sync' ? null : actionTarget === 'remote-lock' ? 'REMOTE LOCK' : actionTarget?.toUpperCase() ?? null;
  const confirmationConsequence = actionTarget === 'remote-lock'
    ? 'Remote lock works only when the supported device already has a passcode or PIN; otherwise its screen may turn off while access remains possible.'
    : actionTarget === 'restart'
      ? 'The restart request depends on platform support and push connectivity. Offline devices or blocked push notifications can delay it, and unsaved work may be lost.'
      : actionTarget === 'retire'
        ? 'Requests removal of Intune management and company-managed data. Personal-data and identity-record outcomes vary by platform and management method.'
        : actionTarget === 'wipe'
          ? 'The request sets keepEnrollmentData=false and keepUserData=false. This does not request retention, but actual reset outcomes vary by supported platform and management method.'
          : confirmation?.consequence;

  return <div className="device360-actions">
    <div className="device360-section-heading">
      <div><h2>{copy.title}</h2><p>{copy.intro}</p></div>
    </div>
    <p className="device-action-acceptance" role="note">{copy.acceptedWarning} {copy.acceptanceDetails}</p>
    <div className="device360-table-scroll" role="region" aria-label={copy.title} tabIndex={0}>
      <table className="device360-table">
        <thead><tr>
          <th scope="col">{copy.actionColumn}</th>
          <th scope="col">{copy.remainsColumn}</th>
          <th scope="col">{copy.removedColumn}</th>
          <th scope="col">{copy.reenrollColumn}</th>
          <th scope="col">{copy.useColumn}</th>
        </tr></thead>
        <tbody>{rows.map(row => <tr key={row.action}>
          <th scope="row">{row.label}</th>
          <td>{row.remains}</td>
          <td>{row.removed}</td>
          <td>{row.reenrollment}</td>
          <td>{row.use}</td>
        </tr>)}</tbody>
      </table>
    </div>
    <p>{copy.docsLabel} <a href="https://learn.microsoft.com/en-us/intune/device-management/actions/sync" target="_blank" rel="noreferrer">Sync</a> · <a href="https://learn.microsoft.com/en-us/intune/device-management/actions/remote-lock" target="_blank" rel="noreferrer">Remote lock</a> · <a href="https://learn.microsoft.com/en-us/intune/device-management/actions/restart" target="_blank" rel="noreferrer">Restart</a> · <a href="https://learn.microsoft.com/en-us/intune/device-management/actions/retire" target="_blank" rel="noreferrer">Retire</a> · <a href="https://learn.microsoft.com/en-us/intune/device-management/actions/wipe" target="_blank" rel="noreferrer">Wipe</a></p>
    {canManage ? <>
      <ActionGroup title={copy.actionsTitle} description={copy.requestDescription}>
        <div className="device-actions">{rows.slice(0, 3).map(row => <div key={row.action} className="device-actions__item">
          <button type="button" className="button button--secondary" onClick={() => setActionTarget(row.action as DeviceAction)}>{row.label}</button>
          <span>{row.use}</span>
        </div>)}</div>
      </ActionGroup>
      {actionStatus && <p role="status">{actionStatus}</p>}
      {actionError && <p role="alert">{copy.failure}</p>}
      <ActionGroup tone="danger" title={copy.dangerZone}>
        <div className="device-actions">{rows.slice(3).map(row => <div key={row.action} className="device-actions__item">
          <button type="button" className="button button--danger" onClick={() => setActionTarget(row.action as DeviceAction)}>{row.label}</button>
          <span>{row.use}</span>
        </div>)}</div>
      </ActionGroup>
    </> : <WorkspaceDataState kind="permission" message={devicesMessages.device360.actionsCapabilityRequired} />}
    {actionTarget && confirmation && <ConfirmationDialog
      title={rows.find(row => row.action === actionTarget)?.label ?? actionTarget}
      target={device.deviceName || 'Unnamed device'}
      proposedChange={`${rows.find(row => row.action === actionTarget)?.label ?? actionTarget} this managed device.`}
      requiredCapability="devices.privileged.manage"
      confirmLabel={confirmation.label}
      consequence={confirmationConsequence}
      tone={confirmation.tone as 'default' | 'danger'}
      destructivePhrase={destructivePhrase}
      destructivePhraseLabel={destructivePhrase ? `Type ${destructivePhrase} to confirm` : undefined}
      busy={actionBusy}
      onConfirm={() => void runAction()}
      onCancel={() => { if (!actionBusy) setActionTarget(null); }}
    >
      {(actionTarget === 'retire' || actionTarget === 'wipe') && <p className="device-action-acceptance" role="note">{copy.acceptedWarning} {copy.acceptanceDetails}</p>}
    </ConfirmationDialog>}
  </div>;
}
