import { cleanup, render, screen, waitFor } from '@testing-library/react';
import React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AdminApp } from '../../../../src/Web/src/features/admin/AdminApp';
import { adminAuthApi } from '../../../../src/Web/src/features/admin/adminAuthApi';

vi.mock('../../../../src/Web/src/features/admin/adminAuthApi', () => ({
  adminAuthApi: { getSession: vi.fn(), login: vi.fn(), logout: vi.fn() },
}));

const authApi = vi.mocked(adminAuthApi);

describe('AdminApp', () => {
  beforeEach(() => vi.clearAllMocks());
  afterEach(() => cleanup());

  it('renders the local admin login when no cookie session exists', async () => {
    authApi.getSession.mockResolvedValue(null);

    render(<AdminApp />);

    expect(await screen.findByRole('heading', { name: 'Admin sign in' })).toBeTruthy();
    expect(screen.getByText('For local development only.')).toBeTruthy();
  });

  it('renders the protected admin shell for a valid cookie session', async () => {
    authApi.getSession.mockResolvedValue({ displayName: 'Admin User' });

    render(<AdminApp />);

    expect(await screen.findByRole('heading', { name: 'Atea platform administration' })).toBeTruthy();
    expect(screen.getByText('Admin User')).toBeTruthy();
  });

  it('treats a 401 session response as signed out', async () => {
    authApi.getSession.mockResolvedValue(null);

    render(<AdminApp />);

    await waitFor(() => expect(screen.getByRole('heading', { name: 'Admin sign in' })).toBeTruthy());
  });

  it('renders an explicit not-found page for unknown admin routes', async () => {
    window.history.pushState({}, '', '/admin/unknown');
    authApi.getSession.mockResolvedValue({ displayName: 'Admin User' });
    render(<AdminApp />);
    expect(await screen.findByRole('heading', { name: 'Admin page not found' })).toBeTruthy();
    expect(screen.getByText('Choose a workspace administration page from the admin home.')).toBeTruthy();
    window.history.pushState({}, '', '/admin');
  });
});
