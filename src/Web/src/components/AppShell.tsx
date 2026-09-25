import React, { type ReactNode, useEffect, useRef, useState } from 'react';
import type { CapabilitySnapshot } from '../capabilities/capabilityTypes';
import { messages } from '../app/messages';
import { PrimaryNav } from './PrimaryNav';
import { TenantContextHeader, type AppSession } from './TenantContextHeader';
import { ThemeToggle, useTheme } from './ThemeToggle';
import greyLogo from '../assets/logos/atea-logo-grey.svg';
import whiteLogo from '../assets/logos/atea-logo-white.svg';

export function AppShell({ children, capabilities, currentPath, session, onNavigate }: { children: ReactNode; capabilities: CapabilitySnapshot | null; currentPath: string; session: AppSession; onNavigate?: (path: string) => void }) {
  const [mobileNavOpen, setMobileNavOpen] = useState(false);
  const menuButton = useRef<HTMLButtonElement>(null);
  useEffect(() => { setMobileNavOpen(false); }, [currentPath]);
  useEffect(() => {
    if (!mobileNavOpen) return;
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') { setMobileNavOpen(false); menuButton.current?.focus(); }
    };
    document.addEventListener('keydown', closeOnEscape);
    return () => document.removeEventListener('keydown', closeOnEscape);
  }, [mobileNavOpen]);
  const { theme } = useTheme();
  const logo = theme === 'dark' ? whiteLogo : greyLogo;
  const labels: Record<string, string> = { overview: 'Overview', users: 'Users', licenses: 'Licenses', devices: 'Devices', services: 'Services', exchange: 'Exchange', activity: 'Activity', settings: 'Workspace Settings', setup: 'Setup', general: 'General', modules: 'Modules', access: 'Access' };
  const segments = currentPath.split('/').filter(Boolean);
  const breadcrumbs = segments.map((segment, index) => ({ label: labels[segment] ?? (index > 0 && segments[0] === 'users' ? 'Details' : segment), href: `/${segments.slice(0, index + 1).join('/')}` }));
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
        <button ref={menuButton} type="button" className="mobile-menu-toggle" aria-expanded={mobileNavOpen} aria-controls="primary-navigation" onClick={() => setMobileNavOpen(open => !open)}>{mobileNavOpen ? 'Close menu' : 'Menu'}</button>
      </header>
      <div className="app-body">
        <div id="primary-navigation" className={mobileNavOpen ? 'mobile-nav-container is-open' : 'mobile-nav-container'}>
          <PrimaryNav capabilities={capabilities} session={session} currentPath={currentPath} onNavigate={path => { setMobileNavOpen(false); onNavigate?.(path); }} />
        </div>
        <main id="main-content" className="app-main" tabIndex={-1}>
          {breadcrumbs.length > 1 && <nav className="breadcrumbs" aria-label="Breadcrumbs"><ol>{breadcrumbs.map((item, index) => <li key={item.href}>{index === breadcrumbs.length - 1 ? <span aria-current="page">{item.label}</span> : item.href === '/users' ? <a href={item.href} onClick={event => { if (onNavigate) { event.preventDefault(); onNavigate(item.href); } }}>{item.label}</a> : <span>{item.label}</span>}</li>)}</ol></nav>}
          {children}
        </main>
      </div>
    </div>
  );
}
