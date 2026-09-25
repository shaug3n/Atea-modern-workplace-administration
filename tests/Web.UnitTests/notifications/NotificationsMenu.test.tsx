import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { NotificationsMenu } from '../../../src/Web/src/components/NotificationsMenu';
import type { WorkspaceIssue } from '../../../src/Web/src/notifications/workspaceIssues';

afterEach(cleanup);
const issues: WorkspaceIssue[] = [
  { key: 'setup', area: 'Connection', kind: 'setup', severity: 'warning', title: 'Consent required', detail: 'Contact your workspace administrator.' },
  { key: 'access', area: 'Your access', kind: 'access', severity: 'info', title: 'Read-only access', detail: 'Some actions require another role.' },
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

  it('refreshes manually without rendering raw API errors', () => {
    const refresh = vi.fn();
    render(<NotificationsMenu issues={issues} onRefresh={refresh} />);
    fireEvent.click(screen.getByRole('button', { name: /notifications/i }));
    fireEvent.click(screen.getByRole('button', { name: /refresh/i }));
    expect(refresh).toHaveBeenCalledOnce();
    expect(screen.queryByText(/secret-token|Graph error/i)).toBeNull();
  });
});
