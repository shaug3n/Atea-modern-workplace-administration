import React, { useEffect, useState } from 'react';
import { messages } from '../../app/messages';
import { useApi } from '../../auth/useApi';
import type { CapabilityDecision } from '../../capabilities/capabilityTypes';
import { PermissionState } from '../../components/PermissionState';
import { fetchAssociatedDevices, type AssociatedDevicesResponse } from './userDetailApi';
import type { ApiFetch } from './userDetailApi';

export function AssociatedDevicesSection({ userId, decision }: { userId: string; decision: CapabilityDecision }) {
  const api = useApi();
  const [result, setResult] = useState<AssociatedDevicesResponse | null>(null);
  const [failed, setFailed] = useState(false);
  const readable = decision.state === 'allowed' || decision.state === 'read_only';

  useEffect(() => {
    if (!readable) { setResult(null); setFailed(false); return; }
    let cancelled = false;
    fetchAssociatedDevices(api as ApiFetch, userId).then((response) => { if (!cancelled) setResult(response); }).catch(() => { if (!cancelled) setFailed(true); });
    return () => { cancelled = true; };
  }, [api, readable, userId]);

  if (!readable) return <section className="detail-card associated-devices" aria-labelledby="associated-devices-title"><div className="detail-card__header"><h2 id="associated-devices-title">Associated devices</h2></div><PermissionState decision={decision}><p>{messages.devicesUnavailable}</p></PermissionState></section>;

  return <section className="detail-card associated-devices" aria-labelledby="associated-devices-title">
    <div className="detail-card__header"><h2 id="associated-devices-title">Associated devices</h2><span className="section-help">Managed devices linked to this user</span></div>
    {failed && <p role="alert">{messages.devicesUnavailable}</p>}
    {!failed && !result && <p role="status">{messages.devicesLoading}</p>}
    {result?.error && <p role="alert">{result.error.message || messages.devicesUnavailable}</p>}
    {result && !result.error && result.items.length === 0 && <p>{messages.devicesNoResults}</p>}
    {result && !result.error && result.items.length > 0 && <ul className="associated-devices__list">{result.items.map((device) => <li key={device.id}><div><strong>{device.deviceName || device.id}</strong><span>{device.operatingSystem || messages.devicesUnknown}</span></div><a href={`/devices?device=${encodeURIComponent(device.id)}`}>Open device {device.deviceName || device.id}</a></li>)}</ul>}
  </section>;
}
