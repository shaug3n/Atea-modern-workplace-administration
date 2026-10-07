import { lazy, StrictMode, Suspense } from 'react';
import { createRoot } from 'react-dom/client';
import './styles/theme.css';
import './styles/foundations.css';
import './styles/components.css';
import { isInvitationPath } from './app/routes';

const AdminApp = lazy(async () => {
  const { AdminApp: App } = await import('./features/admin/AdminApp');
  return { default: App };
});
const isAdminPath = (pathname: string) => pathname === '/admin' || pathname.startsWith('/admin/');

const CustomerApp = lazy(async () => {
  const [{ App }, { AuthProvider }] = await Promise.all([import('./app/App'), import('./auth/AuthProvider')]);
  return { default: () => <AuthProvider><App /></AuthProvider> };
});

const root = document.getElementById('root')!;
const adminAuthMode = import.meta.env.VITE_ADMIN_AUTH_MODE === 'hosted' || !import.meta.env.DEV ? 'hosted' : 'local';
createRoot(root).render(<StrictMode>{isAdminPath(window.location.pathname) ? <Suspense fallback={<main role="status">Loading administration…</main>}><AdminApp authMode={adminAuthMode} /></Suspense> : isInvitationPath(window.location.pathname) ? <Suspense fallback={<main role="status">Loading invitation…</main>}><CustomerApp /></Suspense> : <Suspense fallback={<main role="status">Loading workspace…</main>}><CustomerApp /></Suspense>}</StrictMode>);
