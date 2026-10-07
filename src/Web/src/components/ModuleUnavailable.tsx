import React from 'react';
import { WorkspacePageHeader } from './WorkspacePageHeader';
import { WorkspaceDataState } from './WorkspaceDataState';

export type ModuleUnavailableProps = {
  kind: 'module-off' | 'no-access';
  moduleName: string;
  canManageModules: boolean;
  onNavigate: (path: string) => void;
  message?: string;
};

export function ModuleUnavailable({ kind, moduleName, canManageModules, onNavigate, message }: ModuleUnavailableProps) {
  const off = kind === 'module-off';
  const go = (path: string) => (event: React.MouseEvent) => { event.preventDefault(); onNavigate(path); };
  return (
    <section className="module-unavailable">
      <WorkspacePageHeader title={moduleName} />
      <WorkspaceDataState
        kind="permission"
        title={off ? `${moduleName} is turned off for this workspace` : `You don't have access to ${moduleName}`}
        message={message ?? (off && !canManageModules ? 'Ask a workspace owner to turn it on.' : off ? 'Turn it on in module settings to use it.' : 'Ask a workspace owner if you need access.')}
      />
      <div className="module-unavailable__actions">
        {off && canManageModules && <a className="button button--primary" href="/settings#modules" onClick={go('/settings#modules')}>Open module settings</a>}
        <a className="button button--secondary" href="/overview" onClick={go('/overview')}>Back to overview</a>
      </div>
    </section>
  );
}
