import { lazy, StrictMode, Suspense } from 'react';
import { createRoot } from 'react-dom/client';
import './styles/theme.css';
import { AdminApp, isAdminPath } from './features/admin/AdminApp';

const CustomerApp = lazy(async () => {
  const [{ App }, { AuthProvider }] = await Promise.all([import('./app/App'), import('./auth/AuthProvider')]);
  return { default: () => <AuthProvider><App /></AuthProvider> };
});

const root = document.getElementById('root')!;
createRoot(root).render(<StrictMode>{isAdminPath(window.location.pathname) ? <AdminApp /> : <Suspense fallback={<main role="status">Loading workspace…</main>}><CustomerApp /></Suspense>}</StrictMode>);
