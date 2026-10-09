import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { UserFilters } from '../../../../src/Web/src/features/users/UserFilters';

describe('UserFilters', () => {
  afterEach(() => cleanup());

  it('keeps the first row to search, status and type with optional filters in a disclosure', () => {
    render(<UserFilters filters={{ search: '', accountStatus: '', tenantRole: '', license: '', userType: '' }} onChange={vi.fn()} />);
    const more = screen.getByText('More filters');
    expect(more.closest('details')).toBeTruthy();
    expect(screen.getByLabelText('License').closest('details')).toBe(more.closest('details'));
    expect(screen.getByLabelText('Tenant role').closest('details')).toBe(more.closest('details'));
  });

  it('marks tenant-role filtering unavailable until a safe role index exists', () => {
    const onChange = vi.fn();
    render(<UserFilters filters={{ search: '', accountStatus: '', tenantRole: '', license: '', userType: '' }} onChange={onChange} />);

    fireEvent.change(screen.getByRole('searchbox', { name: 'Search users' }), { target: { value: 'ada' } });
    fireEvent.change(screen.getByLabelText('Account status'), { target: { value: 'enabled' } });
    fireEvent.change(screen.getByLabelText('User type'), { target: { value: 'Member' } });

    fireEvent.change(screen.getByLabelText('License'), { target: { value: 'ENTERPRISEPACK' } });

    expect(onChange).toHaveBeenLastCalledWith({
      search: 'ada',
      accountStatus: 'enabled',
      tenantRole: '',
      license: 'ENTERPRISEPACK',
      userType: 'Member',
    }, 'push');
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
    }, 'replace');
  });

  it('renders four accessible shortcut tiles without numeric values', () => {
    render(<UserFilters filters={{ search: '', accountStatus: 'enabled', tenantRole: '', license: '', userType: '' }} onChange={vi.fn()} />);

    for (const label of ['All users', 'Enabled', 'Disabled', 'Guests']) {
      const tile = screen.getByRole('button', { name: new RegExp(label) });
      expect(tile.getAttribute('aria-pressed')).toBe(label === 'Enabled' ? 'true' : 'false');
      expect(tile.querySelector('.metric-card__value')).toBeNull();
    }
  });

  it.each([
    ['All users', '', ''],
    ['Enabled', 'enabled', ''],
    ['Disabled', 'disabled', ''],
    ['Guests', '', 'Guest'],
  ])('selecting %s changes only its view predicates', (label, accountStatus, userType) => {
    const onChange = vi.fn();
    render(<UserFilters filters={{ search: 'ada', accountStatus: 'disabled', tenantRole: '', license: 'E3', userType: 'Member' }} onChange={onChange} />);

    fireEvent.click(screen.getByRole('button', { name: new RegExp(label) }));

    expect(onChange).toHaveBeenLastCalledWith({
      search: 'ada',
      accountStatus,
      tenantRole: '',
      license: 'E3',
      userType,
    }, 'push');
  });

  it('renders chips for tenant role and license values', () => {
    render(<UserFilters filters={{ search: '', accountStatus: '', tenantRole: 'Global Reader', license: 'ENTERPRISEPACK', userType: '' }} onChange={vi.fn()} />);

    expect(screen.getByText('Tenant role: Global Reader')).toBeTruthy();
    expect(screen.getByText('License: ENTERPRISEPACK')).toBeTruthy();
  });

  it('shows the count of active advanced filters in the summary', () => {
    render(<UserFilters filters={{ search: '', accountStatus: '', tenantRole: 'Global Reader', license: 'ENTERPRISEPACK', userType: '' }} onChange={vi.fn()} />);
    expect(screen.getByText('More filters (2)')).toBeTruthy();
  });
});
