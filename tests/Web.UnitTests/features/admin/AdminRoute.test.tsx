import { describe, expect, it } from 'vitest';
import { isAdminPath } from '../../../../src/Web/src/features/admin/AdminApp';

describe('admin route boundary', () => {
  it('matches only /admin and its child routes', () => {
    expect(isAdminPath('/admin')).toBe(true);
    expect(isAdminPath('/admin/')).toBe(true);
    expect(isAdminPath('/admin/users')).toBe(true);
    expect(isAdminPath('/administrator')).toBe(false);
    expect(isAdminPath('/administer')).toBe(false);
    expect(isAdminPath('/')).toBe(false);
  });
});
