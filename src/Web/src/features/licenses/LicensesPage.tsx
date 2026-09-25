import { useCallback, useEffect, useState } from 'react';
import { useApi } from '../../auth/useApi';
import { messages } from '../../messages/en';
import type { ApiFetch, UsersDirectoryResponse } from '../users/usersApi';
import { downloadCsv, exportStatus, type CsvExportInfo } from '../exports/csvExport';

export type LicenseItem = { skuId: string; partNumber: string; displayName: string; purchased: number; assigned: number; available: number };
export type LicenseOverview = { items: LicenseItem[]; total: number; page: number; pageSize: number; fetchedAt: string; freshness: string; partialData: boolean; access: { state: string }; error?: { message: string } | null };
export type LicenseLoader = (search?: string, page?: number) => Promise<LicenseOverview>;
export type AssigneeLoader = (skuId: string, token: string | null) => Promise<UsersDirectoryResponse>;
type Exporter = (path: string, fileName: string) => Promise<CsvExportInfo>;

export function LicensesPage({ loadLicenses, loadAssignees, exportCsv }: { loadLicenses?: LicenseLoader; loadAssignees?: AssigneeLoader; exportCsv?: Exporter }) {
  if (loadLicenses) return <LoadedLicensesPage loader={loadLicenses} loadAssignees={loadAssignees} exportCsv={exportCsv} />;
  return <AuthenticatedLicensesPage />;
}

function AuthenticatedLicensesPage() {
  const api = useApi() as ApiFetch;
  const loader = useCallback(async (search = '', page = 1) => {
    const params = new URLSearchParams({ page: String(page) });
    if (search.trim()) params.set('search', search.trim());
    const response = await api(`/api/licenses?${params}`);
    if (!response.ok) throw new Error('license overview failed');
    return await response.json() as LicenseOverview;
  }, [api]);
  const assignees = useCallback(async (skuId: string, token: string | null) => {
    const params = new URLSearchParams();
    if (token) params.set('continuationToken', token);
    const response = await api(`/api/licenses/${encodeURIComponent(skuId)}/assignees${params.size ? `?${params}` : ''}`);
    if (!response.ok) throw new Error('license assignees failed');
    return await response.json() as UsersDirectoryResponse;
  }, [api]);
  const exporter = useCallback((path: string, name: string) => downloadCsv(api, path, name), [api]);
  return <LoadedLicensesPage loader={loader} loadAssignees={assignees} exportCsv={exporter} />;
}

function LoadedLicensesPage({ loader, loadAssignees, exportCsv }: { loader: LicenseLoader; loadAssignees?: AssigneeLoader; exportCsv?: Exporter }) {
  const [result, setResult] = useState<LicenseOverview | null>(null);
  const [loadedQueryKey, setLoadedQueryKey] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);
  const [retry, setRetry] = useState(0);
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<LicenseItem | null>(null);
  const [tab, setTab] = useState<'inventory' | 'assignees'>('inventory');
  const [assignees, setAssignees] = useState<UsersDirectoryResponse | null>(null);
  const [assigneeError, setAssigneeError] = useState(false);
  const [assigneeLoading, setAssigneeLoading] = useState(false);
  const [assigneeRetry, setAssigneeRetry] = useState(0);
  const [token, setToken] = useState<string | null>(null);
  const [history, setHistory] = useState<string[]>([]);
  const [exportMessage, setExportMessage] = useState<string | null>(null);
  const [exportError, setExportError] = useState<string | null>(null);
  const [exportPending, setExportPending] = useState(false);
  const queryKey = `${search}\u0000${page}`;

  useEffect(() => {
    let cancelled = false;
    setFailed(false);
    loader(search, page).then(value => { if (!cancelled) { setResult(value); setLoadedQueryKey(queryKey); } }).catch(() => { if (!cancelled) setFailed(true); });
    return () => { cancelled = true; };
  }, [loader, search, page, retry, queryKey]);

  useEffect(() => {
    if (tab !== 'assignees' || !selected || !loadAssignees) return;
    let cancelled = false;
    setAssigneeError(false);
    setAssigneeLoading(true);
    setAssignees(null);
    loadAssignees(selected.skuId, token)
      .then(value => { if (!cancelled) setAssignees(value); })
      .catch(() => { if (!cancelled) setAssigneeError(true); })
      .finally(() => { if (!cancelled) setAssigneeLoading(false); });
    return () => { cancelled = true; };
  }, [tab, selected, token, loadAssignees, assigneeRetry]);

  const choose = (item: LicenseItem) => {
    setSelected(item); setToken(null); setHistory([]); setAssignees(null); setAssigneeLoading(true); setTab('assignees');
  };
  const showAssignees = () => { if (!selected && result?.items[0]) setSelected(result.items[0]); setTab('assignees'); };
  const previousAssignees = () => {
    if (assigneeLoading || assigneeError || assignees?.error || history.length === 0) return;
    const previous = [...history];
    setAssigneeLoading(true); setAssignees(null);
    setToken(previous.pop() || null); setHistory(previous);
  };
  const nextAssignees = () => {
    if (assigneeLoading || assigneeError || !assignees?.continuationToken) return;
    setAssigneeLoading(true); setAssignees(null);
    setHistory(values => [...values, token ?? '']); setToken(assignees.continuationToken);
  };
  const retryAssignees = () => {
    if (assigneeLoading) return;
    setAssigneeLoading(true); setAssigneeError(false); setAssignees(null);
    setAssigneeRetry(value => value + 1);
  };
  const runExport = async (kind: 'inventory' | 'assignees') => {
    if (!exportCsv || exportPending || (kind === 'assignees' && !selected)) return;
    setExportPending(true); setExportMessage(null); setExportError(null);
    try {
      const params = new URLSearchParams();
      if (kind === 'inventory' && search.trim()) params.set('search', search.trim());
      const path = kind === 'inventory' ? `/api/licenses/export.csv${params.size ? `?${params}` : ''}` : `/api/licenses/${encodeURIComponent(selected!.skuId)}/assignees/export.csv`;
      setExportMessage(exportStatus(await exportCsv(path, kind === 'inventory' ? 'licenses.csv' : 'license-assignees.csv')));
    } catch { setExportError('License export failed. Check your permissions and try again.'); }
    finally { setExportPending(false); }
  };

  if (failed) return <section className="content-panel"><p role="alert">License data is unavailable. Try again later.</p><button type="button" onClick={() => setRetry(value => value + 1)}>{messages.retry}</button></section>;
  if (!result || loadedQueryKey !== queryKey) return <section className="content-panel"><p role="status">Loading licenses…</p></section>;
  if (result.access.state !== 'allowed' && result.access.state !== 'read_only') return <section className="content-panel"><h1>{messages.permissionRequiredTitle}</h1><p>{messages.permissionRequiredBody}</p></section>;
  if (result.error) return <section className="content-panel"><h1>Licenses</h1><p role="alert">{result.error.message}</p><button type="button" onClick={() => setRetry(value => value + 1)}>{messages.retry}</button></section>;

  return <section className="content-panel licenses-page" aria-labelledby="licenses-title">
    <p className="eyebrow">Licenses</p><h1 id="licenses-title">License inventory</h1>
    <p>Source: Microsoft Graph subscribed SKUs. Retrieved {new Date(result.fetchedAt).toLocaleString()}.</p>
    <div className="license-tabs" role="tablist" aria-label="License view">
      <button type="button" role="tab" aria-selected={tab === 'inventory'} onClick={() => setTab('inventory')}>Inventory</button>
      <button type="button" role="tab" aria-selected={tab === 'assignees'} onClick={showAssignees} disabled={result.items.length === 0 && !selected}>Assigned users</button>
    </div>
    {tab === 'inventory' ? <section role="tabpanel" aria-label="Inventory">
      <div className="license-toolbar"><label>Search SKUs <input value={search} onChange={event => { setSearch(event.target.value); setPage(1); }} /></label>
      {exportCsv && <button type="button" onClick={() => void runExport('inventory')} disabled={exportPending}>Export filtered CSV</button>}</div>
      {result.items.length === 0 ? <p>No license data is available.</p> : <div className="users-table-wrap"><table className="users-table"><thead><tr><th>License</th><th>Purchased</th><th>Assigned</th><th>Available</th><th>Roster</th></tr></thead><tbody>{result.items.map(item => <tr key={item.skuId}><th scope="row" data-label="License">{item.displayName}<small>{item.skuId}</small></th><td data-label="Purchased">{item.purchased}</td><td data-label="Assigned">{item.assigned}</td><td data-label="Available">{item.available}</td><td data-label="Roster"><button type="button" onClick={() => choose(item)}>View assigned users</button></td></tr>)}</tbody></table></div>}
      <nav className="table-pagination" aria-label="License pages"><span>Page {page} of {Math.max(1, Math.ceil(result.total / result.pageSize))}</span><button type="button" onClick={() => setPage(value => value - 1)} disabled={page <= 1}>Previous page</button><button type="button" onClick={() => setPage(value => value + 1)} disabled={page * result.pageSize >= result.total}>Next page</button></nav>
    </section> : <section role="tabpanel" aria-label="Assigned users">
      {selected && <><h2>{selected.displayName}</h2><dl className="license-counts"><div><dt>Purchased</dt><dd>{selected.purchased}</dd></div><div><dt>Assigned</dt><dd>{selected.assigned}</dd></div><div><dt>Available</dt><dd>{selected.available}</dd></div></dl>
      {exportCsv && <button type="button" onClick={() => void runExport('assignees')} disabled={exportPending}>Export assignees CSV</button>}
      {!loadAssignees ? <p>Assigned-user roster is unavailable.</p> : <>
        {assigneeLoading ? <p role="status">Loading assigned users…</p> : assigneeError || assignees?.error ? <><p role="alert">Assigned-user roster is unavailable.</p><button type="button" onClick={retryAssignees}>{messages.retry}</button></> : assignees ? <><p>Source: Microsoft Graph users assigned to this SKU. Retrieved {new Date(assignees.fetchedAt).toLocaleString()}.</p>{assignees.items.length === 0 ? <p>No assigned users were returned.</p> : <div className="users-table-wrap"><table className="users-table"><thead><tr><th>User</th><th>User principal name</th></tr></thead><tbody>{assignees.items.map(user => <tr key={user.id}><th scope="row" data-label="User"><a href={`/users/${encodeURIComponent(user.id)}`}>{user.displayName || user.userPrincipalName || user.id}</a></th><td data-label="User principal name">{user.userPrincipalName}</td></tr>)}</tbody></table></div>}</> : null}
        <nav className="table-pagination" aria-label="Assignee pages"><span>Page {history.length + 1}</span><button type="button" onClick={previousAssignees} disabled={assigneeLoading || assigneeError || !!assignees?.error || history.length === 0}>Previous page</button><button type="button" onClick={nextAssignees} disabled={assigneeLoading || assigneeError || !assignees?.continuationToken}>Next page</button></nav>
      </>}
      </>}
    </section>}
    {exportMessage && <p role="status">{exportMessage}</p>}{exportError && <p role="alert">{exportError}</p>}
  </section>;
}
