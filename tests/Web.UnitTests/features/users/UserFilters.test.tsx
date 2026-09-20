import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { UserFilters } from '../../../../src/Web/src/features/users/UserFilters';

describe('UserFilters', () => {
  afterEach(() => cleanup());

  it('emits supported directory filters and clearly disables unsupported filters', () => {
    const onChange = vi.fn();
    render(<UserFilters filters={{ search: '', accountStatus: '', tenantRole: '', license: '', userType: '' }} onChange={onChange} />);

    fireEvent.change(screen.getByRole('searchbox', { name: 'Search users' }), { target: { value: 'ada' } });
    fireEvent.change(screen.getByLabelText('Account status'), { target: { value: 'enabled' } });
    fireEvent.change(screen.getByLabelText('User type'), { target: { value: 'Member' } });

    expect(screen.getByLabelText('Tenant role')).toHaveProperty('disabled', true);
    expect(screen.getByLabelText('License')).toHaveProperty('disabled', true);

    expect(onChange).toHaveBeenLastCalledWith({
      search: 'ada',
      accountStatus: 'enabled',
      tenantRole: '',
      license: '',
      userType: 'Member',
    });
  });

  it('renders active filter chips and allows clearing one filter at a time', () => {
    const onChange = vi.fn();
    render(<UserFilters filters={{ search: 'ada', accountStatus: 'enabled', tenantRole: '', license: '', userType: 'Member' }} onChange={onChange} />);

    expect(screen.getByText('Search: ada')).toBeTruthy();
    expect(screen.getByText('Status: Enabled')).toBeTruthy();
    expect(screen.getByText('User type: Member')).toBeTruthy();

    fireEvent.click(screen.getByRole('button', { name: 'Clear search filter' }));

    expect(onChange).toHaveBeenCalledWith({
      search: '',
      accountStatus: 'enabled',
      tenantRole: '',
      license: '',
      userType: 'Member',
    });
  });
});
