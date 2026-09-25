import React, { useEffect, useState } from 'react';
import { adminApi, type WorkspaceOnboardingResult, type WorkspaceSummary } from './adminApi';
import { WorkspaceCreateForm } from './WorkspaceCreateForm';

export function WorkspaceListPage({ onOpenWorkspace }: { onOpenWorkspace: (id: string, firstAdminInvitation?: WorkspaceOnboardingResult) => void }) {
  const [items, setItems] = useState<WorkspaceSummary[] | null>(null);
  const [error, setError] = useState('');
  const [create, setCreate] = useState(false);
  useEffect(() => { adminApi.listWorkspaces().then(setItems).catch(reason => setError(reason instanceof Error ? reason.message : 'Workspaces are unavailable.')); }, []);
  if (error) return <section className="admin-card"><h1>Workspaces</h1><p role="alert">{error}</p></section>;
  if (!items) return <p role="status">Loading workspaces…</p>;

  if (create) return <WorkspaceCreateForm onCancel={() => setCreate(false)} onSubmit={async input => {
    setError('');
    const result = await adminApi.onboardWorkspace(input);
    setItems(current => [...(current ?? []), result.workspace]);
    setCreate(false);
    onOpenWorkspace(result.workspace.id, result);
    return result;
  }} />;

  return <section>
    <div className="admin-page-heading">
      <div><p className="eyebrow">Platform administration</p><h1>Workspaces</h1><p>Onboard the customer workspace and its first administrator together.</p></div>
      <button type="button" onClick={() => setCreate(true)}>Create workspace</button>
    </div>
    {items.length === 0 ? <p className="admin-card">No workspaces are available.</p> : <div className="admin-list">{items.map(item => <button className="admin-card admin-list__item" type="button" key={item.id} onClick={() => onOpenWorkspace(item.id)}><strong>{item.displayName}</strong><span>Tenant ID: {item.tenantId}</span><span>Status: {item.connectionStatus}</span></button>)}</div>}
  </section>;
}
