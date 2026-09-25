import { useCallback, useEffect, useState } from 'react';
import { useApi } from '../../auth/useApi';

type Mailbox = { userId: string; displayName: string; address?: string | null };
type MailboxResponse = { items: Mailbox[]; continuationToken?: string | null; isCompleteExchangeInventory?: boolean; limitation?: string };
type MailboxVerification = {
  userId: string;
  verificationStatus: string;
  mailboxType?: string | null;
  settings?: { timeZone?: string | null; language?: string | null; dateFormat?: string | null; timeFormat?: string | null; automaticRepliesStatus?: string | null } | null;
  error?: { category: string; message: string } | null;
};

export function ExchangeOverviewPage() {
  const api = useApi();
  const [search, setSearch] = useState('');
  const [result, setResult] = useState<MailboxResponse | null>(null);
  const [continuationToken, setContinuationToken] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  const [details, setDetails] = useState<Record<string, 'loading' | MailboxVerification | { verificationStatus: 'unavailable'; error: { message: string } }>>({});
  const load = useCallback(async () => {
    setLoading(true); setFailed(false);
    try {
      const query = new URLSearchParams({ search, pageSize: '25' });
      if (continuationToken) query.set('continuationToken', continuationToken);
      const response = await api(`/api/exchange/mailboxes?${query}`);
      if (!response.ok) throw new Error('exchange unavailable');
      setResult(await response.json() as MailboxResponse);
    } catch { setFailed(true); } finally { setLoading(false); }
  }, [api, continuationToken, search]);
  useEffect(() => { void load(); }, [load]);
  const openDetails = async (mailbox: Mailbox) => {
    setDetails(current => ({ ...current, [mailbox.userId]: 'loading' }));
    try {
      const response = await api(`/api/exchange/mailboxes/${encodeURIComponent(mailbox.userId)}/overview`);
      if (!response.ok) {
        const value = await response.json().catch(() => null) as MailboxVerification | null;
        setDetails(current => ({ ...current, [mailbox.userId]: { verificationStatus: 'unavailable', error: { message: value?.error?.message ?? 'Mailbox details are unavailable for this entry.' } } }));
        return;
      }
      const value = await response.json() as MailboxVerification;
      setDetails(current => ({ ...current, [mailbox.userId]: value }));
    } catch { setDetails(current => ({ ...current, [mailbox.userId]: { verificationStatus: 'unavailable', error: { message: 'Mailbox details are temporarily unavailable.' } } })); }
  };
  return <section className="exchange-page" aria-labelledby="exchange-title">
    <header className="page-header"><div><p className="eyebrow">Services</p><h1 id="exchange-title">Exchange</h1><p>Review mailboxes from the connected directory. Entries are unverified until their Exchange lookup completes.</p></div></header>
    <form className="exchange-toolbar" onSubmit={event => { event.preventDefault(); void load(); }}><label>Search mailboxes<input value={search} onChange={event => { setSearch(event.target.value); setContinuationToken(null); }} placeholder="Name or address" /></label><button type="submit">Search</button></form>
    {loading && <p role="status">Loading Exchange mailboxes…</p>}
    {failed && <section className="permission-panel" role="alert"><h2>Exchange is unavailable</h2><p>Mailbox directory data could not be loaded. Try again later.</p><button type="button" onClick={() => void load()}>Retry</button></section>}
    {!loading && !failed && result && result.items.length === 0 && <p className="async-state">No mailboxes match the current search.</p>}
    {!loading && !failed && result && <p className="integration-note">{result.limitation ?? 'Directory-backed mail-enabled identities; this is not a complete Exchange inventory.'}</p>}
    {!loading && !failed && result && result.items.length > 0 && <><div className="users-table-wrap"><table className="users-table" aria-label="Exchange mailbox directory"><thead><tr><th scope="col">Mailbox</th><th scope="col">Address</th><th scope="col">Verification</th><th scope="col">Details</th></tr></thead><tbody>{result.items.map(mailbox => {
      const verification = details[mailbox.userId];
      const isLoading = verification === 'loading';
      const isVerified = verification !== undefined && !isLoading && verification.verificationStatus === 'verified';
      return <tr key={mailbox.userId}><th scope="row" data-label="Mailbox">{mailbox.displayName}</th><td data-label="Address">{mailbox.address || 'Unavailable'}</td><td data-label="Verification">{isVerified ? <span className="status-badge" data-tone="success">Verified</span> : <span className="status-badge" data-tone="warning">Unverified</span>}</td><td data-label="Details"><button type="button" className="table-action" aria-label={`Verify Exchange details for ${mailbox.displayName}`} disabled={isLoading} onClick={() => void openDetails(mailbox)}>{isLoading ? 'Loading…' : isVerified ? 'Refresh details' : 'Verify details'}</button>
        {verification && !isLoading && <div className="exchange-mailbox-details" role="status">
          {verification.verificationStatus === 'verified' ? <><p>Mailbox type: <strong>{verification.mailboxType || 'Unknown'}</strong></p><dl>{Object.entries({ 'Time zone': verification.settings?.timeZone, Language: verification.settings?.language, 'Date format': verification.settings?.dateFormat, 'Time format': verification.settings?.timeFormat, 'Automatic replies': verification.settings?.automaticRepliesStatus }).filter(([, value]) => value).map(([label, value]) => <div key={label}><dt>{label}</dt><dd>{value}</dd></div>)}</dl></> : <p>{verification.error?.message ?? 'Mailbox verification is unavailable.'}</p>}
          <a href="https://admin.exchange.microsoft.com/#/mailboxes" target="_blank" rel="noreferrer">Open Exchange admin center</a>
        </div>}
      </td></tr>;
    })}</tbody></table></div>{result.continuationToken && <button type="button" onClick={() => setContinuationToken(result.continuationToken ?? null)}>Next page</button>}</>}
  </section>;
}
