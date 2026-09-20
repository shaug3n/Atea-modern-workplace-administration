import React, { useEffect, useState } from 'react';
import { messages } from '../../app/messages';
import type { UserFiltersState } from './usersApi';

const emptyFilters: UserFiltersState = {
  search: '',
  accountStatus: '',
  tenantRole: '',
  license: '',
  userType: '',
};

export function UserFilters({ filters, onChange }: { filters: UserFiltersState; onChange: (filters: UserFiltersState) => void }) {
  const [draft, setDraft] = useState(filters);

  useEffect(() => setDraft(filters), [filters]);

  const update = (patch: Partial<UserFiltersState>) => {
    const next = { ...draft, ...patch };
    setDraft(next);
    onChange(next);
  };

  const clear = (key: keyof UserFiltersState) => update({ [key]: '' });
  const chips = activeChips(draft);

  return (
    <form className="users-filters" role="search" onSubmit={(event) => event.preventDefault()}>
      <label>
        <span>{messages.usersSearchLabel}</span>
        <input
          type="search"
          value={draft.search}
          onChange={(event) => update({ search: event.currentTarget.value })}
          aria-label={messages.usersSearchLabel}
          placeholder={messages.usersSearchPlaceholder}
        />
      </label>
      <label>
        <span>{messages.usersAccountStatusLabel}</span>
        <select value={draft.accountStatus} onChange={(event) => update({ accountStatus: event.currentTarget.value })}>
          <option value="">{messages.usersFilterAny}</option>
          <option value="enabled">{messages.usersStatusEnabled}</option>
          <option value="disabled">{messages.usersStatusDisabled}</option>
        </select>
      </label>
      <label>
        <span>{messages.usersTenantRoleLabel}</span>
        <input value={draft.tenantRole} onChange={(event) => update({ tenantRole: event.currentTarget.value })} placeholder={messages.usersTenantRolePlaceholder} />
      </label>
      <label>
        <span>{messages.usersLicenseLabel}</span>
        <input value={draft.license} onChange={(event) => update({ license: event.currentTarget.value })} placeholder={messages.usersLicensePlaceholder} />
      </label>
      <label>
        <span>{messages.usersUserTypeLabel}</span>
        <select value={draft.userType} onChange={(event) => update({ userType: event.currentTarget.value })}>
          <option value="">{messages.usersFilterAny}</option>
          <option value="Member">Member</option>
          <option value="Guest">Guest</option>
        </select>
      </label>
      {chips.length > 0 && (
        <div className="filter-chips" aria-label={messages.usersActiveFiltersLabel}>
          {chips.map((chip) => (
            <button key={chip.key} type="button" className="filter-chip" onClick={() => clear(chip.key)} aria-label={chip.clearLabel}>
              {chip.label}
            </button>
          ))}
          <button type="button" className="filter-chip filter-chip--clear" onClick={() => { setDraft(emptyFilters); onChange(emptyFilters); }}>
            {messages.usersClearFilters}
          </button>
        </div>
      )}
    </form>
  );
}

function activeChips(filters: UserFiltersState) {
  return [
    filters.search.trim() && { key: 'search' as const, label: `${messages.usersSearchChip}: ${filters.search.trim()}`, clearLabel: messages.usersClearSearchFilter },
    filters.accountStatus && { key: 'accountStatus' as const, label: `${messages.usersStatusChip}: ${labelAccountStatus(filters.accountStatus)}`, clearLabel: messages.usersClearStatusFilter },
    filters.tenantRole.trim() && { key: 'tenantRole' as const, label: `${messages.usersTenantRoleChip}: ${filters.tenantRole.trim()}`, clearLabel: messages.usersClearTenantRoleFilter },
    filters.license.trim() && { key: 'license' as const, label: `${messages.usersLicenseChip}: ${filters.license.trim()}`, clearLabel: messages.usersClearLicenseFilter },
    filters.userType && { key: 'userType' as const, label: `${messages.usersUserTypeChip}: ${filters.userType}`, clearLabel: messages.usersClearUserTypeFilter },
  ].filter(Boolean) as Array<{ key: keyof UserFiltersState; label: string; clearLabel: string }>;
}

function labelAccountStatus(value: string) {
  if (value === 'enabled') return messages.usersStatusEnabled;
  if (value === 'disabled') return messages.usersStatusDisabled;
  return value;
}
