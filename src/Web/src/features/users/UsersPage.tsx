import React, { useCallback, useEffect, useMemo, useState } from 'react';
import type { CapabilityDecision } from '../../capabilities/capabilityTypes';
import { WorkspaceDataState } from '../../components/WorkspaceDataState';
import { WorkspacePageHeader } from '../../components/WorkspacePageHeader';
import { useWorkspaceIssueReporter } from '../../notifications/WorkspaceNotifications';
import { ConfirmationDialog } from '../../components/ConfirmationDialog';
import { DataFreshness } from '../../components/DataFreshness';
import { PermissionState } from '../../components/PermissionState';
import { useApi } from '../../auth/useApi';
import { messages } from '../../app/messages';
import { UserFilters } from './UserFilters';
import { UserCreateDialog } from './UserCreateDialog';
import { UsersTable } from './UsersTable';
import { mutateUser, type UserCommandResponse } from './userMutationApi';
import { fetchUsers, type ApiFetch, type UserFiltersState, type UsersDirectoryResponse, type UserSummary } from './usersApi';
import { downloadCsv, exportStatus } from '../exports/csvExport';

const emptyFilters: UserFiltersState = {
  search: '',
  accountStatus: '',
  tenantRole: '',
  license: '',
  userType: '',
};

export function UsersPage({ capabilities, onNavigate, loadUsers }: { capabilities: CapabilityDecision[]; onNavigate?: (path: string) => void; loadUsers?: (filters: UserFiltersState, continuationToken: string | null) => Promise<UsersDirectoryResponse> }) {
  const api = useApi();
  const issueReporter = useWorkspaceIssueReporter();
  const [filters, setFilters] = useState(() => filtersFromUrl());
  const [debouncedFilters, setDebouncedFilters] = useState(() => filtersFromUrl());
  const [continuationToken, setContinuationToken] = useState<string | null>(null);
  const [previousTokens, setPreviousTokens] = useState<string[]>([]);
  const [result, setResult] = useState<UsersDirectoryResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadFailed, setLoadFailed] = useState(false);
  const [refreshVersion, setRefreshVersion] = useState(0);
  const [disableTarget, setDisableTarget] = useState<UserSummary | null>(null);
  const [disablePending, setDisablePending] = useState(false);
  const [disableError, setDisableError] = useState<string | null>(null);
  const [disableStatus, setDisableStatus] = useState<string | null>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [exportPending, setExportPending] = useState(false);
  const [exportMessage, setExportMessage] = useState<string | null>(null);
  const [exportError, setExportError] = useState<string | null>(null);

  const usersView = findDecision(capabilities, 'users.view');
  const usersCreate = findDecision(capabilities, 'users.create');
  const usersDisable = findDecision(capabilities, 'users.disable');
  const loader = useMemo(() => loadUsers ?? ((nextFilters: UserFiltersState, token: string | null) => fetchUsers(api as ApiFetch, nextFilters, token)), [api, loadUsers]);
  const disableTargetName = disableTarget ? displayName(disableTarget) : '';

  useEffect(() => {
    const restore = () => setFilters(filtersFromUrl());
    window.addEventListener('popstate', restore);
    return () => window.removeEventListener('popstate', restore);
  }, []);

  useEffect(() => {
    const url = new URL(window.location.href);
    for (const key of ['search', 'accountStatus', 'license', 'userType', 'tenantRole']) url.searchParams.delete(key);
    for (const key of ['search', 'accountStatus', 'license', 'userType'] as const) {
      if (filters[key].trim()) url.searchParams.set(key, filters[key].trim());
    }
    window.history.replaceState(window.history.state, '', `${url.pathname}${url.search}${url.hash}`);
  }, [filters]);

  useEffect(() => {
    const timeout = window.setTimeout(() => {
      setPreviousTokens([]);
      setContinuationToken(null);
      setDebouncedFilters(filters);
    }, 300);
    return () => window.clearTimeout(timeout);
  }, [filters]);

  useEffect(() => {
    if (usersView.state !== 'allowed' && usersView.state !== 'read_only') {
      setLoading(false);
      setResult(null);
      return;
    }

    let cancelled = false;
    setLoading(true);
    setLoadFailed(false);
    loader(debouncedFilters, continuationToken)
      .then((response) => {
        if (!cancelled) {
          setResult(response);
          if (response.error) issueReporter.report({ key: 'users:read', area: 'users', kind: 'service', severity: 'warning', title: 'User data unavailable', detail: 'Try loading users again.' });
          else issueReporter.clear('users:read');
        }
      })
      .catch(() => {
        if (!cancelled) { setLoadFailed(true); issueReporter.report({ key: 'users:read', area: 'users', kind: 'service', severity: 'warning', title: 'User data unavailable', detail: 'Try loading users again.' }); }
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => { cancelled = true; };
  }, [continuationToken, debouncedFilters, loader, refreshVersion, usersView.state, issueReporter]);

  const goNext = () => {
    if (!result?.continuationToken) return;
    if (continuationToken) setPreviousTokens((tokens) => [...tokens, continuationToken]);
    else setPreviousTokens((tokens) => [...tokens, '']);
    setContinuationToken(result.continuationToken);
  };

  const goPrevious = () => {
    setPreviousTokens((tokens) => {
      const next = [...tokens];
      const previous = next.pop();
      setContinuationToken(previous || null);
      return next;
    });
  };

  const startDisable = useCallback((user: UserSummary) => {
    setDisableTarget(user);
    setDisableError(null);
    setDisableStatus(null);
  }, []);

  const cancelDisable = useCallback(() => {
    if (disablePending) return;
    setDisableTarget(null);
    setDisableError(null);
  }, [disablePending]);

  const submitDisable = useCallback(async () => {
    if (!disableTarget) return;
    if (usersDisable.state !== 'allowed') {
      setDisableTarget(null);
      setDisableError(messages.userDisablePermissionDenied);
      return;
    }

    setDisablePending(true);
    setDisableError(null);
    try {
      const response = await mutateUser(
        api as ApiFetch,
        `/api/users/${encodeURIComponent(disableTarget.id)}/disable`,
        'POST',
        {},
      );
      if (response.status !== 'succeeded') {
        setDisableError(formatMutationError(response));
        return;
      }

      setDisableStatus(`${displayName(disableTarget)} ${messages.userDisableSucceeded}`);
      setDisableTarget(null);
      setRefreshVersion((version) => version + 1);
    } catch {
      setDisableError(messages.userDisableFailed);
    } finally {
      setDisablePending(false);
    }
  }, [api, disableTarget, usersDisable.state]);

  const state = loading ? 'loading' : loadFailed ? 'error' : result && result.items.length === 0 ? 'empty' : 'ready';
  const readable = usersView.state === 'allowed' || usersView.state === 'read_only';

  const exportUsers = async () => {
    if (!readable || exportPending) return;
    setExportPending(true);
    setExportMessage(null);
    setExportError(null);
    try {
      const parameters = new URLSearchParams();
      for (const key of ['search', 'accountStatus', 'license', 'userType'] as const) {
        if (filters[key].trim()) parameters.set(key, filters[key].trim());
      }
      const path = `/api/users/export.csv${parameters.size ? `?${parameters}` : ''}`;
      setExportMessage(exportStatus(await downloadCsv(api as ApiFetch, path, 'users.csv')));
    } catch {
      setExportError('Filtered users export failed. Check your permissions and try again.');
    } finally {
      setExportPending(false);
    }
  };

  return (
    <section className="users-page" aria-label={messages.usersTitle}>
      <WorkspacePageHeader eyebrow={messages.usersEyebrow} title={messages.usersTitle} description={messages.usersDirectoryIntro} actions={
        <div className="users-page__actions">
          <PermissionState decision={usersCreate}>
            <button type="button" onClick={() => setCreateOpen(true)}>{messages.usersCreateAction}</button>
          </PermissionState>
          <button type="button" onClick={() => setRefreshVersion((version) => version + 1)}>{messages.usersRefreshAction}</button>
          <button type="button" onClick={() => void exportUsers()} disabled={!readable || exportPending}>Export filtered CSV</button>
        </div>} />

      {exportMessage && <p role="status">{exportMessage}</p>}
      {exportError && <p role="alert">{exportError}</p>}

      <UserFilters filters={filters} onChange={setFilters} />

      {readable && result && (
        <DataFreshness
          fetchedAt={result.fetchedAt}
          freshness={result.freshness}
          partialData={result.partialData}
          message={result.error ? 'Some user data could not be loaded.' : undefined}
        />
      )}

      {!readable ? (
        <section className="permission-panel" role="status">
          <h2>{usersView.state.startsWith('pim_') ? messages.usersPimRequiredTitle : messages.usersNoPermissionTitle}</h2>
          {usersView.state === 'hidden' ? <p>{messages.usersNoPermissionBody}</p> : <PermissionState decision={usersView}><span>{usersView.state.startsWith('pim_') ? messages.usersPimRequiredBody : messages.usersNoPermissionBody}</span></PermissionState>}
        </section>
      ) : result?.error && result.items.length === 0 ? (
        <section className="permission-panel"><h2>{messages.usersUnavailable}</h2><WorkspaceDataState state="unavailable" message="User data is unavailable. Check Notifications for details." onRetry={() => setRefreshVersion((version) => version + 1)} /></section>
      ) : (
        state === 'loading' && !result ? <WorkspaceDataState state="loading" message="Loading users…" /> : state === 'error' ? <WorkspaceDataState state="unavailable" message="User data is unavailable. Check Notifications for details." onRetry={() => setRefreshVersion((version) => version + 1)} /> : state === 'empty' ? <WorkspaceDataState state="empty" message={messages.usersNoResults} /> : result && (
            <UsersTable
              users={result.items}
              capabilities={capabilities}
              onNavigate={onNavigate}
              onDisable={usersDisable.state === 'allowed' ? startDisable : undefined}
            />
          )
      )}

      {disableStatus && <p role="status">{disableStatus}</p>}
      {disableTarget && (
        <ConfirmationDialog
          title={messages.userDisableDialogTitle}
          target={disableTargetName}
          proposedChange={messages.userDisableProposedChange}
          requiredCapability="users.disable"
          destructivePhrase="DISABLE"
          busy={disablePending}
          onConfirm={submitDisable}
          onCancel={cancelDisable}
        />
      )}
      {disableError && <p role="alert">{disableError}</p>}
      {createOpen && <UserCreateDialog onCompleted={(response) => {
        if (response.status === 'succeeded') {
          setCreateOpen(false);
          setRefreshVersion((version) => version + 1);
        }
      }} />}

      {readable && !loadFailed && result && !result.error && <nav className="pagination-controls" aria-label={messages.usersPaginationLabel}>
        <span role="status" aria-live="polite">Page {previousTokens.length + 1}</span>
        <button type="button" onClick={goPrevious} disabled={loading || previousTokens.length === 0}>{messages.usersPreviousPage}</button>
        <button type="button" onClick={goNext} disabled={loading || !result.continuationToken}>{messages.usersNextPage}</button>
      </nav>}
    </section>
  );
}

function filtersFromUrl(): UserFiltersState {
  const parameters = new URLSearchParams(window.location.search);
  const accountStatus = parameters.get('accountStatus') ?? '';
  const userType = parameters.get('userType') ?? '';
  return {
    ...emptyFilters,
    search: parameters.get('search') ?? '',
    accountStatus: accountStatus === 'enabled' || accountStatus === 'disabled' ? accountStatus : '',
    userType: userType === 'Member' || userType === 'Guest' ? userType : '',
    license: parameters.get('license') ?? '',
  };
}

function findDecision(capabilities: CapabilityDecision[], capability: CapabilityDecision['capability']): CapabilityDecision {
  return capabilities.find((decision) => decision.capability === capability) ?? { capability, state: 'hidden', reasonCode: 'capability_not_returned' };
}

function displayName(user: UserSummary) {
  return user.displayName || user.userPrincipalName || user.mail || messages.usersUnnamedUser;
}

function formatMutationError(response: UserCommandResponse) {
  if (response.error === 'idempotency_key_reused') return messages.userMutationConflict;
  if (response.error === 'throttled' || response.status === 'temporarily_unavailable') return messages.userMutationThrottled;
  if (response.error === 'source_of_authority_read_only' || response.status === 'source_of_authority_read_only') return messages.userDisableSourceReadOnly;
  if (response.error === 'capability_required' || response.status === 'denied') return messages.userDisablePermissionDenied;
  return messages.userDisableFailed;
}
