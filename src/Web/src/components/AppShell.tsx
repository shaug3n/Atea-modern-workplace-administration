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
import { AccessTransparencyProvider } from '../features/my-access/accessContext';
import { AccountAccessMenu } from '../features/my-access/AccountAccessMenu';
import { readWebBuildMetadata } from '../features/about/buildMetadata';

const webBuild = readWebBuildMetadata(import.meta.env);

export function AppShell({ children, capabilities, currentPath, session, onNavigate, accessState = { loading: false, error: false, refresh: async () => {} }, canViewAbout = false, canSubmitFeedback = false, onOpenFeedbackDialog }: { children: ReactNode; capabilities: CapabilitySnapshot | null; currentPath: string; session: AppSession; onNavigate?: (path: string) => void; accessState?: { loading: boolean; error: boolean; refresh: () => Promise<void> }; canViewAbout?: boolean; canSubmitFeedback?: boolean; onOpenFeedbackDialog?: () => void }) {
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
  useEffect(() => {
    if (!canSubmitFeedback || !onOpenFeedbackDialog) return;
    const openFeedbackShortcut = (event: KeyboardEvent) => {
      if (!event.altKey || !event.shiftKey || event.ctrlKey || event.metaKey
        || event.isComposing || event.keyCode === 229 || event.key === 'Process'
        || event.key.toLowerCase() !== 'f') return;
      const target = event.target;
      if (target instanceof Element && target.closest('input, textarea, select, [contenteditable]:not([contenteditable="false"]), [role="textbox"]')) return;
      event.preventDefault();
      onOpenFeedbackDialog();
    };
    document.addEventListener('keydown', openFeedbackShortcut);
    return () => document.removeEventListener('keydown', openFeedbackShortcut);
  }, [canSubmitFeedback, onOpenFeedbackDialog]);
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
    <AccessTransparencyProvider session={session} value={{ snapshot: capabilities, ...accessState }}>
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
        <div className="app-header__account"><AccountSummary session={session} /><AccountAccessMenu session={session} onNavigate={path => { setMobileNavOpen(false); onNavigate?.(path); }} /></div>
        {canViewAbout && <a className="app-build-chip" href="/about/system-versions" title={`Web build ${webBuild.productVersion}, commit ${webBuild.commit}, branch ${webBuild.branch}`} aria-label={`System versions, version ${webBuild.productVersion}, commit ${webBuild.commit}, branch ${webBuild.branch}`} onClick={event => {
          if (!onNavigate) return;
          event.preventDefault();
          onNavigate('/about/system-versions');
        }}>
          <span className="app-build-chip__version">{webBuild.productVersion}</span>
          <span className="app-build-chip__commit">{webBuild.commit}</span>
          <span className="app-build-chip__branch">{webBuild.branch}</span>
        </a>}
        <button ref={menuButton} type="button" className="mobile-menu-toggle" aria-expanded={mobileNavOpen} aria-controls="primary-navigation" onClick={() => setMobileNavOpen(open => !open)}>{mobileNavOpen ? 'Close menu' : 'Menu'}</button>
      </header>
      <div className="app-body">
        <div id="primary-navigation" className={mobileNavOpen ? 'mobile-nav-container is-open' : 'mobile-nav-container'}>
          <PrimaryNav capabilities={capabilities} session={session} currentPath={currentPath} onNavigate={path => { setMobileNavOpen(false); onNavigate?.(path); }} />
          {mobileNavOpen && <div className="mobile-nav-extras"><ThemeToggle variant="row" /><AccountSummary session={session} /><AccountAccessMenu session={session} onNavigate={path => { setMobileNavOpen(false); onNavigate?.(path); }} /></div>}
        </div>
        <main id="main-content" className="app-main" aria-labelledby="page-title" tabIndex={-1}>
          {breadcrumbs.length > 1 && <nav className="breadcrumbs" aria-label="Breadcrumbs"><ol>{breadcrumbs.map((item, index) => <li key={item.href}>{index === breadcrumbs.length - 1 ? <span aria-current="page">{item.label}</span> : linkable.includes(item.href) ? <a href={item.href} onClick={event => { if (onNavigate) { event.preventDefault(); onNavigate(item.href); } }}>{item.label}</a> : <span>{item.label}</span>}</li>)}</ol></nav>}
          {children}
        </main>
      </div>
      {canSubmitFeedback && onOpenFeedbackDialog && <button className="app-feedback-fab" type="button" aria-keyshortcuts="Alt+Shift+F" onClick={onOpenFeedbackDialog}>{messages.feedbackGiveAction}</button>}
    </div>
    </AccessTransparencyProvider>
  );
}
