import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { NotificationsMenu } from '../../../src/Web/src/components/NotificationsMenu';
import type { WorkspaceIssue } from '../../../src/Web/src/notifications/workspaceIssues';
import { WorkspaceNotificationsProvider, useWorkspaceIssueReporter, useWorkspaceNotifications } from '../../../src/Web/src/notifications/WorkspaceNotifications';
import type { AppSession } from '../../../src/Web/src/components/TenantContextHeader';

afterEach(cleanup);
const issues: WorkspaceIssue[] = [
  { key: 'setup', area: 'Connection', kind: 'setup', severity: 'warning', title: 'Consent required', detail: 'Contact your workspace administrator.', audience: 'affected_user', lastCheckedAt: '2026-09-25T10:00:00Z' },
  { key: 'access', area: 'Your access', kind: 'access', severity: 'info', title: 'Read-only access', detail: 'Some actions require another role.', audience: 'affected_user', lastCheckedAt: '2026-09-25T10:00:00Z' },
];

describe('NotificationsMenu', () => {
  it('labels its control and counts only warnings', () => {
    render(<NotificationsMenu issues={issues} onRefresh={vi.fn()} />);
    expect(screen.getByRole('button', { name: /notifications.*1/i })).toBeTruthy();
  });

  it('opens by keyboard and groups safe issue titles', () => {
    render(<NotificationsMenu issues={issues} onRefresh={vi.fn()} />);
    const button = screen.getByRole('button', { name: /notifications/i });
    button.focus();
    fireEvent.keyDown(button, { key: 'Enter' });
    expect(screen.getByRole('heading', { name: 'Connection' })).toBeTruthy();
    expect(screen.getByRole('heading', { name: 'Your access' })).toBeTruthy();
    expect(screen.getByText('Consent required')).toBeTruthy();
    expect(screen.getByText('Read-only access')).toBeTruthy();
  });

  it('returns focus to the trigger on Escape', () => {
    render(<NotificationsMenu issues={issues} onRefresh={vi.fn()} />);
    const button = screen.getByRole('button', { name: /notifications/i });
    fireEvent.click(button);
    fireEvent.keyDown(document, { key: 'Escape' });
    expect(button.getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(button);
  });

  it('refreshes manually', () => {
    const refresh = vi.fn();
    render(<NotificationsMenu issues={issues} onRefresh={refresh} />);
    fireEvent.click(screen.getByRole('button', { name: /notifications/i }));
    fireEvent.click(screen.getByRole('button', { name: /refresh/i }));
    expect(refresh).toHaveBeenCalledOnce();
  });

  it('shows only the sanitized category when a page reports a raw Graph error', async () => {
    const session: AppSession = { user: { displayName: 'Alex' }, workspace: { id: 'workspace-one', name: 'Workspace One' }, workspaceAccess: { role: 'member', canManageMembers: false, canManageSettings: false } };
    function ReportedMenu() {
      const reporter = useWorkspaceIssueReporter();
      const { issues, refresh } = useWorkspaceNotifications();
      React.useEffect(() => { reporter.report({ key: 'devices:graph-failure', area: 'devices', kind: 'service', severity: 'warning', title: 'Graph error: secret-token', detail: 'Bearer secret-token exposed', action: { label: 'Open secret-token', href: 'https://example.invalid/secret-token' } }); }, [reporter]);
      return <NotificationsMenu issues={issues} onRefresh={refresh} />;
    }
    render(<WorkspaceNotificationsProvider session={session} capabilities={null} capabilitiesError={null} onRefresh={async () => {}}><ReportedMenu /></WorkspaceNotificationsProvider>);
    fireEvent.click(await screen.findByRole('button', { name: /notifications, 1 need attention/i }));
    expect(screen.getByText('Data temporarily unavailable')).toBeTruthy();
    expect(document.body.textContent).not.toContain('secret-token');
    expect(screen.queryByRole('link', { name: /secret-token/i })).toBeNull();
  });

  it('hides the badge at zero, shows the empty text and an access-check footer with refresh', () => {
    const refresh = vi.fn();
    const { container } = render(<NotificationsMenu issues={[]} onRefresh={refresh} access={{ label: 'up to date', checkedAt: new Date().toISOString() }} />);
    expect(container.querySelector('.notifications-menu__count')).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: 'Notifications, 0 need attention' }));
    expect(screen.getByText('No issues need your attention.')).toBeTruthy();
    expect(screen.getByText(/Access check: up to date · checked/)).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Refresh' }));
    expect(refresh).toHaveBeenCalledOnce();
  });
});
