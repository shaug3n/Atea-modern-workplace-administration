import type { AppSession as ShellAppSession } from '../components/TenantContextHeader';

export type AppSession = Omit<ShellAppSession, 'user'> & {
  user: ShellAppSession['user'] & {
    objectId?: string | null;
  };
};
