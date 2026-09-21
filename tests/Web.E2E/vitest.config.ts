import { fileURLToPath, URL } from 'node:url';
import { defineConfig } from '../../src/Web/node_modules/vitest/dist/config.js';

export default defineConfig({
  define: {
    'import.meta.env.VITE_ENTRA_API_SCOPE': JSON.stringify('api://test-api/access_as_user'),
    'import.meta.env.VITE_ENTRA_CLIENT_ID': JSON.stringify('test-client-id'),
    'import.meta.env.VITE_ENTRA_AUTHORITY': JSON.stringify('https://login.microsoftonline.com/organizations'),
    'import.meta.env.VITE_ENTRA_REDIRECT_URI': JSON.stringify('http://localhost:5173/auth/callback')
  },
  resolve: {
    alias: {
      '@testing-library/react': fileURLToPath(new URL('../../src/Web/node_modules/@testing-library/react', import.meta.url)),
      '@azure/msal-browser': fileURLToPath(new URL('../../src/Web/node_modules/@azure/msal-browser', import.meta.url)),
      '@azure/msal-react': fileURLToPath(new URL('../../src/Web/node_modules/@azure/msal-react', import.meta.url)),
      react: fileURLToPath(new URL('../../src/Web/node_modules/react', import.meta.url)),
      'react-dom': fileURLToPath(new URL('../../src/Web/node_modules/react-dom', import.meta.url))
    }
  },
  test: {
    environment: 'jsdom',
    include: ['users-directory.spec.tsx', 'user-detail.spec.tsx', 'user-lifecycle.spec.tsx', 'pim-activation.spec.tsx', 'audit-activity.spec.tsx', 'overview-and-settings.spec.tsx', 'admin-onboarding.spec.tsx', 'consent-callback.spec.tsx']
  }
});
