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
    authApi.getSession.mockResolvedValue({ username: 'admin@example.test' });

    render(<AdminApp />);

    expect(await screen.findByRole('heading', { name: 'Atea platform administration' })).toBeTruthy();
    expect(screen.getByText('admin@example.test')).toBeTruthy();
  });

  it('treats a 401 session response as signed out', async () => {
    authApi.getSession.mockResolvedValue(null);

    render(<AdminApp />);

    await waitFor(() => expect(screen.getByRole('heading', { name: 'Admin sign in' })).toBeTruthy());
  });
});
