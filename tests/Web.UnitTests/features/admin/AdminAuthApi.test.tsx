import { afterEach, describe, expect, it, vi } from 'vitest';
import { adminAuthApi } from '../../../../src/Web/src/features/admin/adminAuthApi';
import { adminApi, setAdminPlatformTokenProvider } from '../../../../src/Web/src/features/admin/adminApi';

describe('adminAuthApi', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('returns the API displayName session metadata', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ displayName: 'Admin User' }), { status: 200 })));
    await expect(adminAuthApi.getSession()).resolves.toEqual({ displayName: 'Admin User' });
  });

  it('sends the hosted Entra bearer token only to the platform session endpoint', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ displayName: 'Atea Operator', objectId: 'operator-1', tenantId: 'tenant-1' }), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    await expect(adminAuthApi.getPlatformSession('access-token')).resolves.toEqual({ displayName: 'Atea Operator', objectId: 'operator-1', tenantId: 'tenant-1' });
    expect(fetchMock).toHaveBeenCalledWith('/api/platform/session', expect.objectContaining({
      headers: { Authorization: 'Bearer access-token' },
      credentials: 'omit',
    }));
  });

  it('distinguishes hosted unauthorized and forbidden responses', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(new Response(null, { status: 401 })).mockResolvedValueOnce(new Response(null, { status: 403 })));
    await expect(adminAuthApi.getPlatformSession('expired')).rejects.toThrow('hosted_admin_unauthorized');
    await expect(adminAuthApi.getPlatformSession('limited')).rejects.toThrow('hosted_admin_forbidden');
  });

  it('attaches the hosted token to platform requests but not local admin-auth requests', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response('[]', { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ displayName: 'Local Admin' }), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);
    const clearProvider = setAdminPlatformTokenProvider(async () => 'platform-token');

    await adminApi.listWorkspaces();
    await adminAuthApi.getSession();
    clearProvider();

    expect(new Headers(fetchMock.mock.calls[0]?.[1]?.headers).get('Authorization')).toBe('Bearer platform-token');
    expect(fetchMock.mock.calls[1]?.[1]).toEqual(expect.objectContaining({ credentials: 'include' }));
    expect(new Headers(fetchMock.mock.calls[1]?.[1]?.headers).has('Authorization')).toBe(false);
  });

  it('rejects a failed logout response', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 500 })));
    await expect(adminAuthApi.logout()).rejects.toThrow('admin_logout_failed');
  });
});
