import { useCallback, useEffect, useState } from 'react';
import { useApi } from '../../auth/useApi';
import { messages } from '../../messages/en';
import type { ApiFetch, UsersDirectoryResponse } from '../users/usersApi';
import { downloadCsv, exportStatus, type CsvExportInfo } from '../exports/csvExport';
import { WorkspacePageHeader } from '../../components/WorkspacePageHeader';
import { DataFreshness } from '../../components/DataFreshness';
import { TechnicalDetails } from '../../components/TechnicalDetails';
import { ResponsiveDataView } from '../../components/ResponsiveDataView';
import { useWorkspaceIssueReporter } from '../../notifications/WorkspaceNotifications';

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
  const issueReporter = useWorkspaceIssueReporter();
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
    loader(search, page).then(value => { if (!cancelled) { setResult(value); setLoadedQueryKey(queryKey); if (value.error && (value.access.state === 'allowed' || value.access.state === 'read_only')) issueReporter.report({ key: 'licenses:read', area: 'licenses', kind: 'service', severity: 'warning', title: 'License data unavailable', detail: 'Try loading licenses again.' }); else issueReporter.clear('licenses:read'); } }).catch(() => { if (!cancelled) { setFailed(true); issueReporter.report({ key: 'licenses:read', area: 'licenses', kind: 'service', severity: 'warning', title: 'License data unavailable', detail: 'Try loading licenses again.' }); } });
    return () => { cancelled = true; };
  }, [loader, search, page, retry, queryKey, issueReporter]);

  useEffect(() => {
    if (tab !== 'assignees' || !selected || !loadAssignees) return;
    let cancelled = false;
    setAssigneeError(false);
    setAssigneeLoading(true);
    setAssignees(null);
    loadAssignees(selected.skuId, token)
      .then(value => { if (!cancelled) { setAssignees(value); if (value.error) issueReporter.report({ key: 'licenses:assignees', area: 'licenses', kind: 'service', severity: 'warning', title: 'License roster unavailable', detail: 'Try loading assigned users again.' }); else issueReporter.clear('licenses:assignees'); } })
      .catch(() => { if (!cancelled) { setAssigneeError(true); issueReporter.report({ key: 'licenses:assignees', area: 'licenses', kind: 'service', severity: 'warning', title: 'License roster unavailable', detail: 'Try loading assigned users again.' }); } })
      .finally(() => { if (!cancelled) setAssigneeLoading(false); });
    return () => { cancelled = true; };
  }, [tab, selected, token, loadAssignees, assigneeRetry, issueReporter]);

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

  return <section className="licenses-page">
    <WorkspacePageHeader eyebrow="Licenses" title="License inventory" description="Review purchased seats, assignments, and license rosters." meta={result && loadedQueryKey === queryKey ? <DataFreshness fetchedAt={result.fetchedAt} freshness={result.freshness === 'live' ? 'fresh' : result.freshness === 'stale' ? 'stale' : result.freshness === 'unavailable' ? 'unavailable' : 'fresh'} partialData={result.partialData} source="Microsoft Graph" onRefresh={() => setRetry(value => value + 1)} /> : undefined} />
    {failed ? <div className="license-state"><p role="alert">License data is unavailable. Try again later.</p><button type="button" onClick={() => setRetry(value => value + 1)}>{messages.retry}</button></div>
    : !result || loadedQueryKey !== queryKey ? <p role="status">Loading licenses…</p>
    : result.access.state !== 'allowed' && result.access.state !== 'read_only' ? <div className="license-state"><h2>{messages.permissionRequiredTitle}</h2><p>{messages.permissionRequiredBody}</p></div>
    : result.error && result.items.length === 0 ? <div className="license-state"><p role="alert">License data is unavailable. {result.error.message}</p><button type="button" onClick={() => setRetry(value => value + 1)}>{messages.retry}</button></div>
    : <>
    {result.partialData && <p role="alert" className="license-partial">Some license data could not be loaded. Showing available results.</p>}
    <div className="license-tabs" role="tablist" aria-label="License view">
      <button type="button" role="tab" aria-selected={tab === 'inventory'} onClick={() => setTab('inventory')}>Inventory</button>
      <button type="button" role="tab" aria-selected={tab === 'assignees'} onClick={showAssignees} disabled={result.items.length === 0 && !selected}>Assigned users</button>
    </div>
    {tab === 'inventory' ? <section role="tabpanel" aria-label="Inventory">
      <div className="license-toolbar"><label>Search SKUs <input value={search} onChange={event => { setSearch(event.target.value); setPage(1); }} /></label>
      {exportCsv && <button type="button" onClick={() => void runExport('inventory')} disabled={exportPending}>Export filtered CSV</button>}</div>
      {result.items.length === 0 ? <p>No license data is available.</p> : <ResponsiveDataView items={result.items} keyOf={item => item.skuId} label="Licenses"
        renderCompact={item => <><strong>{licenseName(item)}</strong>{unrecognised(item) && <span className="license-unavailable">Unrecognised product</span>}<LicenseUsage item={item} /><dl className="responsive-data-view__details license-item-counts"><div><dt>Purchased</dt><dd>{item.purchased}</dd></div><div><dt>Assigned</dt><dd>{item.assigned}</dd></div><div><dt>Available</dt><dd>{item.available}</dd></div></dl><div className="responsive-data-view__actions"><button type="button" className="button button--secondary button--sm" aria-label={`View assigned users for ${licenseName(item)}`} onClick={() => choose(item)}>View assigned users</button></div><LicenseIdentifiers item={item} /></>}
        renderTable={items => <div className="users-table-wrap"><table className="users-table"><thead><tr><th scope="col">License</th><th scope="col" className="numeric">Purchased</th><th scope="col" className="numeric">Assigned</th><th scope="col" className="numeric">Available</th><th scope="col">Usage</th><th scope="col"><span className="sr-only">Roster</span></th></tr></thead><tbody>{items.map(item => <tr key={item.skuId}><th scope="row" data-label="License"><strong>{licenseName(item)}</strong>{unrecognised(item) && <span className="license-unavailable">Unrecognised product</span>}<LicenseIdentifiers item={item} /></th><td className="numeric" data-label="Purchased">{item.purchased}</td><td className="numeric" data-label="Assigned">{item.assigned}</td><td className="numeric" data-label="Available">{item.available}</td><td data-label="Usage"><LicenseUsage item={item} /></td><td data-label="Roster"><button type="button" className="button button--secondary button--sm" aria-label={`View assigned users for ${licenseName(item)}`} onClick={() => choose(item)}>View assigned users</button></td></tr>)}</tbody></table></div>}
      />}
      <nav className="table-pagination" aria-label="License pages"><span>Page {page} of {Math.max(1, Math.ceil(result.total / result.pageSize))}</span><button type="button" onClick={() => setPage(value => value - 1)} disabled={page <= 1}>Previous page</button><button type="button" onClick={() => setPage(value => value + 1)} disabled={page * result.pageSize >= result.total}>Next page</button></nav>
    </section> : <section role="tabpanel" aria-label="Assigned users">
      {result.items.length > 0 && <label className="license-select">License <select value={selected?.skuId ?? ''} onChange={event => { const next = result.items.find(item => item.skuId === event.target.value); if (next) choose(next); }}>{result.items.map(item => <option key={item.skuId} value={item.skuId}>{licenseName(item)}</option>)}</select></label>}
      {selected && <><h2>Users assigned {licenseName(selected)}</h2><dl className="license-counts"><div><dt>Purchased</dt><dd>{selected.purchased}</dd></div><div><dt>Assigned</dt><dd>{selected.assigned}</dd></div><div><dt>Available</dt><dd>{selected.available}</dd></div></dl>
      {exportCsv && <button type="button" onClick={() => void runExport('assignees')} disabled={exportPending}>Export assignees CSV</button>}
      {!loadAssignees ? <p>Assigned-user roster is unavailable.</p> : <>
        {assigneeLoading ? <p role="status">Loading assigned users…</p> : assigneeError || assignees?.error ? <><p role="alert">Assigned-user roster is unavailable.</p><button type="button" onClick={retryAssignees}>{messages.retry}</button></> : assignees ? <>{assignees.items.length === 0 ? <p>No assigned users were returned.</p> : <div className="users-table-wrap"><table className="users-table"><thead><tr><th>User</th><th>User principal name</th></tr></thead><tbody>{assignees.items.map(user => <tr key={user.id}><th scope="row" data-label="User"><a href={`/users/${encodeURIComponent(user.id)}`}>{user.displayName || user.userPrincipalName || user.id}</a></th><td data-label="User principal name">{user.userPrincipalName}</td></tr>)}</tbody></table></div>}</> : null}
        <nav className="table-pagination" aria-label="Assignee pages"><span>Page {history.length + 1}</span><button type="button" onClick={previousAssignees} disabled={assigneeLoading || assigneeError || !!assignees?.error || history.length === 0}>Previous page</button><button type="button" onClick={nextAssignees} disabled={assigneeLoading || assigneeError || !assignees?.continuationToken}>Next page</button></nav>
      </>}
      </>}
    </section>}
    {exportMessage && <p role="status">{exportMessage}</p>}{exportError && <p role="alert">{exportError}</p>}
  </>}
  </section>;
}

function unrecognised(item: LicenseItem) {
  return !item.displayName || item.displayName === item.partNumber;
}

function licenseName(item: LicenseItem) {
  return item.displayName || item.partNumber || 'Unnamed license';
}

function LicenseIdentifiers({ item }: { item: LicenseItem }) {
  return <TechnicalDetails items={[{ label: 'Part number', value: item.partNumber, copy: true }, { label: 'SKU ID', value: item.skuId, copy: true }]} />;
}

function LicenseUsage({ item }: { item: LicenseItem }) {
  const total = item.purchased;
  const pct = total > 0 ? Math.round((item.assigned / total) * 100) : 0;
  const full = item.available === 0;
  return <div className={`license-usage${pct >= 90 ? ' license-usage--high' : ''}`}>
    <meter min={0} max={Math.max(total, 1)} value={Math.min(item.assigned, Math.max(total, 1))} aria-label={`${licenseName(item)} usage`} />
    <span>{full && total > 0 ? 'Fully assigned' : `${item.assigned} of ${total} (${pct}%)`}</span>
  </div>;
}
