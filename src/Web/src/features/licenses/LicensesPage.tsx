import { useCallback, useEffect, useState } from 'react';
import { useApi } from '../../auth/useApi';
import { messages } from '../../messages/en';

export type LicenseOverview = { items: Array<{ skuId: string; partNumber: string; displayName: string; assigned: number; available: number; affectedUsers?: Array<{ id: string; displayName: string }> }>; total: number; page: number; pageSize: number; fetchedAt: string; freshness: string; partialData: boolean; access: { state: string }; };
export type LicenseLoader = () => Promise<LicenseOverview>;

export function LicensesPage({ loadLicenses }: { loadLicenses?: LicenseLoader }) {
  if (loadLicenses) return <LoadedLicensesPage loader={loadLicenses} />;
  return <AuthenticatedLicensesPage />;
}

function AuthenticatedLicensesPage() {
  const api = useApi();
  const loader = useCallback(async () => { const response = await api('/api/licenses'); if (!response.ok) throw new Error('license overview failed'); return await response.json() as LicenseOverview; }, [api]);
  return <LoadedLicensesPage loader={loader} />;
}

function LoadedLicensesPage({ loader }: { loader: LicenseLoader }) {
  const [result, setResult] = useState<LicenseOverview | null>(null); const [failed, setFailed] = useState(false); const [retry, setRetry] = useState(0);
  useEffect(() => { let cancelled = false; setFailed(false); loader().then(value => { if (!cancelled) setResult(value); }).catch(() => { if (!cancelled) setFailed(true); }); return () => { cancelled = true; }; }, [loader, retry]);
  if (failed) return <section className="content-panel"><p role="alert">License data is unavailable. Try again later.</p><button type="button" onClick={() => setRetry(value => value + 1)}>{messages.retry}</button></section>;
  if (!result) return <section className="content-panel"><p role="status">Loading licenses…</p></section>;
  if (result.access.state !== 'allowed' && result.access.state !== 'read_only') return <section className="content-panel"><h1>{messages.permissionRequiredTitle}</h1><p>{messages.permissionRequiredBody}</p></section>;
  return <section className="content-panel" aria-labelledby="licenses-title"><p className="eyebrow">Licenses</p><h1 id="licenses-title">Licenses</h1>{result.items.length === 0 ? <p>No license data is available.</p> : <div className="users-table-wrap"><table className="users-table"><thead><tr><th>License</th><th>Assigned</th><th>Available</th><th>Affected users</th></tr></thead><tbody>{result.items.map(item => <tr key={item.skuId}><td>{item.displayName}</td><td>{item.assigned}</td><td>{item.available}</td><td>{(item.affectedUsers ?? []).map(user => <a key={user.id} href={`/users/${user.id}`}>{user.displayName}</a>)}</td></tr>)}</tbody></table></div>}</section>;
}
