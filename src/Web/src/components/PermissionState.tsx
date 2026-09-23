import React, { cloneElement, isValidElement, useState, type ReactElement, type ReactNode } from 'react';
import type { CapabilityDecision } from '../capabilities/capabilityTypes';
import { useApi } from '../auth/useApi';
import { messages } from '../app/messages';

const explanations: Record<string, string> = {
  read_only: 'This action is read-only for your current Entra role.',
  disabled: 'This action is disabled for the current workspace.',
  consent_required: 'Delegated Microsoft Graph consent is required before this action can run.',
  pim_activation_required: 'Activate the required Entra role in PIM before continuing.',
  pim_approval_required: 'This action is waiting for PIM approval.',
  pim_mfa_required: 'Complete MFA for PIM activation before continuing.',
  pim_eligibility_expired: 'Your PIM eligibility has expired. Request renewed access.',
  temporarily_unavailable: 'Microsoft Graph authorization could not be verified. Try again later.',
};

export function PermissionState({ decision, children }: { decision: CapabilityDecision; children: ReactNode }) {
  if (decision.state === 'hidden') {
    return null;
  }

  if (decision.state === 'allowed') {
    return <>{children}</>;
  }

  const content = disableInteractiveChildren(children);
  const interactiveConsent = decision.nextStep?.href === '/api/workspaces/current/consent/start';
  const scopeStatus = decision.reasonCode === 'scope_probe_unavailable'
    ? messages.permissionScopeCheckUnavailable
    : decision.missingScopes?.length
      ? <> {messages.permissionMissingScopes} <code>{decision.missingScopes.join(', ')}</code></>
      : null;

  return (
    <span data-capability={decision.capability} data-capability-state={decision.state}>
      {content}
      <span role="status">{explanations[decision.state] ?? 'This action is not available.'}{scopeStatus}</span>
      {decision.nextStep?.href && (interactiveConsent
        ? <InteractiveConsentNextStep label={decision.nextStep.label} />
        : <a href={decision.nextStep.href}>{decision.nextStep.label}</a>)}
    </span>
  );
}

function InteractiveConsentNextStep({ label }: { label: string }) {
  const api = useApi();
  const [consentUrl, setConsentUrl] = useState<string | null>(null);
  const [consentError, setConsentError] = useState(false);

  const startConsent = async () => {
    setConsentError(false);
    try {
      const response = await api('/api/workspaces/current/consent/start', { method: 'POST' });
      if (!response.ok) throw new Error('consent_start_failed');
      const result = await response.json() as { authorizationUrl?: string };
      if (!result.authorizationUrl) throw new Error('consent_url_missing');
      setConsentUrl(result.authorizationUrl);
    } catch {
      setConsentError(true);
    }
  };

  return <>
    <button type="button" onClick={() => void startConsent()} disabled={Boolean(consentUrl)}>{label}</button>
    {consentUrl && <a href={consentUrl}>Continue consent</a>}
    {consentError && <span role="alert">Consent could not be started. Try again from the workspace overview.</span>}
  </>;
}

function disableInteractiveChildren(children: ReactNode): ReactNode {
  return React.Children.map(children, (child) => {
    if (!isValidElement(child)) {
      return child;
    }

    const element = child as ReactElement<{ disabled?: boolean; 'aria-disabled'?: boolean; children?: ReactNode }>;
    const type = typeof element.type === 'string' ? element.type : '';
    const disabledProps = type === 'button' || type === 'input' || type === 'select' || type === 'textarea'
      ? { disabled: true }
      : { 'aria-disabled': true };

    return cloneElement(element, {
      ...disabledProps,
      children: element.props.children ? disableInteractiveChildren(element.props.children) : element.props.children,
    });
  });
}
