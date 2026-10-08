import React, { useEffect, useState } from 'react';
import { messages } from '../../app/messages';
import { useApi } from '../../auth/useApi';
import type { CapabilityDecision } from '../../capabilities/capabilityTypes';
import { PermissionState } from '../../components/PermissionState';
import { LoadingSkeleton } from '../../components/LoadingSkeleton';
import { fetchAssociatedDevices, type AssociatedDevicesResponse } from './userDetailApi';
import type { ApiFetch } from './userDetailApi';

export function AssociatedDevicesSection({ userId, decision }: { userId: string; decision: CapabilityDecision }) {
  const api = useApi();
  const [result, setResult] = useState<AssociatedDevicesResponse | null>(null);
  const [failed, setFailed] = useState(false);
  const [loading, setLoading] = useState(false);
  const [retryVersion, setRetryVersion] = useState(0);
  const readable = decision.state === 'allowed' || decision.state === 'read_only';

  useEffect(() => {
    if (!readable) { setResult(null); setFailed(false); setLoading(false); return; }
    let cancelled = false;
    setResult(null);
    setFailed(false);
    setLoading(true);
    fetchAssociatedDevices(api as ApiFetch, userId)
      .then((response) => { if (!cancelled) { setResult(response); setLoading(false); } })
      .catch(() => { if (!cancelled) { setFailed(true); setLoading(false); } });
    return () => { cancelled = true; };
  }, [api, readable, userId, retryVersion]);

  if (!readable) return <section className="detail-card associated-devices" aria-labelledby="associated-devices-title" aria-busy={false}><div className="detail-card__header"><h2 id="associated-devices-title">Associated devices</h2></div><PermissionState decision={decision}><p>{messages.devicesUnavailable}</p></PermissionState></section>;

  return <section className="detail-card associated-devices" aria-labelledby="associated-devices-title" aria-busy={loading}>
    <div className="detail-card__header"><h2 id="associated-devices-title">Associated devices</h2><span className="section-help">Managed devices linked to this user</span></div>
    {loading && <LoadingSkeleton label={messages.devicesLoading} lines={3} />}
    {failed && <><p role="alert">{messages.devicesUnavailable}</p><button type="button" className="button button--secondary button--sm" onClick={() => setRetryVersion(version => version + 1)}>{messages.userProfileRetryDevices}</button></>}
    {result?.error && <><p role="alert">{messages.devicesUnavailable}</p><button type="button" className="button button--secondary button--sm" onClick={() => setRetryVersion(version => version + 1)}>{messages.userProfileRetryDevices}</button></>}
    {result && !result.error && result.items.length === 0 && <p>{messages.devicesNoResults}</p>}
    {result && !result.error && result.items.length > 0 && <ul className="associated-devices__list">{result.items.map((device) => <li key={device.id}><div><strong>{device.deviceName || device.id}</strong><span>{device.operatingSystem || messages.devicesUnknown}</span></div><a href={`/devices?device=${encodeURIComponent(device.id)}`}>Open device {device.deviceName || device.id}</a></li>)}</ul>}
  </section>;
}
