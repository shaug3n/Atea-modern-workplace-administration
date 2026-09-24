import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { WorkspaceAccessPage } from '../../src/Web/src/features/workspace-access/WorkspaceAccessPage';

const apiFetch = vi.hoisted(() => vi.fn());
vi.mock('../../src/Web/src/auth/useApi', () => ({ useApi: () => apiFetch }));

afterEach(() => { cleanup(); apiFetch.mockReset(); });

const access = {
  memberships: [{ id: 'member-1', tenantObjectId: 'oid-1', email: 'member@example.com', platformRole: 'member', createdAt: '2026-09-24T10:00:00Z' }],
  invitations: [{ id: 'invite-1', email: 'pending@example.com', displayName: 'Pending User', role: 'member', expiresAt: '2026-10-01T10:00:00Z', redeemedAt: null, revokedAt: null }],
};

describe('workspace access management', () => {
  it('invites an existing Entra identity and reveals the one-time invitation link', async () => {
    const requests: Array<{ path: string; method: string; body?: unknown }> = [];
    apiFetch.mockImplementation(async (path: string, init: RequestInit = {}) => {
      requests.push({ path, method: init.method ?? 'GET', body: init.body ? JSON.parse(String(init.body)) : undefined });
      if (path === '/api/workspaces/current/access') return Response.json(access);
      if (path === '/api/workspaces/current/access/invitations') return Response.json({ id: 'new-invite', invitationUrl: 'http://localhost:5173/invitations/one-time-secret', expiresAt: '2026-10-01T10:00:00Z' }, { status: 201 });
      return new Response(null, { status: 204 });
    });
    render(<WorkspaceAccessPage />);

    fireEvent.change(await screen.findByLabelText('Email address'), { target: { value: 'new.user@example.com' } });
    fireEvent.change(screen.getByLabelText('Display name'), { target: { value: 'New User' } });
    fireEvent.change(screen.getByLabelText('Workspace role'), { target: { value: 'member' } });
    fireEvent.click(screen.getByRole('button', { name: 'Create invitation' }));

    expect(await screen.findByText('http://localhost:5173/invitations/one-time-secret')).toBeTruthy();
    expect(requests.find(request => request.method === 'POST')?.body).toEqual({ email: 'new.user@example.com', displayName: 'New User', role: 'member' });
  });

  it('changes a workspace role and confirms removal before sending the request', async () => {
    const requests: Array<{ path: string; method: string }> = [];
    apiFetch.mockImplementation(async (path: string, init: RequestInit = {}) => {
      requests.push({ path, method: init.method ?? 'GET' });
      return path === '/api/workspaces/current/access' ? Response.json(access) : new Response(null, { status: 204 });
    });
    render(<WorkspaceAccessPage />);

    fireEvent.change(await screen.findByLabelText('Role for member@example.com'), { target: { value: 'customer_admin' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save role for member@example.com' }));
    await waitFor(() => expect(requests.some(request => request.path === '/api/workspaces/current/access/memberships/member-1' && request.method === 'PATCH')).toBe(true));

    fireEvent.click(screen.getByRole('button', { name: 'Remove access for member@example.com' }));
    expect(screen.getByRole('dialog')).toBeTruthy();
    const cancel = screen.getByRole('button', { name: 'Cancel' });
    expect(document.activeElement).toBe(cancel);
    fireEvent.keyDown(cancel, { key: 'Escape' });
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Remove access for member@example.com' }));
    fireEvent.click(screen.getByRole('button', { name: 'Remove access for member@example.com' }));
    const confirm = screen.getByRole('button', { name: 'Confirm remove access' });
    confirm.focus();
    fireEvent.keyDown(confirm, { key: 'Tab' });
    const cancelAfterWrap = screen.getByRole('button', { name: 'Cancel' });
    expect(document.activeElement).toBe(cancelAfterWrap);
    fireEvent.keyDown(cancelAfterWrap, { key: 'Tab', shiftKey: true });
    expect(document.activeElement).toBe(confirm);
    fireEvent.click(screen.getByRole('button', { name: 'Confirm remove access' }));
    await waitFor(() => expect(requests.some(request => request.path === '/api/workspaces/current/access/memberships/member-1' && request.method === 'DELETE')).toBe(true));
  });

  it('reissues or revokes only pending invitations', async () => {
    const requests: Array<{ path: string; method: string }> = [];
    apiFetch.mockImplementation(async (path: string, init: RequestInit = {}) => {
      requests.push({ path, method: init.method ?? 'GET' });
      if (path === '/api/workspaces/current/access') return Response.json(access);
      if (init.method === 'POST') return Response.json({ id: 'invite-2', invitationUrl: 'http://localhost:5173/invitations/reissued', expiresAt: '2026-10-01T10:00:00Z' });
      return new Response(null, { status: 204 });
    });
    render(<WorkspaceAccessPage />);

    fireEvent.click(await screen.findByRole('button', { name: 'Reissue invitation for pending@example.com' }));
    expect(await screen.findByText('http://localhost:5173/invitations/reissued')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Revoke invitation for pending@example.com' }));
    await waitFor(() => expect(requests.some(request => request.path === '/api/workspaces/current/access/invitations/invite-1' && request.method === 'DELETE')).toBe(true));
  });
});
