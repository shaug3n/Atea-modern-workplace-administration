import React from 'react';
import { InfoTip } from '../../components/InfoTip';
import { LoadingSkeleton } from '../../components/LoadingSkeleton';
import { ProvenanceChip } from '../../components/ProvenanceChip';
import { StatusBadge } from '../../components/StatusBadge';
import { TechnicalDetails } from '../../components/TechnicalDetails';
import { WorkspaceDataState } from '../../components/WorkspaceDataState';
import { formatDateTime } from '../../format/dateTime';
import './device360.css';
import type { ManagedDevice } from './devicesApi';
import type { Device360Response, DeviceCompliancePolicyState } from './device360Api';
import { devicesMessages } from './devicesMessages';

export function Device360OverviewTab({ device, policies, policyLoading, onRetryPolicies, onNavigate }: { device: ManagedDevice; policies: Device360Response<DeviceCompliancePolicyState[]>; policyLoading?: boolean; onRetryPolicies?: () => void; onNavigate?: (path: string) => void }) {
  const copy = devicesMessages.device360Overview;
  const complianceLabel = device.complianceState ? humanizeCompliance(device.complianceState) : devicesMessages.device360Overview.unknownCompliance;
  const complianceTone = device.complianceState?.toLowerCase() === 'compliant'
    ? 'success'
    : device.complianceState?.toLowerCase() === 'noncompliant' ? 'warning' : 'neutral';

  return <div className="device360-overview">
    <div className="device360-overview__grid">
      <section className="content-panel" aria-labelledby="device360-essentials-title">
        <h2 id="device360-essentials-title">{copy.essentialsTitle}</h2>
        <dl className="device360-facts">
          <Fact label={copy.deviceName} value={device.deviceName || copy.unnamedDevice} source="managedDevice.deviceName" />
          <Fact label={copy.ownership} value={reportedOwnershipLabel(device.managedDeviceOwnerType)} source="managedDevice.managedDeviceOwnerType" />
          <Fact label={copy.primaryUser} value={device.userId ? device.userDisplayName || devicesMessages.device360.primaryUserUnavailable : devicesMessages.device360.noPrimaryUser} source="managedDevice.userDisplayName">
            {device.userId && device.userDisplayName
              ? <a href={`/users/${encodeURIComponent(device.userId)}`} onClick={event => { if (onNavigate) { event.preventDefault(); onNavigate(`/users/${encodeURIComponent(device.userId!)}`); } }}>{device.userDisplayName}</a>
              : undefined}
          </Fact>
          <Fact label={copy.userDomain} value={reportedUserDomain(device.userPrincipalName)} source="managedDevice.userPrincipalName" />
          <Fact label={copy.lastCheckIn} value={device.lastSyncDateTime ? formatDateTime(device.lastSyncDateTime) : devicesMessages.device360.unknownValue} source="managedDevice.lastSyncDateTime" />
          <div className="device360-fact">
            <dt>{copy.overallCompliance} <InfoTip label={copy.complianceHelpLabel} content={copy.complianceHelp} /></dt>
            <dd><StatusBadge tone={complianceTone} label={complianceLabel} /></dd>
          </div>
          <Fact label={copy.managementState} value={device.managementState || null} />
          <Fact label={copy.operatingSystem} value={[device.operatingSystem, device.osVersion].filter(Boolean).join(' ') || null} />
          <Fact label={copy.reportedEncryption} value={device.isEncrypted == null ? devicesMessages.device360.unknownValue : device.isEncrypted ? copy.yes : copy.no} />
          <Fact label={copy.manufacturer} value={device.manufacturer} />
          <Fact label={copy.model} value={device.model} />
        </dl>
      </section>

      <section className="content-panel device360-policies" aria-labelledby="device360-policies-title">
        <div className="device360-section-heading">
          <div>
            <h2 id="device360-policies-title">{copy.perPolicyCompliance}</h2>
            <p>{copy.policyDescription}</p>
          </div>
          {policies.retrievedAt && <p className="device360-retrieved">{copy.policiesRetrieved} {formatDateTime(policies.retrievedAt)}</p>}
        </div>
        <PolicyState policies={policies} loading={policyLoading} onRetry={onRetryPolicies} />
      </section>
    </div>

    <section className="content-panel device360-portal" aria-labelledby="device360-portal-title">
      <div>
        <h2 id="device360-portal-title">{copy.portalTitle}</h2>
        <p>{copy.portalDescription}</p>
      </div>
      <a className="button button--secondary" href="https://intune.microsoft.com/" target="_blank" rel="noreferrer">{copy.portalLink}</a>
    </section>

    <TechnicalDetails items={[
      { label: copy.managedDeviceId, value: device.id, copy: true },
      { label: copy.primaryUserId, value: device.userId, copy: true },
      { label: copy.entraDeviceId, value: device.azureAdDeviceId, copy: true },
      { label: copy.serialNumber, value: device.serialNumber, copy: true },
    ]} summary={copy.technicalDetails} />
  </div>;
}

function Fact({ label, value, source, children }: { label: string; value?: string | null; source?: string; children?: React.ReactNode }) {
  return <div className="device360-fact">
    <dt>{label}</dt>
    <dd>{children ?? value ?? devicesMessages.device360Overview.unavailable}</dd>
    {source && <ProvenanceChip source={source} />}
  </div>;
}

function PolicyState({ policies, loading, onRetry }: { policies: Device360Response<DeviceCompliancePolicyState[]>; loading?: boolean; onRetry?: () => void }) {
  const copy = devicesMessages.device360Overview;
  if (loading) return <LoadingSkeleton label={copy.policiesLoading} lines={2} />;

  if (policies.status === 'succeeded' || policies.status === 'partial') {
    const rows = Array.isArray(policies.data) ? policies.data : null;
    if (!rows) return <WorkspaceDataState kind="unavailable" compact message={copy.policyUnreadable} onRetry={onRetry} retryLabel={copy.retryPolicies} />;
    return <>
      {(policies.status === 'partial' || policies.partialData) && <WorkspaceDataState kind="partial" compact message={copy.policyResultsPartial} />}
      {rows.length === 0
        ? <WorkspaceDataState kind="empty" compact message={copy.policiesEmpty} />
        : <ul className="device360-policies__list">{rows.map(policy => (
          <li key={policy.id}>
            <strong>{policy.displayName || copy.policyNameUnavailable}</strong>
            <span>{policy.state ? humanizeCompliance(policy.state) : copy.stateUnavailable}</span>
            {policy.platformType && <span>{policy.platformType}</span>}
          </li>
        ))}</ul>}
    </>;
  }

  if (policies.status === 'no_reported_policies') return <WorkspaceDataState kind="empty" compact message={copy.noReportedPolicies} />;
  if (policies.status === 'unsupported') return <WorkspaceDataState kind="unavailable" compact message={copy.policyUnsupported} />;

  const message = policies.status === 'graph_forbidden'
    ? copy.policyForbidden
    : policies.status === 'missing_scope'
      ? copy.policyMissingScope
      : policies.status === 'consent_required'
        ? copy.policyConsentRequired
      : policies.status === 'capability_required'
        ? copy.policyCapabilityRequired
        : policies.status === 'device_not_found'
          ? copy.policyDeviceNotFound
          : policies.status === 'throttled'
            ? `${copy.policyThrottled}${policies.retryAfterSeconds ? `. Retry after ${policies.retryAfterSeconds} seconds` : '. Retry later'}.`
            : copy.policyUnavailable;
  return <WorkspaceDataState kind="unavailable" compact message={message} onRetry={onRetry} retryLabel={copy.retryPolicies} />;
}

export function reportedOwnershipLabel(value?: string | null) {
  switch (value?.toLowerCase()) {
    case 'company': return devicesMessages.device360.companyOwned;
    case 'personal': return devicesMessages.device360.personallyOwned;
    default: return devicesMessages.device360.ownershipUnknown;
  }
}

export function reportedUserDomain(upn?: string | null) {
  if (!upn) return devicesMessages.device360.unavailableValue;
  const match = /^[^@\s]+@((?:[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?)(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?)+)$/.exec(upn);
  return match?.[1] ?? devicesMessages.device360.unavailableValue;
}

function humanizeCompliance(state: string) {
  const normalized = state.toLowerCase();
  if (normalized === 'compliant') return devicesMessages.device360Overview.compliant;
  if (normalized === 'noncompliant') return devicesMessages.device360Overview.noncompliant;
  if (normalized === 'ingraceperiod') return devicesMessages.device360Overview.gracePeriod;
  if (normalized === 'unknown') return devicesMessages.device360Overview.unknownCompliance;
  return state;
}
