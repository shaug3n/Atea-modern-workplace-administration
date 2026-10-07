import React from 'react';
import { WorkspacePageHeader } from './WorkspacePageHeader';
import { messages } from '../messages/en';

export function NotFoundPage({ path, onNavigate }: { path: string; onNavigate?: (path: string) => void }) {
  return <section className="not-found">
    <WorkspacePageHeader eyebrow="Error 404" title={messages.notFoundTitle} description={messages.notFoundDescription} meta={<code>{path}</code>} />
    <a className="button button--primary" href="/overview" onClick={event => { if (onNavigate) { event.preventDefault(); onNavigate('/overview'); } }}>Go to overview</a>
  </section>;
}
