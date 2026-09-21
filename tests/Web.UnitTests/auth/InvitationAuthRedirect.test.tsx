import { describe, expect, it, vi } from 'vitest';
import { createAuthActions } from '../../../src/Web/src/auth/AuthProvider';

describe('invitation authentication redirect', () => {
  it('preserves the invitation return URI with an opaque state value', async () => {
    window.history.pushState({}, '', '/invitations/route-secret?source=email');
    const instance = {
      loginRedirect: vi.fn().mockResolvedValue(undefined),
      logoutRedirect: vi.fn(),
      acquireTokenSilent: vi.fn(),
      acquireTokenRedirect: vi.fn()
    };

    await createAuthActions(instance, { username: 'customer@example.com' } as never, vi.fn()).signIn();

    expect(instance.loginRedirect).toHaveBeenCalledWith({
      scopes: [expect.any(String)],
      redirectStartPage: window.location.href,
      state: expect.stringMatching(/^[a-f0-9-]{36}$/)
    });
    expect(instance.loginRedirect.mock.calls[0][0].state).not.toContain('route-secret');
    window.history.replaceState({}, '', '/');
  });
});
