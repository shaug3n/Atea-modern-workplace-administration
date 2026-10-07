import React from 'react';
import type { AdminSession } from './adminAuthApi';
import greyLogo from '../../assets/logos/atea-logo-grey.svg';
import { WorkspaceDetailPage } from './WorkspaceDetailPage';
import { WorkspaceListPage } from './WorkspaceListPage';
import type { WorkspaceOnboardingResult } from './adminApi';
import { ThemeToggle } from '../../components/ThemeToggle';

export function AdminShell({ session, onSignOut }: { session: AdminSession; onSignOut: () => void }) {
  const path = window.location.pathname;
  const navigate = (next: string) => { window.history.pushState({}, '', next); window.dispatchEvent(new PopStateEvent('popstate')); };
  const [currentPath, setCurrentPath] = React.useState(path);
  const [firstAdminInvitation, setFirstAdminInvitation] = React.useState<WorkspaceOnboardingResult | null>(null);
  React.useEffect(() => { const handler = () => setCurrentPath(window.location.pathname); window.addEventListener('popstate', handler); return () => window.removeEventListener('popstate', handler); }, []);
  const currentDetail = currentPath.match(/^\/admin\/workspaces\/([^/]+)$/);
  const content = currentDetail ? <WorkspaceDetailPage workspaceId={currentDetail[1]} firstAdminInvitation={firstAdminInvitation?.workspace.id === currentDetail[1] ? firstAdminInvitation : null} /> : currentPath === '/admin' || currentPath === '/admin/' ? <WorkspaceListPage onOpenWorkspace={(id, invite) => { setFirstAdminInvitation(invite ?? null); navigate(`/admin/workspaces/${id}`); }} /> : <section className="admin-card"><h1>Admin page not found</h1><p>Choose a workspace administration page from the admin home.</p><a href="/admin">Return to admin home</a></section>;
  return <div className="admin-app">
    <header className="admin-header">
      <a href="/admin" className="brand-link" aria-label="Atea platform administration home"><img src={greyLogo} alt="Atea" className="brand-logo" /> <span>Platform administration</span></a>
      <div className="admin-user"><span>{session.displayName}</span><ThemeToggle /><button type="button" onClick={onSignOut}>Sign out</button></div>
    </header>
    <main className="admin-content"><p className="admin-brand">Atea platform administration</p>{content}</main>
  </div>;
}
