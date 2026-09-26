declare module '*.css';
declare module '*.svg' {
  const src: string;
  export default src;
}

interface ImportMetaEnv {
  readonly DEV: boolean;
  readonly VITE_ENTRA_CLIENT_ID?: string;
  readonly VITE_ENTRA_AUTHORITY?: string;
  readonly VITE_ENTRA_API_SCOPE?: string;
  readonly VITE_ENTRA_REDIRECT_URI?: string;
  readonly VITE_ADMIN_AUTH_MODE?: 'local' | 'hosted';
  readonly VITE_PLATFORM_ADMIN_CLIENT_ID?: string;
  readonly VITE_PLATFORM_ADMIN_AUTHORITY?: string;
  readonly VITE_PLATFORM_ADMIN_SCOPE?: string;
  readonly VITE_PLATFORM_ADMIN_REDIRECT_URI?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
