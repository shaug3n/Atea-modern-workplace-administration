import React, { useEffect, useState } from 'react';
import { ThemeProvider } from '../../components/ThemeToggle';
import { adminAuthApi, type AdminSession } from './adminAuthApi';
import { AdminLoginPage } from './AdminLoginPage';
import { AdminShell } from './AdminShell';

export function isAdminPath(pathname: string) {
  return pathname === '/admin' || pathname.startsWith('/admin/');
}

export function AdminApp() {
  const [session, setSession] = useState<AdminSession | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    adminAuthApi.getSession().then(setSession).catch(() => setSession(null)).finally(() => setLoading(false));
  }, []);

  const login = async (username: string, password: string) => {
    const nextSession = await adminAuthApi.login(username, password);
    setSession(nextSession);
    return nextSession;
  };

  const signOut = async () => {
    await adminAuthApi.logout();
    window.location.assign('/');
  };

  return <ThemeProvider>{loading ? <main role="status">Loading admin sign in…</main> : session ? <AdminShell session={session} onSignOut={() => void signOut()} /> : <AdminLoginPage onLogin={login} />}</ThemeProvider>;
}
