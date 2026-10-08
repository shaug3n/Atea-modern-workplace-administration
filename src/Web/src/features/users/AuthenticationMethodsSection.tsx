import React, { useEffect, useState } from 'react';
import { messages } from '../../app/messages';
import { useApi } from '../../auth/useApi';
import { ActionGroup } from '../../components/ActionGroup';
import { TechnicalDetails } from '../../components/TechnicalDetails';
import { formatDate } from '../../format/dateTime';
import { humanizeAuthMethodType } from '../../format/humanize';
import { DataFreshness } from '../../components/DataFreshness';
import { ConfirmationDialog } from '../../components/ConfirmationDialog';
import { LoadingSkeleton } from '../../components/LoadingSkeleton';
import { PermissionState } from '../../components/PermissionState';
import type { CapabilityDecision } from '../../capabilities/capabilityTypes';
import { fetchAuthenticationMethods, removeAuthenticationMethod, resetAuthenticationMethods, type AuthenticationMethod, type AuthenticationMethodsResponse } from './authenticationMethodsApi';
import type { ApiFetch } from './userDetailApi';
import { TemporaryAccessPassDialog } from './TemporaryAccessPassDialog';

export type AuthenticationMethodsSummary = { status: 'loading' | 'available' | 'unavailable'; items: AuthenticationMethod[] };

export function AuthenticationMethodsSection({ userId, userLabel, decision, manageDecision, onResult, onAuditWarning, onTemporaryAccessPassOpenChange }: { userId: string; userLabel?: string; decision: CapabilityDecision; manageDecision?: CapabilityDecision; onResult?: (summary: AuthenticationMethodsSummary) => void; onAuditWarning?: (warning: string | null) => void; onTemporaryAccessPassOpenChange?: (open: boolean) => void }) {
  const api = useApi();
  const [result, setResult] = useState<AuthenticationMethodsResponse | null>(null);
  const [failed, setFailed] = useState(false);
  const [loading, setLoading] = useState(false);
  const [retryVersion, setRetryVersion] = useState(0);
  const [removeTarget, setRemoveTarget] = useState<AuthenticationMethod | null>(null);
  const [pending, setPending] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);
  const [auditWarning, setAuditWarning] = useState<string | null>(null);
  const [resetOpen, setResetOpen] = useState(false);
  const [tapOpen, setTapOpen] = useState(false);

  useEffect(() => {
    if (decision.state !== 'allowed' && decision.state !== 'read_only') {
      setResult(null);
      setFailed(false);
      setLoading(false);
      onResult?.({ status: 'unavailable', items: [] });
      return;
    }

    let cancelled = false;
    setResult(null);
    setFailed(false);
    setLoading(true);
    onResult?.({ status: 'loading', items: [] });
    fetchAuthenticationMethods(api as ApiFetch, userId)
      .then((response) => {
        if (!cancelled) {
          setResult(response);
          setLoading(false);
          onResult?.({ status: response.error || response.freshness === 'unavailable' || response.partialData ? 'unavailable' : 'available', items: response.items });
        }
      })
      .catch(() => {
        if (!cancelled) {
          setFailed(true);
          setLoading(false);
          onResult?.({ status: 'unavailable', items: [] });
        }
      });
    return () => { cancelled = true; };
  }, [api, decision.state, userId, retryVersion, onResult]);

  const remove = async () => {
    if (!removeTarget) return;
    setPending(true);
    setActionError(null);
    try {
      const response = await removeAuthenticationMethod(api as ApiFetch, userId, removeTarget);
      setAuditWarning(readAuditWarning(response));
      setRemoveTarget(null);
      updateResult(await fetchAuthenticationMethods(api as ApiFetch, userId));
    } catch (error) {
      setActionError(error instanceof Error ? error.message : messages.userAuthenticationMethodsActionFailed);
    } finally {
      setPending(false);
    }
  };

  const resetMfa = async () => {
    setPending(true);
    setActionError(null);
    try {
      const response = await resetAuthenticationMethods(api as ApiFetch, userId);
      setAuditWarning(readAuditWarning(response));
      setResetOpen(false);
      updateResult(await fetchAuthenticationMethods(api as ApiFetch, userId));
    } catch (error) {
      setActionError(error instanceof Error ? error.message : messages.userAuthenticationMethodsActionFailed);
    } finally {
      setPending(false);
    }
  };

  const hasRemovableMethods = Boolean(result?.items.some((method) => method.type !== 'passwordAuthenticationMethod'));

  const updateResult = (response: AuthenticationMethodsResponse) => {
    setResult(response);
    onResult?.({ status: response.error || response.freshness === 'unavailable' || response.partialData ? 'unavailable' : 'available', items: response.items });
  };

  if (decision.state !== 'allowed' && decision.state !== 'read_only') {
    return (
      <section className="detail-section detail-section--wide" aria-labelledby="authentication-methods-section-title" aria-busy={false}>
        <div className="detail-section__header">
          <h2 id="authentication-methods-section-title">{messages.userAuthenticationMethodsSection}</h2>
        </div>
        <PermissionState decision={decision}>
          <p className="section-help">{messages.userProfileAuthenticationMethodsHelp}</p>
        </PermissionState>
      </section>
    );
  }

  return (
    <section className="detail-section detail-section--wide" aria-labelledby="authentication-methods-section-title" aria-busy={loading}>
      <div className="detail-section__header">
        <h2 id="authentication-methods-section-title">{messages.userAuthenticationMethodsSection}</h2>
        {result && <DataFreshness fetchedAt={result.fetchedAt} freshness={result.freshness === 'live' ? 'fresh' : result.freshness === 'stale' ? 'stale' : 'unavailable'} partialData={result.partialData} message={result.error?.message} />}
        {manageDecision?.state === 'allowed' && <div className="section-actions"><button type="button" className="button button--secondary" onClick={() => { setTapOpen(true); onTemporaryAccessPassOpenChange?.(true); }}>Grant Temporary Access Pass</button></div>}
      </div>
      <p className="section-help">{messages.userProfileAuthenticationMethodsHelp}</p>
      {loading && <LoadingSkeleton label={messages.userAuthenticationMethodsLoading} lines={3} />}
      {failed && <><p role="alert">{messages.userAuthenticationMethodsUnavailable}</p><button type="button" className="button button--secondary button--sm" onClick={() => setRetryVersion(version => version + 1)}>{messages.userProfileRetryAuthenticationMethods}</button></>}
      {result?.error && <><p role="alert">{messages.userAuthenticationMethodsUnavailable}</p><button type="button" className="button button--secondary button--sm" onClick={() => setRetryVersion(version => version + 1)}>{messages.userProfileRetryAuthenticationMethods}</button></>}
      {result && !result.error && result.items.length === 0 && <p>{messages.userProfileNoAuthenticationMethods}</p>}
      {actionError && <p role="alert" className="action-feedback action-feedback--error">{actionError}</p>}
      {auditWarning && <p role="alert" className="audit-warning">{auditWarning}</p>}
      {result && !result.error && result.items.length > 0 && <div className="detail-table-wrap"><table className="detail-table"><caption className="sr-only">Authentication methods</caption><thead><tr><th scope="col">Method</th><th scope="col">Type</th><th scope="col">Registered</th><th scope="col"><span className="sr-only">Actions</span></th></tr></thead><tbody>{result.items.map((method) => <tr key={method.id}><th scope="row" data-label="Method">{method.displayName}</th><td data-label="Type">{humanizeAuthMethodType(method.type)}{method.model && <span className="table-subtext">{method.model}</span>}<TechnicalDetails summary="Technical details" items={[{ label: 'Method type', value: method.type }]} /></td><td data-label="Registered">{method.createdDateTime ? formatDate(method.createdDateTime) : messages.usersUnavailableValue}</td><td data-label="Actions" className="detail-table__actions">{manageDecision?.state === 'allowed' && method.type !== 'passwordAuthenticationMethod' && <button type="button" className="button button--tertiary button--sm button--danger-text" onClick={() => setRemoveTarget(method)}>{messages.userAuthenticationMethodsRemove}</button>}</td></tr>)}</tbody></table></div>}
      {decision.state !== 'allowed' && <p className="section-help">{messages.userAuthenticationMethodsReadOnly}</p>}
      {manageDecision?.state === 'allowed' && hasRemovableMethods && <ActionGroup tone="danger" description="Removes all registered methods except password. The user must register again."><button type="button" className="button button--danger" onClick={() => setResetOpen(true)}>{messages.userAuthenticationMethodsReset}</button></ActionGroup>}
      {removeTarget && <ConfirmationDialog title={messages.userAuthenticationMethodsRemoveTitle} target={removeTarget.displayName} proposedChange={messages.userAuthenticationMethodsRemoveDescription} requiredCapability="authentication.methods.manage" confirmLabel={messages.confirmRemoveMethod} consequence={messages.confirmRemoveMethodConsequence} tone="danger" busy={pending} onConfirm={() => void remove()} onCancel={() => { if (!pending) setRemoveTarget(null); }} />}
      {resetOpen && <ConfirmationDialog title={messages.userAuthenticationMethodsResetTitle} target={userLabel || userId} proposedChange={messages.userAuthenticationMethodsResetDescription} requiredCapability="authentication.methods.manage" destructivePhrase="RESET MFA" confirmLabel={messages.confirmResetMfa} consequence={messages.confirmResetMfaConsequence} tone="danger" busy={pending} onConfirm={() => void resetMfa()} onCancel={() => { if (!pending) setResetOpen(false); }} />}
      {tapOpen && <TemporaryAccessPassDialog userId={userId} target={userLabel || userId} onClose={() => { setTapOpen(false); onTemporaryAccessPassOpenChange?.(false); }} onAuditWarning={onAuditWarning} />}
    </section>
  );
}

function readAuditWarning(response: unknown) {
  const warning = response && typeof response === 'object' && 'auditWarning' in response ? (response as { auditWarning?: unknown }).auditWarning : null;
  return typeof warning === 'string' && warning.trim() ? warning : null;
}
