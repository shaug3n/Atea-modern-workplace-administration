import { useEffect, useRef, useState } from 'react';
import greyLogo from '../../assets/logos/atea-logo-grey.svg';
import { useAuth } from '../../auth/AuthProvider';
import { messages } from '../../messages/en';
import {
  fetchInvitationPreview,
  startInvitationConsent,
  type InvitationPreview,
} from './invitationApi';
import { readPendingFlow, writePendingFlow } from './pendingFlow';
import { InvitationRedemptionPage } from './InvitationRedemptionPage';

const recoveryMessage = 'Your invitation is no longer available. Ask Atea for a new invitation.';

export function InvitationLandingPage({
  nonce,
  redirectToConsent = (url) => window.location.assign(url),
}: {
  nonce: string;
  redirectToConsent?: (url: string) => void;
}) {
  const { account, signIn, signInForTenant } = useAuth();
  const [preview, setPreview] = useState<InvitationPreview | null>(null);
  const [loading, setLoading] = useState(true);
  const [starting, setStarting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const headingRef = useRef<HTMLHeadingElement>(null);
  const pendingFlow = readPendingFlow();
  const consentFlow = pendingFlow?.kind === 'invitation' && pendingFlow.nonce === nonce &&
    pendingFlow.tenantId && ['tenant_sign_in_started', 'redeeming', 'completion_pending', 'completion_submitted', 'result'].includes(pendingFlow.step)
    ? pendingFlow
    : null;

  useEffect(() => {
    let active = true;
    setLoading(true);
    setPreview(null);
    setError(null);
    fetchInvitationPreview(nonce).then(value => {
      if (active) setPreview(value);
    }).catch(reason => {
      if (!active) return;
      setPreview(null);
      setError(reason instanceof Error && reason.message === 'invitation_unavailable'
        ? recoveryMessage
        : messages.invitationPreviewUnavailable);
    }).finally(() => {
      if (active) setLoading(false);
    });
    return () => { active = false; };
  }, [nonce]);

  useEffect(() => {
    if (!loading && (preview || error)) headingRef.current?.focus();
  }, [loading, preview, error]);

  if (preview?.flow === 'sign_in' && account) {
    return <InvitationRedemptionPage nonce={nonce} />;
  }
  if (consentFlow &&
      account?.tenantId?.toLowerCase() === consentFlow.tenantId?.toLowerCase()) {
    return <InvitationRedemptionPage nonce={nonce} challenge={consentFlow.challenge} tenantId={consentFlow.tenantId} />;
  }

  async function beginConsent() {
    if (starting) return;
    setStarting(true);
    setError(null);
    try {
      const result = await startInvitationConsent(nonce);
      writePendingFlow({
        kind: 'invitation',
        nonce,
        challenge: result.challenge,
        expiresAt: result.expiresAt,
        step: 'consent_callback',
      });
      redirectToConsent(result.authorizationUrl);
    } catch (reason) {
      setError(reason instanceof Error && reason.message === 'invitation_unavailable'
        ? recoveryMessage
        : messages.invitationConsentUnavailable);
      headingRef.current?.focus();
    } finally {
      setStarting(false);
    }
  }

  return (
    <main className="invitation-page">
      <header className="invitation-page__header">
        <a className="invitation-page__brand" href="/" aria-label={messages.homeLinkLabel}>
          <img src={greyLogo} alt="" />
        </a>
        <span>{messages.appTitle}</span>
      </header>
      <div className="invitation-page__content">
        <section className="invitation-page__card" aria-labelledby="invitation-landing-title">
          {loading ? (
            <p className="invitation-page__loading" role="status">{messages.invitationPreviewLoading}</p>
          ) : error ? (
            <>
              <p className="eyebrow">{messages.invitationEyebrow}</p>
              <h1 id="invitation-landing-title" ref={headingRef} tabIndex={-1}>{error}</h1>
              <p role="alert">{error}</p>
            </>
          ) : preview?.flow === 'sign_in' ? (
            <>
              <p className="eyebrow">{messages.invitationEyebrow}</p>
              <h1 id="invitation-landing-title" ref={headingRef} tabIndex={-1}>{messages.invitationTitle}</h1>
              <p>{messages.invitationMemberDescription}</p>
              <button className="invitation-page__primary" type="button" onClick={() => void signIn()}>
                {messages.invitationSignInAction}
              </button>
            </>
          ) : preview ? (
            <>
              <p className="eyebrow">{messages.invitationEyebrow}</p>
              <h1 id="invitation-landing-title" ref={headingRef} tabIndex={-1}>
                {messages.invitationConsentTitle(preview.workspaceName)}
              </h1>
              <p>{messages.invitationConsentExplanation}</p>
              <p className="invitation-page__guidance">{messages.invitationConsentRoleGuidance}</p>
              <p className="invitation-page__boundary">{messages.invitationConsentBoundary}</p>
              <details className="invitation-page__permissions">
                <summary>{messages.invitationPermissionDetails}</summary>
                <ul>
                  {preview.permissionScopes.map(scope => <li key={scope}>{scope}</li>)}
                </ul>
              </details>
              {consentFlow ? (
                <button
                  className="invitation-page__primary"
                  type="button"
                  disabled={starting}
                  onClick={() => {
                    setStarting(true);
                    void signInForTenant(consentFlow.tenantId!, `/invitations/${nonce}`)
                      .catch(() => setError(messages.authSignInError))
                      .finally(() => setStarting(false));
                  }}
                >
                  {starting ? messages.authPreparing : messages.invitationSignInAction}
                </button>
              ) : (
                <button
                  className="invitation-page__primary"
                  type="button"
                  onClick={() => void beginConsent()}
                  disabled={starting}
                  aria-busy={starting}
                >
                  {starting ? messages.invitationConsentStarting : messages.invitationConsentAction}
                </button>
              )}
              {error && <p role="alert" tabIndex={-1}>{error}</p>}
            </>
          ) : null}
        </section>
      </div>
    </main>
  );
}
