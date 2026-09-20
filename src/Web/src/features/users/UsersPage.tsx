import React, { useCallback, useEffect, useMemo, useState } from 'react';
import type { CapabilityDecision } from '../../capabilities/capabilityTypes';
import { AsyncState } from '../../components/AsyncState';
import { ConfirmationDialog } from '../../components/ConfirmationDialog';
import { DataFreshness } from '../../components/DataFreshness';
import { PermissionState } from '../../components/PermissionState';
import { useApi } from '../../auth/useApi';
import { messages } from '../../app/messages';
import { UserFilters } from './UserFilters';
import { UsersTable } from './UsersTable';
import { mutateUser, type UserCommandResponse } from './userMutationApi';
import { fetchUsers, type ApiFetch, type UserFiltersState, type UsersDirectoryResponse, type UserSummary } from './usersApi';

const emptyFilters: UserFiltersState = {
  search: '',
  accountStatus: '',
  tenantRole: '',
  license: '',
  userType: '',
};

export function UsersPage({ capabilities, onNavigate, loadUsers }: { capabilities: CapabilityDecision[]; onNavigate?: (path: string) => void; loadUsers?: (filters: UserFiltersState, continuationToken: string | null) => Promise<UsersDirectoryResponse> }) {
  const api = useApi();
  const [filters, setFilters] = useState(emptyFilters);
  const [debouncedFilters, setDebouncedFilters] = useState(emptyFilters);
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

  const usersView = findDecision(capabilities, 'users.view');
  const usersCreate = findDecision(capabilities, 'users.create');
  const usersDisable = findDecision(capabilities, 'users.disable');
  const loader = useMemo(() => loadUsers ?? ((nextFilters: UserFiltersState, token: string | null) => fetchUsers(api as ApiFetch, nextFilters, token)), [api, loadUsers]);
  const disableTargetName = disableTarget ? displayName(disableTarget) : '';

  useEffect(() => {
    const timeout = window.setTimeout(() => {
      setPreviousTokens([]);
      setContinuationToken(null);
      setDebouncedFilters(filters);
    }, 300);
    return () => window.clearTimeout(timeout);
  }, [filters]);

  useEffect(() => {
    if (usersView.state === 'hidden') {
      setLoading(false);
      setResult({
        items: [],
        continuationToken: null,
        fetchedAt: new Date().toISOString(),
        freshness: 'unavailable',
        partialData: true,
        error: { category: 'capability_required', message: messages.usersNoPermissionBody, state: usersView.state },
      });
      return;
    }

    let cancelled = false;
    setLoading(true);
    setLoadFailed(false);
    loader(debouncedFilters, continuationToken)
      .then((response) => {
        if (!cancelled) setResult(response);
      })
      .catch(() => {
        if (!cancelled) setLoadFailed(true);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => { cancelled = true; };
  }, [continuationToken, debouncedFilters, loader, refreshVersion, usersView.state]);

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

  return (
    <section className="users-page" aria-labelledby="users-page-title">
      <div className="users-page__header">
        <div>
          <p className="eyebrow">{messages.usersEyebrow}</p>
          <h1 id="users-page-title">{messages.usersTitle}</h1>
          <p>{messages.usersDirectoryIntro}</p>
        </div>
        <div className="users-page__actions">
          <PermissionState decision={usersCreate}>
            <button type="button">{messages.usersCreateAction}</button>
          </PermissionState>
          <button type="button" onClick={() => setRefreshVersion((version) => version + 1)}>{messages.usersRefreshAction}</button>
        </div>
      </div>

      <UserFilters filters={filters} onChange={setFilters} />

      {result && (
        <DataFreshness
          fetchedAt={result.fetchedAt}
          freshness={result.freshness}
          partialData={result.partialData}
          message={result.error?.message}
        />
      )}

      {result?.error?.category === 'capability_required' && usersView.state === 'hidden' ? (
        <section className="permission-panel" role="status">
          <h2>{messages.usersNoPermissionTitle}</h2>
          <p>{messages.usersNoPermissionBody}</p>
        </section>
      ) : (
        <AsyncState state={state} empty={<span>{messages.usersNoResults}</span>}>
          {result && (
            <UsersTable
              users={result.items}
              capabilities={capabilities}
              onNavigate={onNavigate}
              onDisable={usersDisable.state === 'allowed' ? startDisable : undefined}
            />
          )}
        </AsyncState>
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

      <div className="pagination-controls" aria-label={messages.usersPaginationLabel}>
        <button type="button" onClick={goPrevious} disabled={previousTokens.length === 0}>{messages.usersPreviousPage}</button>
        <button type="button" onClick={goNext} disabled={!result?.continuationToken}>{messages.usersNextPage}</button>
      </div>
    </section>
  );
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
