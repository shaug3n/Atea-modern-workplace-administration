import React, { useId, useRef, useState } from 'react';
import { messages } from '../../app/messages';
import { changelogEntries } from './content';
import { ChangelogSection } from './ChangelogSection';
import './about.css';

type AboutTab = 'overview' | 'security' | 'architecture' | 'privacy' | 'changelog';

const tabs: Array<{ id: AboutTab; label: string }> = [
  { id: 'overview', label: messages.aboutTabOverview },
  { id: 'security', label: messages.aboutTabSecurity },
  { id: 'architecture', label: messages.aboutTabArchitecture },
  { id: 'privacy', label: messages.aboutTabPrivacy },
  { id: 'changelog', label: messages.aboutTabChangelog },
];

export function AboutPage() {
  const [activeTab, setActiveTab] = useState<AboutTab>('overview');
  const id = useId();
  const tabRefs = useRef<Partial<Record<AboutTab, HTMLButtonElement>>>({});
  const selectTab = (tab: AboutTab) => {
    setActiveTab(tab);
    tabRefs.current[tab]?.focus();
  };
  const panelContent = (tab: AboutTab) => {
    switch (tab) {
      case 'overview':
        return <section><h2>{messages.aboutOverviewHeading}</h2><p>{messages.aboutOverviewBody}</p><a className="about-inline-link" href="/about/system-versions">{messages.aboutSystemVersionsLink}</a></section>;
      case 'security':
        return <section><h2>{messages.aboutSecurityHeading}</h2><p>{messages.aboutSecurityBody}</p><p>{messages.aboutSecurityTokens}</p><p>{messages.aboutSecurityBoundary}</p></section>;
      case 'architecture':
        return <section><h2>{messages.aboutArchitectureHeading}</h2><p>{messages.aboutArchitectureBody}</p><p>{messages.aboutArchitectureData}</p></section>;
      case 'privacy':
        return <section><h2>{messages.aboutPrivacyHeading}</h2><p>{messages.aboutPrivacyBody}</p><p>{messages.aboutPrivacyRetention}</p><p>{messages.aboutPrivacyBackups}</p><p>{messages.aboutPrivacyDelivery}</p></section>;
      case 'changelog':
        return <ChangelogSection entries={changelogEntries} />;
    }
  };

  return (
    <article className="about-page" aria-labelledby="page-title">
      <header className="about-page__header">
        <p className="about-eyebrow">{messages.navAbout}</p>
        <h1 id="page-title">{messages.aboutTitle}</h1>
        <p>{messages.aboutDescription}</p>
      </header>
      <div className="about-tabs" role="tablist" aria-label={messages.aboutTitle}>
        {tabs.map(tab => (
          <button
            aria-controls={`${id}-${tab.id}-panel`}
            aria-selected={activeTab === tab.id}
            className="about-tab"
            id={`${id}-${tab.id}-tab`}
            key={tab.id}
            onClick={() => setActiveTab(tab.id)}
            onKeyDown={event => {
              const current = tabs.findIndex(item => item.id === activeTab);
              const next = event.key === 'ArrowRight'
                ? (current + 1) % tabs.length
                : event.key === 'ArrowLeft'
                  ? (current - 1 + tabs.length) % tabs.length
                  : event.key === 'Home'
                    ? 0
                    : event.key === 'End'
                      ? tabs.length - 1
                      : -1;
              if (next >= 0) {
                event.preventDefault();
                selectTab(tabs[next].id);
              }
            }}
            ref={element => { if (element) tabRefs.current[tab.id] = element; }}
            role="tab"
            tabIndex={activeTab === tab.id ? 0 : -1}
            type="button"
          >
            {tab.label}
          </button>
        ))}
      </div>
      {tabs.map(tab => (
        <div
          aria-labelledby={`${id}-${tab.id}-tab`}
          className="about-tab-panel"
          hidden={activeTab !== tab.id}
          id={`${id}-${tab.id}-panel`}
          key={tab.id}
          role="tabpanel"
          tabIndex={0}
        >
          {panelContent(tab.id)}
        </div>
      ))}
    </article>
  );
}
