import React, { useEffect, useMemo, useRef, useState } from 'react';
import { DataFreshness } from '../../components/DataFreshness';
import { DateTime } from '../../components/DateTime';
import { KpiFilterTile } from '../../components/KpiFilterTile';
import { ResponsiveDataView } from '../../components/ResponsiveDataView';
import { WorkspaceDataState } from '../../components/WorkspaceDataState';
import { WorkspacePageHeader } from '../../components/WorkspacePageHeader';
import type { CapabilityDecision } from '../../capabilities/capabilityTypes';
import type { AppSession } from '../../components/TenantContextHeader';
import { authenticationCampaignMessages as copy } from './messages';
import type { AuthenticationCampaignsRegistration, AuthenticationCampaignsResponse } from './authenticationCampaignsApi';
import './authentication-campaigns.css';

export type AuthenticationCampaignsLoader = () => Promise<AuthenticationCampaignsResponse>;

type Props = {
  loadRegistrations: AuthenticationCampaignsLoader;
  capabilities?: CapabilityDecision[];
  session?: AppSession;
  onNavigate?: (path: string) => void;
  authorizationUnavailable?: boolean;
  onAuthorizationRetry?: () => Promise<void>;
};

type View = 'passkeys' | 'phone';
type Population = 'member' | 'guest' | 'unknown';
type PasskeyFilter = 'all' | 'passkey' | 'fido2' | 'mfa';
type PhoneFilter = 'preference' | 'registered';
type RollupField = 'department' | 'officeLocation' | 'companyName';
const pageSize = 10;
const freshnessWindowMs = 36 * 60 * 60 * 1000;

export function AuthenticationCampaignsPage(props: Props) {
  const decision = props.capabilities?.find(item => item.capability === 'authentication.campaigns.view');
  const capabilityAllowsRead = decision?.state === 'allowed' || decision?.state === 'read_only';
  const canLoad = !props.authorizationUnavailable && capabilityAllowsRead;

  return <section className="authentication-campaigns">
    <WorkspacePageHeader
      eyebrow={copy.authenticationCampaignsEyebrow}
      title={copy.authenticationCampaignsTitle}
      description={copy.authenticationCampaignsDescription}
    />
    {canLoad
      ? <CampaignsViews {...props} />
      : <AccessDiagnostics decision={decision} onRetry={props.onAuthorizationRetry} />}
  </section>;
}

function AccessDiagnostics({ decision, onRetry }: { decision?: CapabilityDecision; onRetry?: () => Promise<void> }) {
  return <section className="authentication-campaigns__access" aria-labelledby="authentication-campaigns-access-title">
    <h2 id="authentication-campaigns-access-title">{copy.authenticationCampaignsAccessTitle}</h2>
    <p>{copy.authenticationCampaignsAccessBody}</p>
    <ul>
      <li>{copy.authenticationCampaignsAccessRole}</li>
      <li>{copy.authenticationCampaignsAccessPermission}</li>
      <li>{copy.authenticationCampaignsAccessDirectoryPermission}</li>
      <li>{copy.authenticationCampaignsAccessLicense}</li>
    </ul>
    <p><strong>{copy.authenticationCampaignsAccessDecision}:</strong> {decision?.state ?? 'Unavailable'}</p>
    {onRetry && <button className="button button--secondary" type="button" onClick={() => void onRetry()}>{copy.authenticationCampaignsRetry}</button>}
  </section>;
}

function CampaignsViews(props: Props) {
  const [view, setView] = useState<View>('passkeys');
  const tabRefs = useRef<Array<HTMLButtonElement | null>>([]);
  const activateTab = (next: View) => setView(next);
  const onTabKeyDown = (event: React.KeyboardEvent<HTMLButtonElement>) => {
    const current = view === 'passkeys' ? 0 : 1;
    const next = event.key === 'ArrowRight' || event.key === 'Home'
      ? event.key === 'Home' ? 0 : (current + 1) % 2
      : event.key === 'ArrowLeft' || event.key === 'End'
        ? event.key === 'End' ? 1 : (current + 1) % 2
        : -1;
    if (next < 0) return;
    event.preventDefault();
    const value: View = next === 0 ? 'passkeys' : 'phone';
    setView(value);
    tabRefs.current[next]?.focus();
  };

  return <>
    <p className="authentication-campaigns__notice" role="note">{copy.authenticationCampaignsRegistrationNotice}</p>
    <p className="authentication-campaigns__license-note">{copy.authenticationCampaignsLicenseNote}</p>
    <div className="authentication-campaigns__tabs" role="tablist" aria-label="Authentication campaign views">
      {(['passkeys', 'phone'] as const).map((value, index) => <button
        key={value}
        ref={element => { tabRefs.current[index] = element; }}
        id={`authentication-campaigns-tab-${value}`}
        className="authentication-campaigns__tab"
        type="button"
        role="tab"
        aria-selected={view === value}
        aria-controls="authentication-campaigns-panel"
        tabIndex={view === value ? 0 : -1}
        onClick={() => activateTab(value)}
        onKeyDown={onTabKeyDown}
      >{value === 'passkeys' ? copy.authenticationCampaignsPasskeysTab : copy.authenticationCampaignsPhoneTab}</button>)}
    </div>
    <CampaignsDataPanel {...props} view={view} onNavigateToPhone={() => activateTab('phone')} />
  </>;
}

function CampaignsDataPanel({ loadRegistrations, capabilities, session, onNavigate, view, onNavigateToPhone }: Props & { view: View; onNavigateToPhone: () => void }) {
  const [response, setResponse] = useState<AuthenticationCampaignsResponse | null>(null);
  const [failure, setFailure] = useState<unknown>(null);
  const [loading, setLoading] = useState(true);
  const [retry, setRetry] = useState(0);
  const [population, setPopulation] = useState<Population>('member');
  const [passkeyFilter, setPasskeyFilter] = useState<PasskeyFilter>('all');
  const [phoneFilter, setPhoneFilter] = useState<PhoneFilter>('preference');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setFailure(null);
    loadRegistrations().then(value => {
      if (!cancelled) setResponse(value);
    }).catch(reason => {
      if (!cancelled) setFailure(reason);
    }).finally(() => {
      if (!cancelled) setLoading(false);
    });
    return () => { cancelled = true; };
  }, [loadRegistrations, retry]);

  const populationAccounts = useMemo(() => (response?.items ?? []).filter(item => matchesPopulation(item, population)), [response, population]);
  const matchingAccounts = useMemo(() => populationAccounts.filter(item => matchesSelectedMetric(item, view, view === 'passkeys' ? passkeyFilter : phoneFilter)), [populationAccounts, view, passkeyFilter, phoneFilter]);
  const normalizedSearch = search.trim().toLocaleLowerCase();
  const searchedAccounts = useMemo(() => matchingAccounts.filter(item => !normalizedSearch || [
    item.displayName,
    item.userPrincipalName,
    ...(item.methodsRegistered ?? []),
  ].some(value => value?.toLocaleLowerCase().includes(normalizedSearch))), [matchingAccounts, normalizedSearch]);
  const pageCount = Math.ceil(searchedAccounts.length / pageSize);
  const pageItems = searchedAccounts.slice((page - 1) * pageSize, page * pageSize);
  const accessToUsers = canOpenUsers(session, capabilities);

  useEffect(() => { setPage(1); }, [population, passkeyFilter, phoneFilter, search, view]);

  if (loading) {
    return <section id="authentication-campaigns-panel" className="authentication-campaigns__panel" role="tabpanel" aria-labelledby={`authentication-campaigns-tab-${view}`} aria-busy="true">
      <WorkspaceDataState state="loading" message="Loading authentication registration report…" />
    </section>;
  }
  if (failure) {
    const status = (failure as { status?: number }).status;
    const category = (failure as { category?: string }).category;
    return <section id="authentication-campaigns-panel" className="authentication-campaigns__panel" role="tabpanel" aria-labelledby={`authentication-campaigns-tab-${view}`}>
      <WorkspaceDataState
        kind={status === 403 ? 'permission' : 'unavailable'}
        title={status === 403 ? 'Report access denied' : 'Registration report unavailable'}
        message={category === 'consent_required' ? 'The API needs delegated Microsoft Graph consent for this report.' : copy.authenticationCampaignsUnavailable}
        onRetry={() => setRetry(value => value + 1)}
        retryLabel="Retry report"
      />
    </section>;
  }
  if (!response) return null;

  const latestSourceTimestamp = response.sourceLastUpdatedTo ?? response.sourceLastUpdatedFrom;
  const sourceFreshnessKnown = response.sourceLastUpdatedTo !== null && Number.isFinite(Date.parse(response.sourceLastUpdatedTo));
  const stale = sourceFreshnessKnown && isStale(latestSourceTimestamp);
  const olderRecords = isStale(response.sourceLastUpdatedFrom);
  const panelTitle = view === 'passkeys' ? copy.authenticationCampaignsPasskeysTab : copy.authenticationCampaignsPhoneTab;
  return <section id="authentication-campaigns-panel" className="authentication-campaigns__panel" role="tabpanel" aria-labelledby={`authentication-campaigns-tab-${view}`}>
    {view === 'passkeys'
      ? <PasskeyKpis accounts={populationAccounts} filter={passkeyFilter} setFilter={setPasskeyFilter} population={population} />
      : <PhoneKpis accounts={populationAccounts} filter={phoneFilter} setFilter={setPhoneFilter} population={population} />}
    {view === 'passkeys'
      ? <div className="authentication-campaigns__view-intro"><p>{copy.authenticationCampaignsPasskeysIntro} <strong>{copy.authenticationCampaignsEligibility}</strong></p><button className="button button--secondary" type="button" onClick={onNavigateToPhone}>{copy.authenticationCampaignsPhoneCandidateAction}</button></div>
      : <section className="authentication-campaigns__deadline"><h2>{copy.authenticationCampaignsDeadline}</h2><p>{copy.authenticationCampaignsPhoneIntro}</p><a className="button button--secondary" href="#authentication-campaigns-account-results" onClick={() => document.getElementById('authentication-campaigns-account-results')?.focus()}>{copy.authenticationCampaignsPhoneCandidateAction}</a></section>}
    <div className="authentication-campaigns__freshness">
      {sourceFreshnessKnown
        ? <DataFreshness fetchedAt={response.fetchedAt} freshness={stale ? 'stale' : 'fresh'} partialData={response.partialData} labels={{ fresh: copy.authenticationCampaignsLatestUpdateRecent, stale: copy.authenticationCampaignsSourceStale }} source={response.partialData ? (stale ? copy.authenticationCampaignsSourceStale : copy.authenticationCampaignsLatestUpdateRecent) : undefined} />
        : <p role="status">{copy.authenticationCampaignsSourceUnknown} {copy.authenticationCampaignsFetched} <DateTime value={response.fetchedAt} />.</p>}
      {(response.sourceLastUpdatedFrom || response.sourceLastUpdatedTo) &&
        <p>{copy.authenticationCampaignsSourceRange}: {response.sourceLastUpdatedFrom ? <DateTime value={response.sourceLastUpdatedFrom} /> : copy.authenticationCampaignsUnknown} – {response.sourceLastUpdatedTo ? <DateTime value={response.sourceLastUpdatedTo} /> : copy.authenticationCampaignsUnknown}. Fetch time is shown separately.</p>}
      {olderRecords && <p role="status">{copy.authenticationCampaignsOlderRecords}</p>}
    </div>
    {response.partialData && <p className="authentication-campaigns__partial" role="status">{copy.authenticationCampaignsPartial}{response.reportErrorCategory ? ` Report: ${response.reportErrorCategory}.` : ''}{response.directoryErrorCategory ? ` Directory: ${response.directoryErrorCategory}.` : ''}</p>}
    <div className="authentication-campaigns__coverage" aria-label="Report and directory coverage">
      <p><strong>{copy.authenticationCampaignsReportCoverage}:</strong> {response.items.length} observed accounts from {response.observedRecordCount} report records. {copy.authenticationCampaignsNotTenantTotal}</p>
      <p><strong>{copy.authenticationCampaignsDirectoryCoverage}:</strong> {response.enrichedAccountCount} enriched accounts of {response.items.length} observed accounts ({response.directoryEnrichmentState}).</p>
      {response.duplicateRecordCount > 0 && <p>{response.duplicateRecordCount} duplicate report records were excluded from account counts.</p>}
    </div>
    <div className="authentication-campaigns__controls">
      <label>
        {copy.authenticationCampaignsPopulation}
        <select aria-label={copy.authenticationCampaignsPopulation} value={population} onChange={event => setPopulation(event.target.value as Population)}>
          <option value="member">{copy.authenticationCampaignsMembers}</option>
          <option value="guest">{copy.authenticationCampaignsGuests}</option>
          <option value="unknown">{copy.authenticationCampaignsUnknownUsers}</option>
        </select>
      </label>
      <label>
        {copy.authenticationCampaignsSearch}
        <input aria-label={copy.authenticationCampaignsSearch} placeholder={copy.authenticationCampaignsSearchPlaceholder} value={search} onChange={event => setSearch(event.target.value)} />
      </label>
    </div>
    <h2 id="authentication-campaigns-account-results" tabIndex={-1} className="authentication-campaigns__section-title">{panelTitle} account results</h2>
    <p aria-live="polite">{copy.authenticationCampaignsResultCount(searchedAccounts.length)}</p>
    {response.items.length === 0 && !search.trim()
      ? <WorkspaceDataState state="empty" message={copy.authenticationCampaignsNoAccounts} />
      : searchedAccounts.length === 0
        ? <WorkspaceDataState state="empty" message={copy.authenticationCampaignsNoMatches} />
        : <ResponsiveDataView
          items={pageItems}
          keyOf={item => item.id}
          label={copy.authenticationCampaignsAccountResults}
          renderCompact={item => <CompactAccount item={item} accessToUsers={accessToUsers} onNavigate={onNavigate} />}
          renderTable={items => <AccountTable items={items} accessToUsers={accessToUsers} onNavigate={onNavigate} />}
        />}
    <nav className="authentication-campaigns__pagination" aria-label="Account result pages">
      <span>{copy.authenticationCampaignsPageLabel(page, pageCount)}</span>
      <button type="button" className="button button--secondary button--sm" onClick={() => setPage(value => value - 1)} disabled={page <= 1}>{copy.authenticationCampaignsPreviousPage}</button>
      <button type="button" className="button button--secondary button--sm" onClick={() => setPage(value => value + 1)} disabled={page >= pageCount}>{copy.authenticationCampaignsNextPage}</button>
    </nav>
    <Rollups accounts={matchingAccounts} />
  </section>;
}

function PasskeyKpis({ accounts, filter, setFilter, population }: { accounts: AuthenticationCampaignsRegistration[]; filter: PasskeyFilter; setFilter: (value: PasskeyFilter) => void; population: Population }) {
  const groupLabel = populationLabel(population);
  const knownPasskey = accounts.filter(item => ['registered', 'not_reported'].includes(item.passkeyRegistrationState)).length;
  const knownFido = accounts.filter(item => item.isGenericFido2Registered !== null).length;
  const knownMfa = accounts.filter(item => item.isMfaRegistered !== null).length;
  return <div className="authentication-campaigns__kpis" aria-label="Passkey registration metrics">
    <KpiFilterTile label={copy.authenticationCampaignsAllPopulation} value={accounts.length} detail={`${accounts.length} ${groupLabel} in observed report`} selected={filter === 'all'} onClick={() => setFilter('all')} />
    <KpiFilterTile label={copy.authenticationCampaignsPasskeyKpi} value={metricValue(knownPasskey, accounts.filter(item => item.passkeyRegistrationState === 'registered').length)} detail={metricDetail(knownPasskey, accounts.length - knownPasskey, groupLabel, 'passkey registration')} selected={filter === 'passkey'} onClick={() => setFilter('passkey')} />
    <KpiFilterTile label={copy.authenticationCampaignsFidoKpi} value={metricValue(knownFido, accounts.filter(item => item.isGenericFido2Registered === true).length)} detail={metricDetail(knownFido, accounts.length - knownFido, groupLabel, 'generic FIDO2 registration')} selected={filter === 'fido2'} onClick={() => setFilter('fido2')} />
    <KpiFilterTile label={copy.authenticationCampaignsMfaKpi} value={metricValue(knownMfa, accounts.filter(item => item.isMfaRegistered === true).length)} detail={metricDetail(knownMfa, accounts.length - knownMfa, groupLabel, 'MFA registration')} selected={filter === 'mfa'} onClick={() => setFilter('mfa')} />
  </div>;
}

function PhoneKpis({ accounts, filter, setFilter, population }: { accounts: AuthenticationCampaignsRegistration[]; filter: PhoneFilter; setFilter: (value: PhoneFilter) => void; population: Population }) {
  const groupLabel = populationLabel(population);
  const knownPreferences = accounts.filter(item => ['phone', 'not_phone'].includes(item.phonePreferenceState)).length;
  const knownPhoneRegistrations = accounts.filter(item => ['registered', 'not_registered'].includes(item.phoneRegistrationState)).length;
  const preferred = accounts.filter(item => item.phonePreferenceState === 'phone');
  const registered = accounts.filter(item => item.phoneRegistrationState === 'registered');
  return <div className="authentication-campaigns__kpis" aria-label="SMS and phone metrics">
    <KpiFilterTile label={copy.authenticationCampaignsPhoneCandidates} value={metricValue(knownPreferences, preferred.length)} detail={metricDetail(knownPreferences, accounts.length - knownPreferences, groupLabel, 'phone preference')} selected={filter === 'preference'} onClick={() => setFilter('preference')} />
    <KpiFilterTile label={copy.authenticationCampaignsPhoneRegistered} value={metricValue(knownPhoneRegistrations, registered.length)} detail={metricDetail(knownPhoneRegistrations, accounts.length - knownPhoneRegistrations, groupLabel, 'phone registration')} selected={filter === 'registered'} onClick={() => setFilter('registered')} />
  </div>;
}

function AccountTable({ items, accessToUsers, onNavigate }: { items: AuthenticationCampaignsRegistration[]; accessToUsers: boolean; onNavigate?: (path: string) => void }) {
  return <table className="authentication-campaigns__table" aria-label={copy.authenticationCampaignsAccountResults}>
    <caption className="sr-only">{copy.authenticationCampaignsAccountResults}</caption>
    <thead><tr><th scope="col">Account</th><th scope="col">{copy.authenticationCampaignsUserType}</th><th scope="col">{copy.authenticationCampaignsPasskeyState}</th><th scope="col">{copy.authenticationCampaignsFidoState}</th><th scope="col">{copy.authenticationCampaignsMfaRegistered}</th><th scope="col">{copy.authenticationCampaignsMfaCapable}</th><th scope="col">{copy.authenticationCampaignsPasswordlessCapable}</th><th scope="col">{copy.authenticationCampaignsPhoneState}</th><th scope="col">{copy.authenticationCampaignsPhonePreference}</th><th scope="col">{copy.authenticationCampaignsMethods}</th></tr></thead>
    <tbody>{items.map(item => <tr key={item.id}>
      <th scope="row"><AccountName item={item} accessToUsers={accessToUsers} onNavigate={onNavigate} /></th>
      <td>{displayUserType(item.userType)}</td>
      <td>{stateLabel(item.passkeyRegistrationState, true)}</td>
      <td>{item.isGenericFido2Registered === null ? copy.authenticationCampaignsUnknown : item.isGenericFido2Registered ? copy.authenticationCampaignsRegistered : copy.authenticationCampaignsNotRegistered}</td>
      <td>{booleanLabel(item.isMfaRegistered)}</td>
      <td>{booleanLabel(item.isMfaCapable)}</td>
      <td>{booleanLabel(item.isPasswordlessCapable)}</td>
      <td>{stateLabel(item.phoneRegistrationState, false)}</td>
      <td>{preferenceLabel(item)}</td>
      <td>{(item.methodsRegistered ?? []).length > 0 ? item.methodsRegistered!.map((method, index) => <React.Fragment key={`${method}-${index}`}>{index > 0 && ', '}{method}</React.Fragment>) : copy.authenticationCampaignsUnknown}</td>
    </tr>)}</tbody>
  </table>;
}

function CompactAccount({ item, accessToUsers, onNavigate }: { item: AuthenticationCampaignsRegistration; accessToUsers: boolean; onNavigate?: (path: string) => void }) {
  return <article className="authentication-campaigns__compact">
    <h3><AccountName item={item} accessToUsers={accessToUsers} onNavigate={onNavigate} /></h3>
    {item.userPrincipalName && <p>{item.userPrincipalName}</p>}
    <dl><div><dt>{copy.authenticationCampaignsUserType}</dt><dd>{displayUserType(item.userType)}</dd></div><div><dt>{copy.authenticationCampaignsPasskeyState}</dt><dd>{stateLabel(item.passkeyRegistrationState, true)}</dd></div><div><dt>{copy.authenticationCampaignsFidoState}</dt><dd>{item.isGenericFido2Registered === null ? copy.authenticationCampaignsUnknown : item.isGenericFido2Registered ? copy.authenticationCampaignsRegistered : copy.authenticationCampaignsNotRegistered}</dd></div><div><dt>{copy.authenticationCampaignsMfaRegistered}</dt><dd>{booleanLabel(item.isMfaRegistered)}</dd></div><div><dt>{copy.authenticationCampaignsMfaCapable}</dt><dd>{booleanLabel(item.isMfaCapable)}</dd></div><div><dt>{copy.authenticationCampaignsPasswordlessCapable}</dt><dd>{booleanLabel(item.isPasswordlessCapable)}</dd></div><div><dt>{copy.authenticationCampaignsPhoneState}</dt><dd>{stateLabel(item.phoneRegistrationState, false)}</dd></div><div><dt>{copy.authenticationCampaignsPhonePreference}</dt><dd>{preferenceLabel(item)}</dd></div><div><dt>{copy.authenticationCampaignsMethods}</dt><dd>{(item.methodsRegistered ?? []).join(', ') || copy.authenticationCampaignsUnknown}</dd></div></dl>
  </article>;
}

function AccountName({ item, accessToUsers, onNavigate }: { item: AuthenticationCampaignsRegistration; accessToUsers: boolean; onNavigate?: (path: string) => void }) {
  const label = item.displayName || item.userPrincipalName || item.id;
  if (!accessToUsers) return <span>{label}</span>;
  return <a href={`/users/${encodeURIComponent(item.id)}`} onClick={event => { event.preventDefault(); onNavigate?.(`/users/${encodeURIComponent(item.id)}`); }}>{label}</a>;
}

function Rollups({ accounts }: { accounts: AuthenticationCampaignsRegistration[] }) {
  const groups: Array<{ field: RollupField; label: string }> = [
    { field: 'department', label: copy.authenticationCampaignsDepartment },
    { field: 'officeLocation', label: copy.authenticationCampaignsOffice },
    { field: 'companyName', label: copy.authenticationCampaignsCompany },
  ];
  return <section className="authentication-campaigns__rollups">
    <h2>{copy.authenticationCampaignsRollups}</h2>
    <p>{copy.authenticationCampaignsDirectoryCoverage} within this population and metric: {accounts.filter(item => item.directoryJoinState === 'matched').length} enriched accounts of {accounts.length} matching observed accounts.</p>
    {groups.map(({ field, label }) => {
      const counts = new Map<string, number>();
      for (const item of accounts) {
        const raw = item[field];
        const bucket = item.directoryJoinState === 'unavailable' || item.directoryJoinState === 'partial'
          ? 'Unavailable'
          : raw?.trim() || 'Unknown';
        counts.set(bucket, (counts.get(bucket) ?? 0) + 1);
      }
      return <details key={field} className="authentication-campaigns__rollup">
        <summary>{label}</summary>
        <table aria-label={`${label} rollup`}><thead><tr><th scope="col">{label}</th><th scope="col">Observed accounts</th></tr></thead><tbody>{[...counts].sort(([left], [right]) => left.localeCompare(right)).map(([name, count]) => <tr key={name}><th scope="row">{name}</th><td>{count}</td></tr>)}</tbody></table>
      </details>;
    })}
  </section>;
}

function matchesPopulation(item: AuthenticationCampaignsRegistration, population: Population) {
  const type = item.userType?.toLocaleLowerCase();
  if (population === 'member') return type === 'member';
  if (population === 'guest') return type === 'guest';
  return type !== 'member' && type !== 'guest';
}

function matchesSelectedMetric(item: AuthenticationCampaignsRegistration, view: View, filter: PasskeyFilter | PhoneFilter) {
  if (view === 'passkeys') {
    if (filter === 'all') return true;
    if (filter === 'passkey') return item.passkeyRegistrationState === 'registered';
    if (filter === 'fido2') return item.isGenericFido2Registered === true;
    return item.isMfaRegistered === true;
  }
  return filter === 'preference' ? item.phonePreferenceState === 'phone' : item.phoneRegistrationState === 'registered';
}

function metricValue(denominator: number, numerator: number) {
  return denominator > 0 ? numerator : 'Not applicable';
}

function metricDetail(denominator: number, excluded: number, groupLabel: string, metric: string) {
  return copy.authenticationCampaignsKnownDenominator(denominator, groupLabel, metric, excluded);
}

function populationLabel(population: Population) {
  return population === 'member' ? 'member accounts' : population === 'guest' ? 'guest accounts' : 'unknown or unspecified user types';
}

function stateLabel(state: string, passkey: boolean) {
  if (state === 'registered') return copy.authenticationCampaignsRegistered;
  if (state === 'not_registered' || (passkey && state === 'not_reported')) return passkey ? copy.authenticationCampaignsNotReported : copy.authenticationCampaignsNotRegistered;
  return copy.authenticationCampaignsUnknown;
}

function preferenceLabel(item: AuthenticationCampaignsRegistration) {
  if (item.isSystemPreferredAuthenticationMethodEnabled === true) {
    const value = item.systemPreferredAuthenticationMethods?.join(', ') || copy.authenticationCampaignsUnknown;
    return `${copy.authenticationCampaignsSystemPreferred}: ${value} (${preferenceStateLabel(item.phonePreferenceState)})`;
  }
  if (item.isSystemPreferredAuthenticationMethodEnabled === false) {
    const value = item.userPreferredMethodForSecondaryAuthentication || copy.authenticationCampaignsUnknown;
    return `${copy.authenticationCampaignsUserPreferred}: ${value} (${preferenceStateLabel(item.phonePreferenceState)})`;
  }
  return copy.authenticationCampaignsPreferenceSourceUnknown;
}

function preferenceStateLabel(state: string) {
  if (state === 'phone') return copy.authenticationCampaignsPhonePreferenceCandidate;
  if (state === 'not_phone') return copy.authenticationCampaignsNotPhone;
  return copy.authenticationCampaignsUnknown;
}

function booleanLabel(value: boolean | null) {
  return value === null ? copy.authenticationCampaignsUnknown : value ? 'Yes' : 'No';
}

function displayUserType(userType: string | null) {
  if (!userType) return copy.authenticationCampaignsUnknown;
  if (userType.toLocaleLowerCase() === 'member') return copy.authenticationCampaignsMembers;
  if (userType.toLocaleLowerCase() === 'guest') return copy.authenticationCampaignsGuests;
  return userType;
}

function canOpenUsers(session?: AppSession, capabilities?: CapabilityDecision[]) {
  if (!session?.workspace.moduleAccess?.includes('users') || !session.workspace.enabledModules?.includes('users')) return false;
  const decision = capabilities?.find(item => item.capability === 'users.view');
  return decision?.state === 'allowed' || decision?.state === 'read_only';
}

function isStale(timestamp: string | null) {
  if (!timestamp) return false;
  return Date.now() - Date.parse(timestamp) > freshnessWindowMs;
}
