import { useNavigate } from '../../app/useNavigate';

export function SettingsSummaryPage() {
  const navigate = useNavigate();
  return <section className="settings-page" aria-labelledby="settings-title">
    <header className="page-header"><div><p className="eyebrow">Workspace administration</p><h1 id="settings-title">Settings</h1><p>Configure workspace setup, general preferences, enabled modules and customer access.</p></div></header>
    <div className="settings-summary-grid">
      <SettingsCard title="Setup" description="Connect this workspace to Microsoft 365 and review connection status." href="/settings/setup" onNavigate={navigate} />
      <SettingsCard title="General" description="Update the workspace name and customer-facing preferences." href="/settings/general" onNavigate={navigate} />
      <SettingsCard title="Modules" description="Choose which workspace services are enabled for this customer." href="/settings/modules" onNavigate={navigate} />
      <SettingsCard title="Access" description="Invite people and manage their workspace roles and module access." href="/settings/access" onNavigate={navigate} />
    </div>
  </section>;
}

function SettingsCard({ title, description, href, onNavigate }: { title: string; description: string; href: string; onNavigate: (path: string) => void }) {
  return <article className="settings-summary-card"><h2>{title}</h2><p>{description}</p><a href={href} onClick={(event) => { event.preventDefault(); onNavigate(href); }}>Open {title}</a></article>;
}
