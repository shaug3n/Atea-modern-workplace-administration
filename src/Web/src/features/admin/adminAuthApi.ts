export type AdminSession = {
  displayName: string;
};

async function request(path: string, init?: RequestInit): Promise<Response> {
  return fetch(path, { ...init, credentials: 'include' });
}

export const adminAuthApi = {
  async login(username: string, password: string): Promise<AdminSession> {
    const response = await request('/api/admin-auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password }),
    });
    if (!response.ok) throw new Error('admin_login_failed');
    return await response.json() as AdminSession;
  },

  async getSession(): Promise<AdminSession | null> {
    const response = await request('/api/admin-auth/session');
    if (response.status === 401) return null;
    if (!response.ok) throw new Error('admin_session_unavailable');
    return await response.json() as AdminSession;
  },

  async logout(): Promise<void> {
    const response = await request('/api/admin-auth/logout', { method: 'POST' });
    if (!response.ok) throw new Error('admin_logout_failed');
  },
};
