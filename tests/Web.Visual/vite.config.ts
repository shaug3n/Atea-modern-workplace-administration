import react from '../../src/Web/node_modules/@vitejs/plugin-react/dist/index.js';
import { defineConfig } from '../../src/Web/node_modules/vite/dist/node/index.js';
import { fileURLToPath, URL } from 'node:url';

const fixtureRoot = fileURLToPath(new URL('.', import.meta.url));
const repositoryRoot = fileURLToPath(new URL('../..', import.meta.url));

export default defineConfig({
  root: fixtureRoot,
  plugins: [react()],
  define: {
    'import.meta.env.VITE_ENTRA_API_SCOPE': JSON.stringify('api://test-api/access_as_user'),
    'import.meta.env.VITE_ENTRA_CLIENT_ID': JSON.stringify('test-client-id'),
    'import.meta.env.VITE_ENTRA_AUTHORITY': JSON.stringify('https://login.microsoftonline.com/organizations'),
    'import.meta.env.VITE_ENTRA_REDIRECT_URI': JSON.stringify('http://127.0.0.1:5173/auth/callback'),
  },
  resolve: {
    alias: [
      {
        find: 'react',
        replacement: fileURLToPath(new URL('../../src/Web/node_modules/react', import.meta.url)),
      },
      {
        find: 'react-dom',
        replacement: fileURLToPath(new URL('../../src/Web/node_modules/react-dom', import.meta.url)),
      },
      {
        find: '@azure/msal-browser',
        replacement: fileURLToPath(new URL('../../src/Web/node_modules/@azure/msal-browser', import.meta.url)),
      },
      {
        find: '@azure/msal-react',
        replacement: fileURLToPath(new URL('../../src/Web/node_modules/@azure/msal-react', import.meta.url)),
      },
      {
        find: /^(\.\.\/)+auth\/useApi$/,
        replacement: fileURLToPath(new URL('./useApiFixture.ts', import.meta.url)),
      },
    ],
  },
  server: {
    fs: { allow: [repositoryRoot] },
  },
});
