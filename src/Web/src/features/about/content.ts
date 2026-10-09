export type ChangelogType = 'New' | 'Improved' | 'Fixed';

export type ChangelogEntry = {
  id: string;
  release: string;
  type: ChangelogType;
  description: string;
};

export const changelogEntries: ChangelogEntry[] = [
  {
    id: 'w0-workspace-shell',
    release: 'W0 foundation · 2026-10-08',
    type: 'New',
    description: 'Introduced a shared workspace shell with primary navigation, tenant context, responsive navigation and a theme control.',
  },
  {
    id: 'w0-access-states',
    release: 'W0 foundation · 2026-10-08',
    type: 'Improved',
    description: 'Added reusable access and data-freshness presentations so permission and availability states are visible in the workspace.',
  },
  {
    id: 'w0-capability-labels',
    release: 'W0 foundation · 2026-10-08',
    type: 'Fixed',
    description: 'Restored reserved capability labels so the front-end permission catalog matches the API contract.',
  },
];
