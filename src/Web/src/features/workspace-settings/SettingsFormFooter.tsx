import { formatRelative } from '../../format/dateTime';

type Props = {
  dirty: boolean;
  saving: boolean;
  savedAt: string | null;
  error?: string | null;
  onReset: () => void;
  saveLabel?: string;
  onSave?: () => void;
};

export function SettingsFormFooter({ dirty, saving, savedAt, error, onReset, saveLabel = 'Save changes', onSave }: Props) {
  return <div className="settings-form-actions settings-form-footer">
    {dirty && <span className="settings-form-footer__dirty" data-tone="warning">Unsaved changes</span>}
    {!dirty && savedAt && <span role="status">Saved {formatRelative(savedAt)}</span>}
    {error && <span role="alert" className="settings-form-footer__error">{error}</span>}
    <span className="settings-form-footer__buttons">
      <button type="button" className="button button--tertiary" disabled={!dirty || saving} onClick={onReset}>Reset</button>
      <button type={onSave ? 'button' : 'submit'} className="button button--primary" disabled={!dirty || saving} onClick={onSave}>{saving ? 'Saving…' : saveLabel}</button>
    </span>
  </div>;
}
