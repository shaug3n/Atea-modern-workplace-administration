import React from 'react';
import type { AdminSession } from './adminAuthApi';
import greyLogo from '../../assets/logos/atea-logo-grey.svg';

export function AdminShell({ session, onSignOut }: { session: AdminSession; onSignOut: () => void }) {
  return <div className="admin-app">
    <header className="admin-header">
      <a href="/admin" className="brand-link" aria-label="Atea platform administration home"><img src={greyLogo} alt="Atea" className="brand-logo" /> <span>Platform administration</span></a>
      <div className="admin-user"><span>{session.displayName}</span><button type="button" onClick={onSignOut}>Sign out</button></div>
    </header>
    <main className="admin-content"><p className="eyebrow">Admin console</p><h1>Atea platform administration</h1><p>Workspace administration tools will appear here in the next MVP task.</p></main>
  </div>;
}
