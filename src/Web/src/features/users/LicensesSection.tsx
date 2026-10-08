import React from 'react';
import { messages } from '../../app/messages';
import { LoadingSkeleton } from '../../components/LoadingSkeleton';
import type { AssignedLicense, UserDetailSection } from './userDetailApi';
import { SectionHeader } from './IdentitySection';

export function LicensesSection({ section, canManage, onAdd, onRemove, busy = false }: { section: UserDetailSection<AssignedLicense>; canManage?: boolean; onAdd?: () => void; onRemove?: (license: AssignedLicense) => void; busy?: boolean }) {
  return (
    <section className="detail-section" aria-labelledby="licenses-section-title" aria-busy={busy}>
      <SectionHeader id="licenses-section-title" title={messages.userLicensesSection} access={section.access} section="licenses" />
      {busy && <LoadingSkeleton label={messages.statusLoading} lines={2} />}
      {canManage && <button type="button" onClick={onAdd}>Assign license</button>}
      {section.items.length === 0 ? <p>{section.access.error?.message ?? messages.userSectionNoData}</p> : (
        <ul className="record-list">
          {section.items.map((license) => (
            <li key={license.skuId}>
              <strong>{license.displayName || license.skuPartNumber || license.skuId}</strong>
              <span>{license.skuPartNumber || license.skuId}</span>
              {canManage && <button type="button" onClick={() => onRemove?.(license)}>Remove license</button>}
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
