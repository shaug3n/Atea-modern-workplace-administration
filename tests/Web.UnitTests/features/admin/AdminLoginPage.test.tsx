import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AdminLoginPage } from '../../../../src/Web/src/features/admin/AdminLoginPage';

describe('AdminLoginPage', () => {
  beforeEach(() => vi.clearAllMocks());
  afterEach(() => cleanup());

  it('submits labelled credentials and disables the button while signing in', async () => {
    let resolveLogin!: (value: { username: string }) => void;
    const onLogin = vi.fn(() => new Promise<{ username: string }>((resolve) => { resolveLogin = resolve; }));
    render(<AdminLoginPage onLogin={onLogin} />);

    fireEvent.change(screen.getByLabelText('Username'), { target: { value: 'admin' } });
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'secret' } });
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(onLogin).toHaveBeenCalledWith('admin', 'secret');
    expect(screen.getByRole('button', { name: 'Signing in…' })).toHaveProperty('disabled', true);
    resolveLogin({ username: 'admin' });
    await waitFor(() => expect(screen.getByRole('button', { name: 'Sign in' })).toHaveProperty('disabled', false));
  });

  it('shows generic invalid-login copy when authentication fails', async () => {
    const onLogin = vi.fn().mockRejectedValue(new Error('wrong password'));
    render(<AdminLoginPage onLogin={onLogin} />);

    fireEvent.change(screen.getByLabelText('Username'), { target: { value: 'admin' } });
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'wrong' } });
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));

    expect((await screen.findByRole('alert')).textContent).toBe('Invalid username or password.');
  });
});
