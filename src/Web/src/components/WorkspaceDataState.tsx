import React from 'react';

export function WorkspaceDataState({ state, message, onRetry }: { state: 'loading' | 'empty' | 'unavailable'; message: string; onRetry?: () => void }) {
  return <section className={`async-state workspace-data-state workspace-data-state--${state}`} role={state === 'unavailable' ? 'alert' : 'status'}>
    <p>{message}</p>
    {state === 'unavailable' && onRetry && <button type="button" onClick={onRetry}>Retry</button>}
  </section>;
}
