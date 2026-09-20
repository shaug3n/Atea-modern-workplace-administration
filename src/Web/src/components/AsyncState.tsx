import React, { type ReactNode } from 'react';
import { messages } from '../app/messages';

export function AsyncState({ state, children, empty }: { state: 'loading' | 'error' | 'empty' | 'ready'; children: ReactNode; empty?: ReactNode }) {
  if (state === 'loading') {
    return (
      <div className="async-state async-state--loading" role="status" aria-live="polite">
        <span className="async-state__bar" />
        <span className="async-state__bar" />
        <span className="async-state__bar" />
        <span>{messages.usersLoading}</span>
      </div>
    );
  }

  if (state === 'error') {
    return <div className="async-state" role="alert">{messages.usersUnavailable}</div>;
  }

  if (state === 'empty') {
    return <div className="async-state">{empty ?? messages.usersNoResults}</div>;
  }

  return <>{children}</>;
}
