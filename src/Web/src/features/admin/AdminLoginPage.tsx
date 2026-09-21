import React, { useState } from 'react';
import type { AdminSession } from './adminAuthApi';
import greyLogo from '../../assets/logos/atea-logo-grey.svg';

export function AdminLoginPage({ onLogin }: { onLogin: (username: string, password: string) => Promise<AdminSession> }) {
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState(false);

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    setSubmitting(true);
    setError(false);
    try { await onLogin(username, password); } catch { setError(true); } finally { setSubmitting(false); }
  };

  return <main className="admin-login" aria-labelledby="admin-login-title">
    <div className="admin-login__card">
      <img src={greyLogo} alt="Atea" className="admin-login__logo" />
      <p className="eyebrow">Atea Unified Workplace</p>
      <h1 id="admin-login-title">Admin sign in</h1>
      <p>Sign in to manage the local platform administration console.</p>
      <p className="admin-login__notice">For local development only.</p>
      <form onSubmit={submit}>
        <label htmlFor="admin-username">Username</label>
        <input id="admin-username" value={username} onChange={(event) => setUsername(event.target.value)} autoComplete="username" required />
        <label htmlFor="admin-password">Password</label>
        <input id="admin-password" type="password" value={password} onChange={(event) => setPassword(event.target.value)} autoComplete="current-password" required />
        {error && <p role="alert">Invalid username or password.</p>}
        <button type="submit" disabled={submitting}>{submitting ? 'Signing in…' : 'Sign in'}</button>
      </form>
    </div>
  </main>;
}
