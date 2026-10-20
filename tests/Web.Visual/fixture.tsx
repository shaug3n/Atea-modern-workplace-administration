import React, { useState } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from '../../src/Web/src/app/App';
import { ActionMenu } from '../../src/Web/src/components/ActionMenu';
import { ReasonDialog } from '../../src/Web/src/components/ReasonDialog';
import { UserCreateDialog } from '../../src/Web/src/features/users/UserCreateDialog';
import type { CapabilitySnapshot } from '../../src/Web/src/capabilities/capabilityTypes';
import type { AppSession } from '../../src/Web/src/app/sessionTypes';
import type { ThemePreferenceStore } from '../../src/Web/src/components/ThemeToggle';
import '../../src/Web/src/styles/theme.css';
import '../../src/Web/src/styles/foundations.css';
import '../../src/Web/src/styles/components.css';

const workspaceId = 'fixture-workspace';
const session: AppSession = {
  user: { displayName: 'Local Visual Admin', userPrincipalName: 'admin@example.test', objectId: 'fixture-admin' },
  workspace: {
    id: workspaceId, name: 'Local Customer', enabledModules: ['users', 'devices', 'licenses', 'exchange', 'authentication-campaigns', 'about', 'feedback'],
    moduleAccess: ['users', 'devices', 'licenses', 'exchange', 'authentication-campaigns', 'license-hygiene', 'about', 'feedback'],
  },
  workspaceAccess: { role: 'customer_admin', isOwner: true, canManageMembers: true, canManageSettings: true, canManageModules: true, canManageMemberModules: true },
};

const capabilities: CapabilitySnapshot = {
  workspaceId,
  evaluatedAt: '2026-09-21T12:00:00.000Z',
  sourceState: 'graph_authoritative',
  capabilities: [
    'users.view', 'users.create', 'users.update', 'users.disable', 'users.reset_password',
    'users.sessions.revoke', 'groups.manage_members', 'licenses.view', 'licenses.assign',
    'roles.assign', 'pim.view', 'pim.activate', 'devices.view', 'devices.manage',
    'authentication.campaigns.view', 'licenses.hygiene.view', 'audit.view',
    'platform.about.view', 'feedback.submit',
  ].map(capability => ({ capability, state: 'allowed', reasonCode: 'local_visual_fixture' })),
};

const loadSession = async () => session;
const loadCapabilities = async () => {
  if (new URLSearchParams(window.location.search).get('fixtureState') === 'denied') {
    return {
      ...capabilities,
      capabilities: capabilities.capabilities.map(item => item.capability === 'audit.view' ? { ...item, state: 'hidden' as const } : item),
    };
  }
  return capabilities;
};
const loadConnectionHealth = async () => ({ status: 'permission_incomplete' as const, lastVerifiedAt: null });
const preferenceStore: ThemePreferenceStore = {
  load: async () => {
    const preference = new URLSearchParams(window.location.search).get('theme');
    return preference === 'dark' || preference === 'light' ? preference : 'light';
  },
  save: async () => {},
};

function FixtureTools() {
  const [open, setOpen] = useState(false);
  const [reasonOpen, setReasonOpen] = useState(false);
  const [createOpen, setCreateOpen] = useState(false);
  const [extraValue, setExtraValue] = useState('Local fixture note');
  const [status, setStatus] = useState('No fixture action selected.');
  if (new URLSearchParams(window.location.search).get('tools') !== '1') return null;

  return <>
    <details className="visual-fixture-tools" open={open} onToggle={event => setOpen((event.currentTarget as HTMLDetailsElement).open)}>
      <summary>Visual QA tools</summary>
      <div className="visual-fixture-tools__content">
        <p>Local fixtures only. No tenant or Graph connection.</p>
        <ActionMenu label="Fixture actions" ariaLabel="Local visual fixture actions" items={[
          { label: 'Busy action', onSelect: () => setStatus('Busy action selected.'), busy: true },
          { label: 'Unavailable action', onSelect: () => setStatus('Disabled action selected.'), disabled: true },
          { label: 'Refresh fixture', onSelect: () => setStatus('Local fixture action selected.') },
        ]} />
        <button type="button" className="button button--secondary" onClick={() => setReasonOpen(true)}>Open ReasonDialog fixture</button>
        <button type="button" className="button button--secondary" onClick={() => setCreateOpen(true)}>Open user-create dialog</button>
        <p role="status">{status}</p>
      </div>
    </details>
    {reasonOpen && <ReasonDialog
      title="Review workspace change"
      target="Local visual workspace"
      proposedChange="Inspect caller-controlled fields and reason handling."
      requiredCapability="workspace.settings.manage"
      confirmLabel="Save fixture review"
      reasonHint="This content stays in the local fixture."
      extraFields={<div className="visual-extra-fields">{Array.from({ length: 10 }, (_, index) => (
        <label key={index}>Local review field {index + 1}
          <input aria-label={`Local review field ${index + 1}`} value={index === 0 ? extraValue : `Fixture value ${index + 1}`} readOnly={index !== 0} onChange={index === 0 ? event => setExtraValue(event.target.value) : undefined} />
        </label>
      ))}</div>}
      onConfirm={reason => { setStatus(`Reason confirmed (${reason.length} characters).`); setReasonOpen(false); }}
      onCancel={() => setReasonOpen(false)}
    />}
    {createOpen && <UserCreateDialog onCancel={() => setCreateOpen(false)} onCompleted={() => setStatus('Local user-create fixture completed.')} />}
  </>;
}

function FixtureApp() {
  return <>
    <style>{`
      .visual-fixture-tools { position: fixed; right: 1rem; bottom: 1rem; z-index: 20; width: min(22rem, calc(100vw - 2rem)); border: 1px solid var(--uw-border-strong); border-radius: var(--uw-radius-md); background: var(--uw-surface); box-shadow: var(--uw-elevation-2); }
      .visual-fixture-tools > summary { padding: .75rem 1rem; color: var(--uw-heading); cursor: pointer; font-weight: 700; }
      .visual-fixture-tools__content { display: grid; gap: .75rem; padding: 0 1rem 1rem; }
      .visual-fixture-tools__content p { margin: 0; }
      .visual-fixture-tools__content .action-menu { position: relative; }
      .visual-extra-fields { display: grid; gap: .75rem; }
      .visual-extra-fields label { display: grid; gap: .25rem; }
    `}</style>
    <App loadSession={loadSession} loadCapabilities={loadCapabilities} loadConnectionHealth={loadConnectionHealth} themePreferenceStore={preferenceStore} />
    <FixtureTools />
  </>;
}

createRoot(document.getElementById('root')!).render(<FixtureApp />);
