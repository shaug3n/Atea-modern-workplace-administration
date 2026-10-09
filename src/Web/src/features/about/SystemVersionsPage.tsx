import React, { useEffect, useState } from 'react';
import { useApi } from '../../auth/useApi';
import { messages } from '../../app/messages';
import type { AppRoute } from '../../app/routes';
import { readWebBuildMetadata, type WebBuildMetadata } from './buildMetadata';
import { fetchApiBuildMetadata, type ApiBuildMetadata } from './aboutApi';

type ApiState =
  | { status: 'loading' }
  | { status: 'loaded'; metadata: ApiBuildMetadata }
  | { status: 'error'; statusCode?: number };

const webBuild = readWebBuildMetadata(import.meta.env);

function displayValue(value: string | null | undefined) {
  return value?.trim() ? value : 'Unavailable';
}

export function SystemVersionsPage({ authorizedRoutes, webMetadata = webBuild }: {
  authorizedRoutes: Array<Pick<AppRoute, 'path' | 'label'>>;
  webMetadata?: WebBuildMetadata;
}) {
  const api = useApi();
  const [apiState, setApiState] = useState<ApiState>({ status: 'loading' });

  useEffect(() => {
    let cancelled = false;
    setApiState({ status: 'loading' });
    fetchApiBuildMetadata(api).then(metadata => {
      if (!cancelled) setApiState({ status: 'loaded', metadata });
    }).catch(error => {
      if (!cancelled) setApiState({ status: 'error', statusCode: typeof error?.status === 'number' ? error.status : undefined });
    });
    return () => { cancelled = true; };
  }, [api]);

  return (
    <article className="about-page about-system-versions" aria-labelledby="page-title">
      <header className="about-page__header">
        <p className="about-eyebrow">{messages.navAbout}</p>
        <h1 id="page-title">{messages.aboutSystemVersionsTitle}</h1>
        <p>{messages.aboutSystemVersionsDescription}</p>
      </header>
      <div className="about-version-grid">
        <VersionCard title={messages.aboutWebApplication} metadata={webMetadata} />
        <section className="about-version-card" aria-labelledby="about-api-title">
          <h2 id="about-api-title">{messages.aboutApi}</h2>
          {apiState.status === 'loading' && <p role="status">{messages.aboutVersionsLoading}</p>}
          {apiState.status === 'error' && (
            <p className="about-version-error" role="alert">
              {apiState.statusCode ? messages.aboutVersionsHttpError(apiState.statusCode) : messages.aboutVersionsUnavailable}
            </p>
          )}
          {apiState.status === 'loaded' && <MetadataValues metadata={apiState.metadata} />}
        </section>
      </div>
      <section className="about-route-inventory" aria-labelledby="about-route-inventory-heading">
        <h2 id="about-route-inventory-heading">{messages.aboutAuthorizedRoutes}</h2>
        <p>{messages.aboutRouteInventoryDescription}</p>
        <ul>
          {authorizedRoutes.map(route => <li key={route.path}><code>{route.path}</code><span>{route.label}</span></li>)}
        </ul>
      </section>
    </article>
  );
}

function VersionCard({ title, metadata }: { title: string; metadata: WebBuildMetadata }) {
  return (
    <section className="about-version-card" aria-labelledby="about-web-title">
      <h2 id="about-web-title">{title}</h2>
      <MetadataValues metadata={metadata} />
    </section>
  );
}

function MetadataValues({ metadata }: { metadata: { productVersion?: string | null; commit?: string | null; branch?: string | null } }) {
  return (
    <dl className="about-metadata-list">
      <div><dt>{messages.aboutProductVersion}</dt><dd>{displayValue(metadata.productVersion)}</dd></div>
      <div><dt>{messages.aboutCommit}</dt><dd>{displayValue(metadata.commit)}</dd></div>
      <div><dt>{messages.aboutBranch}</dt><dd>{displayValue(metadata.branch)}</dd></div>
    </dl>
  );
}
