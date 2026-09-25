import { useEffect, useRef, useState } from 'react';
import { useApi } from '../../auth/useApi';
import { WorkspacePageHeader } from '../../components/WorkspacePageHeader';
import { WorkspaceDataState } from '../../components/WorkspaceDataState';
import { ResponsiveDataView } from '../../components/ResponsiveDataView';
import { useWorkspaceIssueReporter } from '../../notifications/WorkspaceNotifications';

type Mailbox = { userId: string; displayName: string; address?: string | null };
type MailboxResponse = { items: Mailbox[]; continuationToken?: string | null; isCompleteExchangeInventory?: boolean; limitation?: string };
type MailboxVerification = {
  userId: string;
  verificationStatus: string;
  mailboxType?: string | null;
  settings?: { timeZone?: string | null; language?: string | null; dateFormat?: string | null; timeFormat?: string | null; automaticRepliesStatus?: string | null } | null;
  error?: { category: string; message: string } | null;
};
type VerificationState = { status: 'loading' } | { status: 'loaded'; value: MailboxVerification; retrievedAt: string } | { status: 'error'; message: string };

export function ExchangeOverviewPage() {
  const api = useApi();
  const issueReporter = useWorkspaceIssueReporter();
  const [draftSearch, setDraftSearch] = useState('');
  const [query, setQuery] = useState({ search: '', token: null as string | null, page: 1 });
  const [previousTokens, setPreviousTokens] = useState<Array<string | null>>([]);
  const [result, setResult] = useState<MailboxResponse | null>(null);
  const [retrievedAt, setRetrievedAt] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  const [retry, setRetry] = useState(0);
  const [details, setDetails] = useState<Record<string, VerificationState>>({});
  const detailsGeneration = useRef(0);
  useEffect(() => {
    let cancelled = false;
    detailsGeneration.current += 1;
    setLoading(true); setFailed(false); setResult(null); setRetrievedAt(null); setDetails({});
    const parameters = new URLSearchParams({ search: query.search, pageSize: '25' });
    if (query.token) parameters.set('continuationToken', query.token);
    api(`/api/exchange/mailboxes?${parameters}`)
      .then(async response => {
        if (!response.ok) throw new Error('exchange unavailable');
        const value = await response.json() as MailboxResponse;
        if (!cancelled) { setResult(value); setRetrievedAt(new Date().toISOString()); issueReporter.clear('exchange:directory'); }
      })
      .catch(() => { if (!cancelled) { setFailed(true); issueReporter.report({ key: 'exchange:directory', area: 'services', kind: 'service', severity: 'warning', title: 'Exchange directory unavailable', detail: 'Try loading mailboxes again.' }); } })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; detailsGeneration.current += 1; };
  }, [api, query, retry, issueReporter]);
  const submitSearch = () => {
    const search = draftSearch.trim();
    setPreviousTokens([]);
    if (search === query.search && !query.token) setRetry(value => value + 1);
    else setQuery({ search, token: null, page: 1 });
  };
  const openDetails = async (mailbox: Mailbox) => {
    const generation = detailsGeneration.current;
    setDetails(current => ({ ...current, [mailbox.userId]: { status: 'loading' } }));
    try {
      const response = await api(`/api/exchange/mailboxes/${encodeURIComponent(mailbox.userId)}/overview`);
      if (!response.ok) {
        const value = await response.json().catch(() => null) as MailboxVerification | null;
        if (generation === detailsGeneration.current) { setDetails(current => ({ ...current, [mailbox.userId]: { status: 'error', message: value?.error?.category === 'not_found' ? 'No Exchange mailbox was found for this directory entry.' : value?.error?.category === 'not_authorized' ? 'Mailbox verification is unavailable for this entry.' : 'Mailbox details are unavailable for this entry.' } })); issueReporter.report({ key: `exchange:mailbox:${mailbox.userId}`, area: 'services', kind: 'service', severity: 'warning', title: 'Mailbox verification unavailable', detail: 'Try verifying this mailbox again.' }); }
        return;
      }
      const value = await response.json() as MailboxVerification;
      if (generation === detailsGeneration.current) { setDetails(current => ({ ...current, [mailbox.userId]: { status: 'loaded', value, retrievedAt: new Date().toISOString() } })); if (value.verificationStatus === 'verified') issueReporter.clear(`exchange:mailbox:${mailbox.userId}`); else issueReporter.report({ key: `exchange:mailbox:${mailbox.userId}`, area: 'services', kind: 'service', severity: 'warning', title: 'Mailbox verification unavailable', detail: 'Try verifying this mailbox again.' }); }
    } catch { if (generation === detailsGeneration.current) { setDetails(current => ({ ...current, [mailbox.userId]: { status: 'error', message: 'Mailbox details are temporarily unavailable.' } })); issueReporter.report({ key: `exchange:mailbox:${mailbox.userId}`, area: 'services', kind: 'service', severity: 'warning', title: 'Mailbox verification unavailable', detail: 'Try verifying this mailbox again.' }); } }
  };
  const verificationView = (mailbox: Mailbox) => {
    const verification = details[mailbox.userId];
    const isLoading = verification?.status === 'loading';
    const isVerified = verification?.status === 'loaded' && verification.value.verificationStatus === 'verified';
    return <><span className="status-badge" data-tone={isVerified ? 'success' : 'warning'}>{isVerified ? 'Verified' : 'Unverified'}</span><div className="responsive-data-view__actions"><button type="button" className="table-action" aria-label={`Verify Exchange details for ${mailbox.displayName}`} disabled={isLoading} onClick={() => void openDetails(mailbox)}>{isLoading ? 'Loading…' : isVerified ? 'Refresh details' : 'Verify details'}</button></div>
      {verification && verification.status !== 'loading' && <div className="exchange-mailbox-details" role="status"><p className="data-freshness">Source: Microsoft Graph · Exchange mailbox lookup. {verification.status === 'loaded' ? `Retrieved: ${new Date(verification.retrievedAt).toLocaleString()}` : 'Not retrieved.'}</p>
        {isVerified && verification.status === 'loaded' ? <><p>Mailbox type: <strong>{verification.value.mailboxType || 'Unknown'}</strong></p><dl>{Object.entries({ 'Time zone': verification.value.settings?.timeZone, Language: verification.value.settings?.language, 'Date format': verification.value.settings?.dateFormat, 'Time format': verification.value.settings?.timeFormat, 'Automatic replies': verification.value.settings?.automaticRepliesStatus }).filter(([, value]) => value).map(([label, value]) => <div key={label}><dt>{label}</dt><dd>{value}</dd></div>)}</dl></> : <p>{verification.status === 'error' ? verification.message : 'Mailbox verification is unavailable.'}</p>}
        <a href="https://admin.exchange.microsoft.com/#/mailboxes" target="_blank" rel="noreferrer">Open Exchange admin center</a></div>}</>;
  };
  return <section className="exchange-page" aria-label="Exchange">
    <WorkspacePageHeader eyebrow="Services" title="Exchange" description="Review mailboxes from the connected directory. Entries are unverified until their Exchange lookup completes." />
    <form className="exchange-toolbar" role="search" onSubmit={event => { event.preventDefault(); submitSearch(); }}><label>Search mailboxes<input value={draftSearch} onChange={event => setDraftSearch(event.target.value)} placeholder="Name or address" /></label><button type="submit" disabled={loading}>Search</button></form>
    {loading && <WorkspaceDataState state="loading" message="Loading Exchange mailboxes…" />}
    {!loading && failed && <section className="permission-panel"><h2>Exchange is unavailable</h2><WorkspaceDataState state="unavailable" message="Mailbox directory data could not be loaded. Check Notifications for details." onRetry={() => setRetry(value => value + 1)} /></section>}
    {!loading && !failed && result && result.items.length === 0 && <WorkspaceDataState state="empty" message="No mailboxes match the current search." />}
    {!loading && !failed && result && <><p className="data-freshness">Source: Microsoft Graph · directory-backed mail-enabled identities. Retrieved: {retrievedAt ? new Date(retrievedAt).toLocaleString() : 'Not retrieved'}</p><p className="integration-note">{result.limitation ?? 'Directory-backed mail-enabled identities; this is not a complete Exchange inventory.'}</p></>}
    {!loading && !failed && result && result.items.length > 0 && <ResponsiveDataView items={result.items} keyOf={mailbox => mailbox.userId} label="Exchange mailboxes" renderCompact={mailbox => <><strong>{mailbox.displayName}</strong><span>{mailbox.address || 'Address unavailable'}</span>{verificationView(mailbox)}</>} renderTable={items => <table className="users-table" aria-label="Exchange mailbox directory"><thead><tr><th scope="col">Mailbox</th><th scope="col">Address</th><th scope="col">Verification and details</th></tr></thead><tbody>{items.map(mailbox => <tr key={mailbox.userId}><th scope="row">{mailbox.displayName}</th><td>{mailbox.address || 'Unavailable'}</td><td>{verificationView(mailbox)}</td></tr>)}</tbody></table>} />}
    {!loading && !failed && result && (query.page > 1 || result.continuationToken) && <nav className="pagination-controls" aria-label="Exchange mailbox pages"><button type="button" disabled={query.page === 1} onClick={() => { const previous = previousTokens[previousTokens.length - 1] ?? null; setPreviousTokens(tokens => tokens.slice(0, -1)); setQuery(current => ({ ...current, token: previous, page: current.page - 1 })); }}>Previous page</button><span aria-live="polite">Page {query.page}</span><button type="button" disabled={!result.continuationToken} onClick={() => { setPreviousTokens(tokens => [...tokens, query.token]); setQuery(current => ({ ...current, token: result.continuationToken ?? null, page: current.page + 1 })); }}>Next page</button></nav>}
  </section>;
}
