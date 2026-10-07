import React, { type ReactNode, useEffect, useRef, useState } from 'react';
import type { CapabilitySnapshot } from '../capabilities/capabilityTypes';
import { messages } from '../app/messages';
import { PrimaryNav } from './PrimaryNav';
import { AccountSummary, TenantContextHeader, type AppSession } from './TenantContextHeader';
import { ThemeToggle, useTheme } from './ThemeToggle';
import greyLogo from '../assets/logos/atea-logo-grey.svg';
import whiteLogo from '../assets/logos/atea-logo-white.svg';
import { NotificationsMenu } from './NotificationsMenu';
import { useWorkspaceNotifications } from '../notifications/WorkspaceNotifications';

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
  const [notificationsOpen, setNotificationsOpen] = useState(false);
  const { theme } = useTheme();
  const notifications = useWorkspaceNotifications();
  const accessLimited = capabilities !== null && (capabilities.sourceState !== 'graph_authoritative' || capabilities.capabilities.some(c => c.state === 'consent_required' || c.state.startsWith('pim_')));
  const logo = theme === 'dark' ? whiteLogo : greyLogo;
  const labels: Record<string, string> = { overview: 'Overview', users: 'Users', licenses: 'Licenses', devices: 'Devices', services: 'Services', exchange: 'Exchange', activity: 'Activity', settings: 'Workspace Settings', setup: 'Setup', general: 'General', modules: 'Modules', access: 'Access' };
  const segments = currentPath.split('/').filter(Boolean);
  const guid = /^[0-9a-f]{8}-([0-9a-f]{4}-){3}[0-9a-f]{12}$/i;
  const breadcrumbLabel = (segment: string, index: number) => labels[segment] ?? ((index > 0 && (segments[0] === 'users' || segments[0] === 'devices')) || guid.test(segment) ? 'Details' : segment);
  const linkable = ['/users', '/devices', '/licenses', '/settings'];
  const breadcrumbs = segments.map((segment, index) => ({ label: breadcrumbLabel(segment, index), href: `/${segments.slice(0, index + 1).join('/')}` }));
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
        <TenantContextHeader session={session} accessLimited={accessLimited} onAccessLimitedClick={() => setNotificationsOpen(true)} />
        <div className="app-header__controls">
          <NotificationsMenu issues={notifications.issues} onRefresh={notifications.refresh} open={notificationsOpen} onOpenChange={setNotificationsOpen} access={capabilities ? { label: accessLimited ? 'limited' : 'up to date', checkedAt: capabilities.evaluatedAt } : undefined} />
          {!mobileNavOpen && <ThemeToggle />}
        </div>
        <div className="app-header__account"><AccountSummary session={session} /></div>
        <button ref={menuButton} type="button" className="mobile-menu-toggle" aria-expanded={mobileNavOpen} aria-controls="primary-navigation" onClick={() => setMobileNavOpen(open => !open)}>{mobileNavOpen ? 'Close menu' : 'Menu'}</button>
      </header>
      <div className="app-body">
        <div id="primary-navigation" className={mobileNavOpen ? 'mobile-nav-container is-open' : 'mobile-nav-container'}>
          <PrimaryNav capabilities={capabilities} session={session} currentPath={currentPath} onNavigate={path => { setMobileNavOpen(false); onNavigate?.(path); }} />
          {mobileNavOpen && <div className="mobile-nav-extras"><ThemeToggle variant="row" /><AccountSummary session={session} /></div>}
        </div>
        <main id="main-content" className="app-main" aria-labelledby="page-title" tabIndex={-1}>
          {breadcrumbs.length > 1 && <nav className="breadcrumbs" aria-label="Breadcrumbs"><ol>{breadcrumbs.map((item, index) => <li key={item.href}>{index === breadcrumbs.length - 1 ? <span aria-current="page">{item.label}</span> : linkable.includes(item.href) ? <a href={item.href} onClick={event => { if (onNavigate) { event.preventDefault(); onNavigate(item.href); } }}>{item.label}</a> : <span>{item.label}</span>}</li>)}</ol></nav>}
          {children}
        </main>
      </div>
    </div>
  );
}
