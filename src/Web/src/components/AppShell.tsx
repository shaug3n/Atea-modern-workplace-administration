import React, { type ReactNode } from 'react';
import type { CapabilitySnapshot } from '../capabilities/capabilityTypes';
import { messages } from '../app/messages';
import { PrimaryNav } from './PrimaryNav';
import { TenantContextHeader, type AppSession } from './TenantContextHeader';
import { ThemeToggle, useTheme } from './ThemeToggle';
import greyLogo from '../assets/logos/atea-logo-grey.svg';
import whiteLogo from '../assets/logos/atea-logo-white.svg';

export function AppShell({ children, capabilities, currentPath, session, onNavigate }: { children: ReactNode; capabilities: CapabilitySnapshot; currentPath: string; session: AppSession; onNavigate?: (path: string) => void }) {
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
        <TenantContextHeader session={session} connectionFresh={capabilities.sourceState === 'graph_authoritative'} />
        <ThemeToggle />
      </header>
      <div className="app-body">
        <PrimaryNav capabilities={capabilities} currentPath={currentPath} onNavigate={onNavigate} />
        <main id="main-content" className="app-main" tabIndex={-1}>
          {children}
        </main>
      </div>
    </div>
  );
}
