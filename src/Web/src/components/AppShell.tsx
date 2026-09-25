import React, { type ReactNode, useState } from 'react';
import type { CapabilitySnapshot } from '../capabilities/capabilityTypes';
import { messages } from '../app/messages';
import { PrimaryNav } from './PrimaryNav';
import { TenantContextHeader, type AppSession } from './TenantContextHeader';
import { ThemeToggle, useTheme } from './ThemeToggle';
import greyLogo from '../assets/logos/atea-logo-grey.svg';
import whiteLogo from '../assets/logos/atea-logo-white.svg';

export function AppShell({ children, capabilities, currentPath, session, onNavigate }: { children: ReactNode; capabilities: CapabilitySnapshot | null; currentPath: string; session: AppSession; onNavigate?: (path: string) => void }) {
  const [mobileNavOpen, setMobileNavOpen] = useState(false);
  const { theme } = useTheme();
  const logo = theme === 'dark' ? whiteLogo : greyLogo;
  return (
    <div className="atea-app">
      <a className="skip-link" href="#main-content">{messages.skipToContent}</a>
      <header className="app-header">
        <a className="brand-link" href="/overview" aria-label={messages.homeLinkLabel} onClick={(event) => {
          if (!onNavigate) {
            return;
          }
          event.preventDefault();
          onNavigate('/overview');
        }}>
          <img className="brand-logo" src={logo} alt="" data-logo-asset={theme === 'dark' ? 'atea-logo-white.svg' : 'atea-logo-grey.svg'} />
          <span>{messages.appTitle}</span>
        </a>
        <TenantContextHeader session={session} capabilitySnapshotFresh={capabilities?.sourceState === 'graph_authoritative'} />
        <ThemeToggle />
        <button type="button" className="mobile-menu-toggle" aria-expanded={mobileNavOpen} aria-controls="primary-navigation" onClick={() => setMobileNavOpen(open => !open)}>{mobileNavOpen ? 'Close menu' : 'Menu'}</button>
      </header>
      <div className="app-body">
        <div id="primary-navigation" className={mobileNavOpen ? 'mobile-nav-container is-open' : 'mobile-nav-container'}>
          <PrimaryNav capabilities={capabilities} session={session} currentPath={currentPath} onNavigate={path => { setMobileNavOpen(false); onNavigate?.(path); }} />
        </div>
        <main id="main-content" className="app-main" tabIndex={-1}>
          {children}
        </main>
      </div>
    </div>
  );
}
