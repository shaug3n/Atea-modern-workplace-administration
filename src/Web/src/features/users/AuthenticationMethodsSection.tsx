import React, { useEffect, useState } from 'react';
import { messages } from '../../app/messages';
import { useApi } from '../../auth/useApi';
import { DataFreshness } from '../../components/DataFreshness';
import { ConfirmationDialog } from '../../components/ConfirmationDialog';
import { PermissionState } from '../../components/PermissionState';
import type { CapabilityDecision } from '../../capabilities/capabilityTypes';
import { fetchAuthenticationMethods, removeAuthenticationMethod, resetAuthenticationMethods, type AuthenticationMethod, type AuthenticationMethodsResponse } from './authenticationMethodsApi';
import type { ApiFetch } from './userDetailApi';
import { TemporaryAccessPassDialog } from './TemporaryAccessPassDialog';

export function AuthenticationMethodsSection({ userId, userLabel, decision, manageDecision }: { userId: string; userLabel?: string; decision: CapabilityDecision; manageDecision?: CapabilityDecision }) {
  const api = useApi();
  const [result, setResult] = useState<AuthenticationMethodsResponse | null>(null);
  const [failed, setFailed] = useState(false);
  const [removeTarget, setRemoveTarget] = useState<AuthenticationMethod | null>(null);
  const [pending, setPending] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);
  const [resetOpen, setResetOpen] = useState(false);
  const [tapOpen, setTapOpen] = useState(false);

  useEffect(() => {
    if (decision.state !== 'allowed' && decision.state !== 'read_only') {
      setResult(null);
      setFailed(false);
      return;
    }

    let cancelled = false;
    fetchAuthenticationMethods(api as ApiFetch, userId)
      .then((response) => { if (!cancelled) setResult(response); })
      .catch(() => { if (!cancelled) setFailed(true); });
    return () => { cancelled = true; };
  }, [api, decision.state, userId]);

  const remove = async () => {
    if (!removeTarget) return;
    setPending(true);
    setActionError(null);
    try {
      await removeAuthenticationMethod(api as ApiFetch, userId, removeTarget);
      setRemoveTarget(null);
      setResult(await fetchAuthenticationMethods(api as ApiFetch, userId));
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
      await resetAuthenticationMethods(api as ApiFetch, userId);
      setResetOpen(false);
      setResult(await fetchAuthenticationMethods(api as ApiFetch, userId));
    } catch (error) {
      setActionError(error instanceof Error ? error.message : messages.userAuthenticationMethodsActionFailed);
    } finally {
      setPending(false);
    }
  };

  const hasRemovableMethods = Boolean(result?.items.some((method) => method.type !== 'passwordAuthenticationMethod'));

  if (decision.state !== 'allowed' && decision.state !== 'read_only') {
    return (
      <section className="detail-section detail-section--wide" aria-labelledby="authentication-methods-section-title">
        <div className="detail-section__header">
          <h2 id="authentication-methods-section-title">{messages.userAuthenticationMethodsSection}</h2>
        </div>
        <PermissionState decision={decision}>
          <p className="section-help">{messages.userAuthenticationMethodsDescription}</p>
        </PermissionState>
      </section>
    );
  }

  return (
    <section className="detail-section detail-section--wide" aria-labelledby="authentication-methods-section-title">
      <div className="detail-section__header">
        <h2 id="authentication-methods-section-title">{messages.userAuthenticationMethodsSection}</h2>
        {result && <DataFreshness fetchedAt={result.fetchedAt} freshness={result.freshness === 'live' ? 'fresh' : result.freshness === 'stale' ? 'stale' : 'unavailable'} partialData={result.partialData} message={result.error?.message} />}
        {manageDecision?.state === 'allowed' && <div className="section-actions"><button type="button" className="button button--primary" onClick={() => setTapOpen(true)}>Grant Temporary Access Pass</button>{hasRemovableMethods && <button type="button" className="button button--danger" onClick={() => setResetOpen(true)}>{messages.userAuthenticationMethodsReset}</button>}</div>}
      </div>
      <p className="section-help">{messages.userAuthenticationMethodsDescription}</p>
      {failed && <p role="alert">{messages.userAuthenticationMethodsUnavailable}</p>}
      {!failed && !result && <p role="status">{messages.userAuthenticationMethodsLoading}</p>}
      {result?.error && <p role="alert">{result.error.message}</p>}
      {result && !result.error && result.items.length === 0 && <p>{messages.userAuthenticationMethodsNone}</p>}
      {actionError && <p role="alert" className="action-feedback action-feedback--error">{actionError}</p>}
      {result && !result.error && result.items.length > 0 && <div className="detail-table-wrap"><table className="detail-table"><caption className="sr-only">Authentication methods</caption><thead><tr><th scope="col">Method</th><th scope="col">Type</th><th scope="col">Registered</th><th scope="col"><span className="sr-only">Actions</span></th></tr></thead><tbody>{result.items.map((method) => <tr key={method.id}><th scope="row" data-label="Method">{method.displayName}</th><td data-label="Type"><code>{method.type}</code>{method.model && <span className="table-subtext">{method.model}</span>}</td><td data-label="Registered">{method.createdDateTime ? new Date(method.createdDateTime).toLocaleDateString() : messages.usersUnavailableValue}</td><td data-label="Actions" className="detail-table__actions">{manageDecision?.state === 'allowed' && method.type !== 'passwordAuthenticationMethod' && <button type="button" className="table-action table-action--danger" onClick={() => setRemoveTarget(method)}>{messages.userAuthenticationMethodsRemove}</button>}</td></tr>)}</tbody></table></div>}
      {decision.state !== 'allowed' && <p className="section-help">{messages.userAuthenticationMethodsReadOnly}</p>}
      {removeTarget && <ConfirmationDialog title={messages.userAuthenticationMethodsRemoveTitle} target={removeTarget.displayName} proposedChange={messages.userAuthenticationMethodsRemoveDescription} requiredCapability="authentication.methods.manage" confirmLabel={messages.confirmRemoveMethod} consequence={messages.confirmRemoveMethodConsequence} tone="danger" busy={pending} onConfirm={() => void remove()} onCancel={() => { if (!pending) setRemoveTarget(null); }} />}
      {resetOpen && <ConfirmationDialog title={messages.userAuthenticationMethodsResetTitle} target={userLabel || userId} proposedChange={messages.userAuthenticationMethodsResetDescription} requiredCapability="authentication.methods.manage" destructivePhrase="RESET MFA" confirmLabel={messages.confirmResetMfa} consequence={messages.confirmResetMfaConsequence} tone="danger" busy={pending} onConfirm={() => void resetMfa()} onCancel={() => { if (!pending) setResetOpen(false); }} />}
      {tapOpen && <TemporaryAccessPassDialog userId={userId} target={userLabel || userId} onClose={() => setTapOpen(false)} />}
    </section>
  );
}
