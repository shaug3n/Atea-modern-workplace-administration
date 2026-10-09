import React, { useEffect, useMemo, useState } from 'react';
import { messages } from '../../app/messages';
import { useApi } from '../../auth/useApi';
import { ConfirmationDialog } from '../../components/ConfirmationDialog';
import { mutateUser, type UserCommandResponse } from './userMutationApi';
import { UserWriteReasonField, normalizeUserWriteReason } from './UserWriteReasonField';

type LicenseChoice = { skuId: string; partNumber: string; displayName: string };
type LicenseCatalogPayload = { items?: LicenseChoice[]; error?: string | { category?: string; code?: string }; access?: { state?: string } };

export function LicenseAssignmentDialog({ userId, skuId, target, mode, assignedSkuIds = [], disabledPlans = [], onCancel, onCompleted }: { userId: string; skuId: string | null; target?: string; mode: 'assign' | 'remove'; assignedSkuIds?: string[]; disabledPlans?: string[]; onCancel?: () => void; onCompleted?: (result: UserCommandResponse) => void }) {
  const api = useApi();
  const [choices, setChoices] = useState<LicenseChoice[]>([]);
  const [selectedId, setSelectedId] = useState(skuId ?? '');
  const [catalogError, setCatalogError] = useState<string | null>(null);
  const [catalogLoading, setCatalogLoading] = useState(mode === 'assign');
  const [pending, setPending] = useState(false);
  const [reason, setReason] = useState('');
  const [reasonError, setReasonError] = useState<'reason_required' | 'reason_too_long' | null>(null);
  useEffect(() => {
    if (mode !== 'assign') return;
    let cancelled = false;
    setCatalogLoading(true);
    api('/api/licenses?pageSize=100').then(async (response) => {
      const payload = await response.json().catch(() => ({})) as LicenseCatalogPayload;
      const category = catalogErrorCategory(payload, response.ok);
      if (category) throw new Error(category);
      if (!cancelled) setChoices(payload.items ?? []);
    }).catch((error: unknown) => { if (!cancelled) setCatalogError(mapCatalogError(error instanceof Error ? error.message : 'catalog_unavailable')); })
      .finally(() => { if (!cancelled) setCatalogLoading(false); });
    return () => { cancelled = true; };
  }, [api, mode]);
  const selectable = useMemo(() => choices.filter((choice) => !assignedSkuIds.some((assignedId) => assignedId.localeCompare(choice.skuId, undefined, { sensitivity: 'accent' }) === 0)), [assignedSkuIds, choices]);
  const selected = selectable.find((choice) => choice.skuId === selectedId);
  const effectiveId = mode === 'assign' ? selectedId : skuId;
  const effectiveTarget = mode === 'assign' ? selected?.displayName || selected?.partNumber || selected?.skuId || 'Select a license' : target || skuId || '';
  const submit = async () => {
    if (!effectiveId || pending) return;
    const normalizedReason = normalizeUserWriteReason(reason);
    if (normalizedReason.error) {
      setReasonError(normalizedReason.error);
      return;
    }
    setReasonError(null);
    const path = `/api/users/${encodeURIComponent(userId)}/licenses/${encodeURIComponent(effectiveId)}`;
    setPending(true);
    try {
      const result = await mutateUser(api, path, mode === 'assign' ? 'POST' : 'DELETE', { skuId: effectiveId, disabledPlans, reason: normalizedReason.reason });
      onCompleted?.(result);
    }
    catch { onCompleted?.({ status: 'temporarily_unavailable', requiredCapability: 'licenses.assign', replayed: false, error: 'temporarily_unavailable' }); }
    finally { setPending(false); }
  };

  return (
    <ConfirmationDialog
      title={messages.userLicenseDialogTitle}
      target={effectiveTarget}
      proposedChange={mode === 'assign' ? messages.userLicenseAssignProposedChange : messages.userLicenseRemoveProposedChange}
      requiredCapability="licenses.assign"
      confirmLabel={mode === 'assign' ? messages.confirmAssignLicense : messages.confirmRemoveLicense}
      busy={pending}
      confirmBlocked={Boolean(reasonError)}
      onConfirm={submit}
      onCancel={onCancel}
      sourceLimitation={catalogError || (mode === 'assign' && !catalogLoading && choices.length === 0 ? 'No eligible licenses are available for this user.' : mode === 'assign' && !selectedId ? 'Select a license before confirming.' : null)}
    >
      {mode === 'assign' && <label>License<select aria-label="License" value={selectedId} onChange={(event) => setSelectedId(event.target.value)}><option value="">Select a license</option>{selectable.map((choice) => <option key={choice.skuId} value={choice.skuId}>{choice.displayName || choice.partNumber || choice.skuId}</option>)}</select></label>}
      <UserWriteReasonField value={reason} onChange={(value) => { setReason(value); setReasonError(null); }} error={reasonError} />
    </ConfirmationDialog>
  );
}

function mapCatalogError(category: string) {
  if (category === 'capability_required' || category === 'consent_required') return 'Microsoft Graph permissions are required to read the license catalog. Review permissions and try again.';
  if (category === 'not_authorized') return 'Microsoft Graph denied access to the license catalog. Review permissions and try again.';
  if (category === 'not_found') return 'The license catalog is no longer available. Refresh and try again.';
  if (category === 'invalid_request' || category === 'invalid_license') return 'The license catalog request was rejected. Refresh and try again.';
  return 'The license catalog is unavailable. Refresh and try again.';
}

function catalogErrorCategory(payload: LicenseCatalogPayload, responseOk: boolean) {
  const error = typeof payload.error === 'string' ? payload.error : payload.error?.category || payload.error?.code;
  if (error) return error;
  if (!responseOk) return 'catalog_unavailable';
  if (payload.access?.state && !['allowed', 'read_only'].includes(payload.access.state)) return 'capability_required';
  return null;
}
