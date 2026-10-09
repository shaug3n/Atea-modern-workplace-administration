import React, { useEffect, useState } from 'react';
import { messages } from '../../app/messages';
import { KpiFilterTile } from '../../components/KpiFilterTile';
import type { UserFiltersState } from './usersApi';

const emptyFilters: UserFiltersState = {
  search: '',
  accountStatus: '',
  tenantRole: '',
  license: '',
  userType: '',
};

export function UserFilters({ filters, onChange }: { filters: UserFiltersState; onChange: (filters: UserFiltersState, historyMode: 'push' | 'replace') => void }) {
  const [draft, setDraft] = useState(filters);
  const [moreOpen, setMoreOpen] = useState(Boolean(filters.license));

  useEffect(() => setDraft(filters), [filters]);

  const update = (patch: Partial<UserFiltersState>, historyMode: 'push' | 'replace' = 'push') => {
    const next = { ...draft, ...patch };
    setDraft(next);
    onChange(next, historyMode);
  };

  const clear = (key: keyof UserFiltersState) => update({ [key]: '' }, key === 'search' ? 'replace' : 'push');
  const chips = activeChips(draft);
  const moreCount = [draft.license, draft.tenantRole].filter(Boolean).length;

  return (
    <form className="users-filters" role="search" onSubmit={(event) => event.preventDefault()}>
      <div className="users-filters__views metric-grid" role="group" aria-label={messages.usersViewShortcutsLabel} style={{ gridColumn: '1 / -1' }}>
        <KpiFilterTile
          label={messages.usersViewAllLabel}
          selected={!draft.accountStatus && !draft.userType}
          onClick={() => update({ accountStatus: '', userType: '' }, 'push')}
        />
        <KpiFilterTile
          label={messages.usersViewEnabledLabel}
          selected={draft.accountStatus === 'enabled' && !draft.userType}
          onClick={() => update({ accountStatus: 'enabled', userType: '' }, 'push')}
        />
        <KpiFilterTile
          label={messages.usersViewDisabledLabel}
          selected={draft.accountStatus === 'disabled' && !draft.userType}
          onClick={() => update({ accountStatus: 'disabled', userType: '' }, 'push')}
        />
        <KpiFilterTile
          label={messages.usersViewGuestsLabel}
          selected={draft.userType === 'Guest' && !draft.accountStatus}
          onClick={() => update({ accountStatus: '', userType: 'Guest' }, 'push')}
        />
      </div>
      <label>
        <span>{messages.usersSearchLabel}</span>
        <input
          type="search"
          value={draft.search}
          onChange={(event) => update({ search: event.currentTarget.value }, 'replace')}
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
        <span>{messages.usersUserTypeLabel}</span>
        <select value={draft.userType} onChange={(event) => update({ userType: event.currentTarget.value })}>
          <option value="">{messages.usersFilterAny}</option>
          <option value="Member">Member</option>
          <option value="Guest">Guest</option>
        </select>
      </label>
      <details className="users-filters__more" open={Boolean(draft.license) || moreOpen} onToggle={event => setMoreOpen(event.currentTarget.open)} onFocusCapture={() => setMoreOpen(true)}>
        <summary>{moreCount > 0 ? `More filters (${moreCount})` : 'More filters'}</summary>
        <div className="users-filters__more-fields">
          <label>
            <span>{messages.usersLicenseLabel}</span>
            <input aria-label={messages.usersLicenseLabel} value={draft.license} onChange={(event) => update({ license: event.currentTarget.value })} placeholder={messages.usersLicensePlaceholder} />
          </label>
          <label>
            <span>{messages.usersTenantRoleLabel}</span>
            <input aria-label={messages.usersTenantRoleLabel} value="" disabled placeholder={messages.usersTenantRolePlaceholder} aria-describedby="users-tenant-role-unavailable" />
            <small id="users-tenant-role-unavailable">Tenant-role filtering is unavailable until the directory has a safe role-assignment index.</small>
          </label>
        </div>
      </details>
      {chips.length > 0 && (
        <div className="filter-chips" aria-label={messages.usersActiveFiltersLabel}>
          {chips.map((chip) => (
            <button key={chip.key} type="button" className="filter-chip" onClick={() => clear(chip.key)} aria-label={chip.clearLabel}>
              {chip.label}
            </button>
          ))}
          <button type="button" className="filter-chip filter-chip--clear" onClick={() => { setDraft(emptyFilters); onChange(emptyFilters, 'push'); }}>
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
    filters.userType && { key: 'userType' as const, label: `${messages.usersUserTypeChip}: ${filters.userType}`, clearLabel: messages.usersClearUserTypeFilter },
    filters.tenantRole && { key: 'tenantRole' as const, label: `${messages.usersTenantRoleChip}: ${filters.tenantRole}`, clearLabel: messages.usersClearTenantRoleFilter },
    filters.license && { key: 'license' as const, label: `${messages.usersLicenseChip}: ${filters.license}`, clearLabel: messages.usersClearLicenseFilter },
  ].filter(Boolean) as Array<{ key: keyof UserFiltersState; label: string; clearLabel: string }>;
}

function labelAccountStatus(value: string) {
  if (value === 'enabled') return messages.usersStatusEnabled;
  if (value === 'disabled') return messages.usersStatusDisabled;
  return value;
}
