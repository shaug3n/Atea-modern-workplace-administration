import { afterEach, describe, expect, it, vi } from 'vitest';
import { adminAuthApi } from '../../../../src/Web/src/features/admin/adminAuthApi';

describe('adminAuthApi', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('returns the API displayName session metadata', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ displayName: 'Admin User' }), { status: 200 })));
    await expect(adminAuthApi.getSession()).resolves.toEqual({ displayName: 'Admin User' });
  });

  it('rejects a failed logout response', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 500 })));
    await expect(adminAuthApi.logout()).rejects.toThrow('admin_logout_failed');
  });
});
