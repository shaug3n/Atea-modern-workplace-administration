import React, { cloneElement, isValidElement, type ReactElement, type ReactNode } from 'react';
import type { CapabilityDecision } from '../capabilities/capabilityTypes';

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
  return (
    <span data-capability={decision.capability} data-capability-state={decision.state}>
      {content}
      <span role="status">{explanations[decision.state] ?? 'This action is not available.'}</span>
      {decision.nextStep?.href && <a href={decision.nextStep.href}>{decision.nextStep.label}</a>}
    </span>
  );
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
