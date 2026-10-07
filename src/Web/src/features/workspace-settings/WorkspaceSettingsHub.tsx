import { useEffect, useState } from 'react';
import { WorkspacePageHeader } from '../../components/WorkspacePageHeader';
import { DataFreshness } from '../../components/DataFreshness';
import type { AppSession } from '../../components/TenantContextHeader';
import { WorkspaceAccessPage } from '../workspace-access/WorkspaceAccessPage';
import { OnboardingPage } from './OnboardingPage';
import { WorkspaceModulesPage } from './WorkspaceModulesPage';
import { WorkspaceSettingsPage } from './WorkspaceSettingsPage';

const headings = {
  connection: 'onboarding-title',
  general: 'general-title',
  modules: 'modules-title',
  access: 'workspace-access-title',
} as const;

export function WorkspaceSettingsHub({ session }: { session: AppSession }) {
  const canManageSettings = session.workspaceAccess?.canManageSettings === true;
  const canManageModules = session.workspaceAccess?.canManageModules === true;
  const canManageMembers = session.workspaceAccess?.canManageMembers === true;
  const sections = [
    ...(canManageSettings ? [{ id: 'connection', label: 'Connection' }, { id: 'general', label: 'General' }] : []),
    ...(canManageModules ? [{ id: 'modules', label: 'Modules' }] : []),
    ...(canManageMembers ? [{ id: 'access', label: 'Access' }] : []),
  ];

  const [activeHash, setActiveHash] = useState(() => window.location.hash.slice(1));
  const [loadedAt] = useState(() => new Date().toISOString());

  useEffect(() => {
    const focusSection = () => {
      setActiveHash(window.location.hash.slice(1));
      const key = window.location.hash.slice(1) as keyof typeof headings;
      if (!sections.some(section => section.id === key)) return;
      const heading = document.getElementById(headings[key]);
      heading?.focus({ preventScroll: false });
      heading?.scrollIntoView?.({ block: 'start' });
    };
    focusSection();
    window.addEventListener('hashchange', focusSection);
    return () => window.removeEventListener('hashchange', focusSection);
  }, [window.location.hash, canManageSettings, canManageModules, canManageMembers]);

  return <div className="workspace-settings-hub">
    <WorkspacePageHeader eyebrow="Workspace administration" title="Workspace Settings" description="Manage the connection, workspace preferences, modules and member access." meta={<DataFreshness fetchedAt={loadedAt} freshness="fresh" partialData={false} source="Workspace configuration" />} />
    <div className="settings-layout">
    <nav className="settings-index" aria-label="Settings sections">
      {sections.map(section => <a key={section.id} href={`#${section.id}`} aria-current={activeHash === section.id ? 'true' : undefined}>{section.label}</a>)}
    </nav>
    <div className="settings-sections">
    {canManageSettings && <div id="connection"><OnboardingPage embedded /></div>}
    {canManageSettings && <div id="general"><WorkspaceSettingsPage embedded /></div>}
    {canManageModules && <div id="modules"><WorkspaceModulesPage embedded /></div>}
    {canManageMembers && <div id="access"><WorkspaceAccessPage embedded isOwner={session.workspaceAccess?.isOwner === true} canManageModules={session.workspaceAccess?.canManageMemberModules === true} availableModules={session.workspace.moduleAccess ?? []} /></div>}
    </div>
    </div>
  </div>;
}
