import { useCallback, useEffect, useMemo, useState } from 'react';
import { useApi } from '../../../auth/useApi';
import { DataFreshness } from '../../../components/DataFreshness';
import { ExplainerPanel } from '../../../components/ExplainerPanel';
import { InfoTip } from '../../../components/InfoTip';
import { KpiFilterTile } from '../../../components/KpiFilterTile';
import { WorkspacePageHeader } from '../../../components/WorkspacePageHeader';
import type { CapabilityDecision } from '../../../capabilities/capabilityTypes';
import type { ApiFetch } from '../../users/usersApi';
import { fetchLicenseHygiene, type LicenseHygieneAssignedSku, type LicenseHygieneDisabledAccount, type LicenseHygieneLoader, type LicenseHygieneResponse, type LicenseHygieneSku, type LicenseHygieneSourceStatus } from './licenseHygieneApi';
import { messages } from '../../../app/messages';
import './licenseHygiene.css';

export { fetchLicenseHygiene } from './licenseHygieneApi';
export type { LicenseHygieneResponse } from './licenseHygieneApi';

type Category = 'all' | 'capacity' | 'disabled-accounts';

type Props = {
  workspaceId: string;
  loadHygiene?: LicenseHygieneLoader;
  capabilities?: CapabilityDecision[];
  enabledModules?: string[];
  assignedModules?: string[];
};

export function LicenseHygienePage(props: Props) {
  if (props.loadHygiene) return <LoadedLicenseHygienePage {...props} loader={props.loadHygiene} />;
  return <AuthenticatedLicenseHygienePage {...props} />;
}

function AuthenticatedLicenseHygienePage(props: Props) {
  const api = useApi() as ApiFetch;
  const loader = useCallback((signal?: AbortSignal) => fetchLicenseHygiene(api, signal), [api]);
  return <LoadedLicenseHygienePage {...props} loader={loader} />;
}

function LoadedLicenseHygienePage({ workspaceId, loader, capabilities = [], enabledModules = [], assignedModules = [] }: Props & { loader: LicenseHygieneLoader }) {
  const [snapshot, setSnapshot] = useState<LicenseHygieneResponse | null>(null);
  const [loadedWorkspaceId, setLoadedWorkspaceId] = useState<string | null>(null);
  const [loadState, setLoadState] = useState<'loading' | 'ready' | 'error'>('loading');
  const [loadFailure, setLoadFailure] = useState<'permission' | 'service' | null>(null);
  const [attempt, setAttempt] = useState(0);
  const [search, setSearch] = useState('');
  const [selectedSkuId, setSelectedSkuId] = useState('');
  const [category, setCategory] = useState<Category>('all');
  const [capacityPage, setCapacityPage] = useState(1);
  const [accountPage, setAccountPage] = useState(1);
  const [refreshing, setRefreshing] = useState(false);
  const activeSnapshot = loadedWorkspaceId === workspaceId ? snapshot : null;
  const loading = !activeSnapshot && loadState !== 'error' || loadState === 'loading';
  const usersDecision = capabilities.find(decision => decision.capability === 'users.view');
  const userDetailsAllowed = (usersDecision?.state === 'allowed' || usersDecision?.state === 'read_only')
    && enabledModules.includes('users')
    && assignedModules.includes('users');

  useEffect(() => {
    if (!workspaceId) {
      setSnapshot(null);
      setLoadedWorkspaceId(null);
      setLoadState('error');
      setLoadFailure('service');
      setSearch('');
      setSelectedSkuId('');
      setCategory('all');
      setCapacityPage(1);
      setAccountPage(1);
      return;
    }
    const controller = new AbortController();
    setSnapshot(null);
    setLoadedWorkspaceId(null);
    setLoadState('loading');
    setLoadFailure(null);
    setSearch('');
    setSelectedSkuId('');
    setCategory('all');
    setCapacityPage(1);
    setAccountPage(1);
    loader(controller.signal).then(value => {
      if (!controller.signal.aborted) {
        setSnapshot(value);
        setLoadedWorkspaceId(workspaceId);
        setLoadState('ready');
      }
    }).catch((reason: unknown) => {
      if (!controller.signal.aborted) {
        setSnapshot(null);
        setLoadedWorkspaceId(workspaceId);
        setLoadState('error');
        const status = typeof reason === 'object' && reason !== null && 'status' in reason
          ? (reason as { status?: number }).status
          : undefined;
        setLoadFailure(status === 401 || status === 403 ? 'permission' : 'service');
      }
    }).finally(() => {
      if (!controller.signal.aborted) setRefreshing(false);
    });
    return () => controller.abort();
  }, [workspaceId, loader, attempt]);

  const clearFilters = () => {
    setSearch('');
    setSelectedSkuId('');
    setCategory('all');
    setCapacityPage(1);
    setAccountPage(1);
  };

  const refresh = () => {
    setSnapshot(null);
    setLoadedWorkspaceId(null);
    setLoadState('loading');
    setLoadFailure(null);
    setRefreshing(true);
    setSearch('');
    setSelectedSkuId('');
    setCategory('all');
    setCapacityPage(1);
    setAccountPage(1);
    setAttempt(value => value + 1);
  };

  const accessibleSnapshot = activeSnapshot && (activeSnapshot.access.state === 'allowed' || activeSnapshot.access.state === 'read_only')
    ? activeSnapshot
    : null;
  const skuChoices = useMemo(() => {
    if (!accessibleSnapshot) return [];
    const values = new Map<string, string>();
    for (const item of accessibleSnapshot.capacityItems) values.set(item.skuId, skuLabel(item));
    for (const account of accessibleSnapshot.disabledAccounts) {
      for (const sku of account.assignedLicenses) values.set(sku.skuId, skuLabel(sku));
    }
    return [...values].sort((a, b) => a[1].localeCompare(b[1]));
  }, [accessibleSnapshot]);

  const baseFiltered = useMemo(() => {
    if (!accessibleSnapshot) return { capacity: [] as LicenseHygieneSku[], accounts: [] as LicenseHygieneDisabledAccount[] };
    const query = search.trim().toLocaleLowerCase();
    const skuMatchesQuery = (sku: LicenseHygieneAssignedSku | LicenseHygieneSku) =>
      [sku.skuId, sku.partNumber, sku.displayName].some(value => value?.toLocaleLowerCase().includes(query));
    const capacity = accessibleSnapshot.capacityItems.filter(item =>
      (!selectedSkuId || item.skuId === selectedSkuId)
      && (!query || skuMatchesQuery(item)));
    const accounts = accessibleSnapshot.disabledAccounts.filter(account => {
      const assignedSkuMatch = account.assignedLicenses.some(sku =>
        (!selectedSkuId || sku.skuId === selectedSkuId)
        && (!query || skuMatchesQuery(sku)));
      const identityMatch = [account.id, account.displayName, account.userPrincipalName]
        .some(value => value?.toLocaleLowerCase().includes(query));
      return (!selectedSkuId || account.assignedLicenses.some(sku => sku.skuId === selectedSkuId))
        && (!query || identityMatch || assignedSkuMatch);
    });
    return { capacity, accounts };
  }, [accessibleSnapshot, search, selectedSkuId]);

  const capacityCount = sumAvailable(baseFiltered.capacity);
  const inventoryAvailable = accessibleSnapshot !== null && (accessibleSnapshot.inventory.freshness !== 'unavailable' || accessibleSnapshot.capacityItems.length > 0);
  const userSourceAvailable = accessibleSnapshot !== null && (accessibleSnapshot.userEvidence.freshness !== 'unavailable' || accessibleSnapshot.disabledAccounts.length > 0);
  const userEvidencePartial = accessibleSnapshot !== null && (accessibleSnapshot.userEvidence.partialData || !accessibleSnapshot.coverage.completed);
  const capacityMetric = !accessibleSnapshot ? messages.licenseHygieneStatusUnavailable
    : accessibleSnapshot.inventory.freshness === 'unavailable'
      ? accessibleSnapshot.capacityItems.length ? `${capacityCount} observed` : messages.licenseHygieneStatusUnavailable
      : !inventoryAvailable ? messages.licenseHygieneStatusUnavailable
        : accessibleSnapshot.inventory.partialData && baseFiltered.capacity.length === 0 ? 'Unknown'
          : accessibleSnapshot.inventory.partialData ? `${capacityCount} observed` : capacityCount;
  const accountMetric = !accessibleSnapshot ? messages.licenseHygieneStatusUnavailable
    : accessibleSnapshot.userEvidence.freshness === 'unavailable'
      ? accessibleSnapshot.disabledAccounts.length ? `${baseFiltered.accounts.length} observed` : messages.licenseHygieneStatusUnavailable
      : !userSourceAvailable ? messages.licenseHygieneStatusUnavailable
        : userEvidencePartial && baseFiltered.accounts.length === 0 ? '0 observed'
          : userEvidencePartial ? `${baseFiltered.accounts.length} observed` : baseFiltered.accounts.length;

  const pageCapacity = baseFiltered.capacity.slice((capacityPage - 1) * 25, capacityPage * 25);
  const pageAccounts = baseFiltered.accounts.slice((accountPage - 1) * 25, accountPage * 25);
  const showCapacity = category === 'all' || category === 'capacity';
  const showAccounts = category === 'all' || category === 'disabled-accounts';
  const statusLoading = loading;

  return <section className="license-hygiene-page">
    <WorkspacePageHeader eyebrow="Licenses" title={messages.licenseHygieneTitle} description={messages.licenseHygieneDescription} meta={accessibleSnapshot ? <button type="button" className="button button--tertiary button--sm" onClick={refresh} disabled={refreshing}>{refreshing ? messages.licenseHygieneRefreshing : messages.licenseHygieneRefresh}</button> : undefined} />
    <ExplainerPanel title={messages.licenseHygieneExplainerTitle}>
      <p>{messages.licenseHygieneExplainer}</p>
      <p>{messages.licenseHygieneLimits}</p>
    </ExplainerPanel>
    <div className="license-hygiene-results" aria-busy={statusLoading} aria-label="License hygiene results">
    {loadState === 'error' && !activeSnapshot
      ? <section className="license-state"><p role="alert">{loadFailure === 'permission' ? messages.licenseHygieneDenied : messages.licenseHygieneUnavailable}</p><button type="button" onClick={refresh}>{messages.licenseHygieneRetry}</button></section>
      : !activeSnapshot
        ? <p role="status">{messages.licenseHygieneLoading}</p>
        : activeSnapshot.access.state !== 'allowed' && activeSnapshot.access.state !== 'read_only'
          ? <section className="license-state"><p role="alert">{messages.licenseHygieneDenied}</p><button type="button" onClick={refresh}>{messages.licenseHygieneRetry}</button></section>
          : <>
            <div className="license-hygiene-sources" aria-label="License hygiene source status">
              <SourceFreshness status={accessibleSnapshot!.inventory} source={messages.licenseHygieneInventorySource} onRefresh={refresh} />
              <SourceFreshness status={accessibleSnapshot!.userEvidence} source={messages.licenseHygieneUserSource} partialData={userEvidencePartial} onRefresh={refresh} />
            </div>
            {accessibleSnapshot!.inventory.error && <p role="alert">{messages.licenseHygieneInventoryError}: {accessibleSnapshot!.inventory.error.message}</p>}
            {accessibleSnapshot!.userEvidence.error && <p role="alert">{messages.licenseHygieneUserError}: {accessibleSnapshot!.userEvidence.error.message}</p>}
            <p className="license-hygiene-filter-note">{messages.licenseHygieneFilterDescription}</p>
            <div className="metric-grid license-hygiene-kpis">
              <div className="license-hygiene-kpi license-hygiene-kpi--capacity">
                <KpiFilterTile
                  label={messages.licenseHygieneCapacity}
                  value={capacityMetric}
                  detail={accessibleSnapshot!.inventory.partialData ? 'Partial inventory evidence' : 'Verified available seats'}
                  selected={category === 'capacity'}
                  onClick={() => { setCategory(value => value === 'capacity' ? 'all' : 'capacity'); setCapacityPage(1); setAccountPage(1); }}
                />
                <InfoTip label={messages.licenseHygieneCapacity} content={<p>{messages.licenseHygieneCapacityDefinition}</p>} />
              </div>
              <div className={`license-hygiene-kpi license-hygiene-kpi--accounts${baseFiltered.accounts.length > 0 && !userEvidencePartial && userSourceAvailable ? ' has-findings' : ' is-incomplete'}`}>
                <KpiFilterTile
                  label={messages.licenseHygieneDisabledAccounts}
                  value={accountMetric}
                  detail={userEvidencePartial ? messages.licenseHygienePartial : 'Review findings'}
                  selected={category === 'disabled-accounts'}
                  onClick={() => { setCategory(value => value === 'disabled-accounts' ? 'all' : 'disabled-accounts'); setCapacityPage(1); setAccountPage(1); }}
                />
                <InfoTip label={messages.licenseHygieneDisabledAccounts} content={<p>{messages.licenseHygieneDisabledDefinition}</p>} />
              </div>
            </div>
            <p>{messages.licenseHygieneSnapshotScope}</p>
            <p>{messages.licenseHygieneCoverageScope}</p>
            <div className="license-toolbar license-hygiene-toolbar">
              <label>{messages.licenseHygieneSearchLabel}<input type="search" aria-label={messages.licenseHygieneSearchLabel} placeholder={messages.licenseHygieneSearchPlaceholder} value={search} onChange={event => { setSearch(event.target.value); setCapacityPage(1); setAccountPage(1); }} /></label>
              <label>{messages.licenseHygieneSkuLabel}<select aria-label={messages.licenseHygieneSkuLabel} value={selectedSkuId} onChange={event => { setSelectedSkuId(event.target.value); setCapacityPage(1); setAccountPage(1); }}>
                <option value="">{messages.licenseHygieneSkuAny}</option>
                {skuChoices.map(([id, label]) => <option key={id} value={id}>{label} ({id})</option>)}
              </select></label>
              <button type="button" className="button button--secondary" onClick={clearFilters}>{messages.licenseHygieneClearFilters}</button>
            </div>
            <section className={`license-hygiene-coverage${!accessibleSnapshot!.coverage.completed || accessibleSnapshot!.coverage.missingEvidenceRecords > 0 ? ' is-partial' : ''}`} aria-label={messages.licenseHygieneCoverage}>
              <h2>{messages.licenseHygieneCoverage}</h2>
              <p>{messages.licenseHygieneRecordsAssessed}: {accessibleSnapshot!.coverage.recordsAssessed}. {accessibleSnapshot!.coverage.completed ? messages.licenseHygieneComplete : messages.licenseHygienePartial}. {messages.licenseHygieneStopReason(accessibleSnapshot!.coverage.stopReason)}</p>
              <p>{messages.licenseHygieneMissingEvidence(accessibleSnapshot!.coverage.missingEvidenceRecords)}</p>
              <p>{messages.licenseHygieneUnscannedUnknown}</p>
              <p>{messages.licenseHygieneUnsupportedInactivity}</p>
              <p>{messages.licenseHygieneUnsupportedOverlap}</p>
            </section>
            <div>
              {showCapacity && <section aria-labelledby="hygiene-capacity-title">
                <h2 id="hygiene-capacity-title">{messages.licenseHygieneCapacityTable}</h2>
                {!inventoryAvailable ? <p>{messages.licenseHygieneNoCapacityAssessment}</p>
                  : baseFiltered.capacity.length === 0 ? <p>{messages.licenseHygieneNoCapacity}</p>
                    : <div className="users-table-wrap"><table className="users-table">
                      <thead><tr><th scope="col">SKU</th><th scope="col" className="numeric">{messages.licenseHygienePurchased}</th><th scope="col" className="numeric">{messages.licenseHygieneAssigned}</th><th scope="col" className="numeric">{messages.licenseHygieneAvailable}</th></tr></thead>
                      <tbody>{pageCapacity.map(item => <CapacityRow key={item.skuId} item={item} />)}</tbody>
                    </table></div>}
                {inventoryAvailable && <Pagination label={messages.licenseHygieneCapacityPages} page={capacityPage} total={baseFiltered.capacity.length} onChange={setCapacityPage} />}
              </section>}
              {showAccounts && <section aria-labelledby="hygiene-accounts-title">
                <h2 id="hygiene-accounts-title">{messages.licenseHygieneAccountsTable}</h2>
                {!userSourceAvailable ? <p>{messages.licenseHygieneNoAccountAssessment}</p>
                  : baseFiltered.accounts.length === 0 ? <p>{userEvidencePartial ? messages.licenseHygieneNoPartialAccounts : messages.licenseHygieneNoAccounts}</p>
                    : <div className="users-table-wrap"><table className="users-table">
                      <thead><tr><th scope="col">{messages.licenseHygieneAccount}</th><th scope="col">{messages.licenseHygieneAccountStatus}</th><th scope="col">{messages.licenseHygieneAssignedLicenses}</th><th scope="col">{messages.licenseHygieneEvidenceAt}</th><th scope="col">{messages.licenseHygieneEvidenceSource}</th></tr></thead>
                      <tbody>{pageAccounts.map(account => <AccountRow key={account.id} account={account} userDetailsAllowed={userDetailsAllowed} />)}</tbody>
                    </table></div>}
                {userSourceAvailable && <Pagination label={messages.licenseHygieneAccountPages} page={accountPage} total={baseFiltered.accounts.length} onChange={setAccountPage} />}
              </section>}
            </div>
          </>}
    </div>
  </section>;
}

function SourceFreshness({ status, source, partialData = status.partialData, onRefresh }: { status: LicenseHygieneSourceStatus; source: string; partialData?: boolean; onRefresh: () => void }) {
  return <DataFreshness
    presentation="banner"
    fetchedAt={status.fetchedAt}
    freshness={toW0Freshness(status.freshness)}
    partialData={partialData}
    source={source}
    message={status.error?.message}
    onRefresh={onRefresh}
  />;
}

function toW0Freshness(freshness: string): 'fresh' | 'stale' | 'unavailable' {
  if (freshness === 'live' || freshness === 'fresh') return 'fresh';
  if (freshness === 'stale') return 'stale';
  return 'unavailable';
}

function CapacityRow({ item }: { item: LicenseHygieneSku }) {
  return <tr>
    <th scope="row" data-label="SKU"><strong>{item.displayName || messages.licenseHygieneProductUnavailable}</strong>{item.displayName === item.partNumber && <span className="license-unavailable">{messages.licenseHygieneProductUnavailable}</span>}
      <details><summary>Identifiers</summary><dl><div><dt>Part number</dt><dd>{item.partNumber || messages.licenseHygieneProductUnavailable}</dd></div><div><dt>SKU ID</dt><dd>{item.skuId}</dd></div></dl></details>
    </th>
    <td className="numeric" data-label={messages.licenseHygienePurchased}>{item.purchased}</td>
    <td className="numeric" data-label={messages.licenseHygieneAssigned}>{item.assigned}</td>
    <td className="numeric" data-label={messages.licenseHygieneAvailable}>{item.available}</td>
  </tr>;
}

function AccountRow({ account, userDetailsAllowed }: { account: LicenseHygieneDisabledAccount; userDetailsAllowed: boolean }) {
  const identity = account.displayName || account.userPrincipalName || account.id;
  return <tr>
    <th scope="row" data-label={messages.licenseHygieneAccount}>{userDetailsAllowed ? <a href={`/users/${encodeURIComponent(account.id)}`}>{identity}</a> : identity}<small>{account.userPrincipalName || account.id}</small></th>
    <td data-label={messages.licenseHygieneAccountStatus}><span className="status-badge" data-tone="danger">{messages.licenseHygieneStatusDisabled}</span></td>
    <td data-label={messages.licenseHygieneAssignedLicenses}><ul>{account.assignedLicenses.map(sku => <li key={sku.skuId}>{skuLabel(sku)} <code>{sku.skuId}</code>{sku.partNumber && <small>{sku.partNumber}</small>}</li>)}</ul></td>
    <td data-label={messages.licenseHygieneEvidenceAt}><time dateTime={account.evidenceAt}>{account.evidenceAt}</time></td>
    <td data-label={messages.licenseHygieneEvidenceSource}>{account.evidenceSource}</td>
  </tr>;
}

function Pagination({ label, page, total, onChange }: { label: string; page: number; total: number; onChange: (nextPage: number) => void }) {
  const pages = Math.max(1, Math.ceil(total / 25));
  const first = total ? (page - 1) * 25 + 1 : 0;
  const last = Math.min(page * 25, total);
  return <nav className="table-pagination" aria-label={label}>
    <span>Page {page} of {pages} · {first}–{last} of {total}</span>
    <button type="button" onClick={() => onChange(page - 1)} disabled={page <= 1}>{messages.licenseHygienePreviousPage}</button>
    <button type="button" onClick={() => onChange(page + 1)} disabled={page >= pages}>{messages.licenseHygieneNextPage}</button>
  </nav>;
}

function skuLabel(sku: LicenseHygieneAssignedSku | LicenseHygieneSku) {
  return sku.displayName || sku.partNumber || messages.licenseHygieneProductUnavailable;
}

function sumAvailable(items: LicenseHygieneSku[]) {
  return items.reduce((sum, item) => sum + item.available, 0);
}
