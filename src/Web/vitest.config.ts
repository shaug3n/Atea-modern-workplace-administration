import { defineConfig } from 'vitest/config';
import { fileURLToPath, URL } from 'node:url';

export default defineConfig({
  define: {
    'import.meta.env.VITE_ENTRA_API_SCOPE': JSON.stringify('api://test-api/access_as_user'),
    'import.meta.env.VITE_ENTRA_CLIENT_ID': JSON.stringify('test-client-id'),
    'import.meta.env.VITE_ENTRA_AUTHORITY': JSON.stringify('https://login.microsoftonline.com/organizations'),
    'import.meta.env.VITE_ENTRA_REDIRECT_URI': JSON.stringify('http://localhost:5173/auth/callback'),
    'import.meta.env.VITE_PLATFORM_ADMIN_CLIENT_ID': JSON.stringify('platform-admin-client'),
    'import.meta.env.VITE_PLATFORM_ADMIN_AUTHORITY': JSON.stringify('https://login.microsoftonline.com/atea-tenant'),
    'import.meta.env.VITE_PLATFORM_ADMIN_SCOPE': JSON.stringify('api://platform-api/access_as_user'),
    'import.meta.env.VITE_PLATFORM_ADMIN_REDIRECT_URI': JSON.stringify('http://localhost:5173/admin/auth/callback')
  },
  server: { fs: { allow: ['../..'] } },
  resolve: {
    alias: {
      '@testing-library/react': fileURLToPath(new URL('./node_modules/@testing-library/react', import.meta.url)),
      '@azure/msal-browser': fileURLToPath(new URL('./node_modules/@azure/msal-browser', import.meta.url)),
      '@azure/msal-react': fileURLToPath(new URL('./node_modules/@azure/msal-react', import.meta.url)),
      react: fileURLToPath(new URL('./node_modules/react', import.meta.url)),
      'react-dom': fileURLToPath(new URL('./node_modules/react-dom', import.meta.url))
    }
  },
  test: {
    environment: 'jsdom',
    include: ['../../tests/Web.UnitTests/**/*.test.tsx']
  }
});
