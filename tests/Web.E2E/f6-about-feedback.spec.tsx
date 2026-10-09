import React from 'react';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { App } from '../../src/Web/src/app/App';
import type { CapabilitySnapshot } from '../../src/Web/src/capabilities/capabilityTypes';
import { ChangelogSection } from '../../src/Web/src/features/about/ChangelogSection';
import { changelogEntries } from '../../src/Web/src/features/about/content';

const apiMock = vi.hoisted(() => vi.fn());
vi.mock('../../src/Web/src/auth/useApi', () => ({ useApi: () => apiMock }));

const workspaceA = '55555555-5555-5555-5555-555555555555';
const workspaceB = '66666666-6666-6666-6666-666666666666';
const aboutCapability = { capability: 'platform.about.view', state: 'allowed', reasonCode: 'workspace_member' } as const;
const feedbackCapability = { capability: 'feedback.submit', state: 'allowed', reasonCode: 'workspace_member' } as const;

function session(workspaceId: string, modules: string[], objectId = '22222222-2222-2222-2222-222222222222') {
  return {
    user: { displayName: 'Alex Morgan', objectId },
    workspace: {
      id: workspaceId,
      name: `Workspace ${workspaceId === workspaceA ? 'A' : 'B'}`,
      enabledModules: modules,
      moduleAccess: modules,
    },
  };
}

function capabilities(workspaceId: string, decisions: readonly CapabilitySnapshot['capabilities'][number][]): CapabilitySnapshot {
  return {
    workspaceId,
    evaluatedAt: '2026-10-09T08:00:00Z',
    sourceState: 'graph_authoritative',
    capabilities: [...decisions],
  };
}

function renderApp(path: string, workspaceId: string, modules: string[], decisions: readonly CapabilitySnapshot['capabilities'][number][], objectId?: string) {
  window.history.replaceState({}, '', path);
  return render(<App
    loadCapabilities={async () => capabilities(workspaceId, decisions)}
    loadSession={async () => session(workspaceId, modules, objectId)}
  />);
}

function feedbackItem(id: string, subject: string, message = 'Saved message') {
  return {
    id,
    category: 'General',
    subject,
    message,
    createdAt: '2026-10-09T08:00:00Z',
    expiresAt: '2027-01-07T08:00:00Z',
  };
}

function feedbackPage(items: ReturnType<typeof feedbackItem>[], nextCursor: string | null = null) {
  return Response.json({ items, nextCursor });
}

function fillFeedback(subject = 'Saved subject', message = 'Saved message') {
  fireEvent.change(screen.getByLabelText('Category'), { target: { value: 'General' } });
  fireEvent.change(screen.getByLabelText('Subject'), { target: { value: subject } });
  fireEvent.change(screen.getByLabelText('Message'), { target: { value: message } });
}

describe('owned About and Feedback journeys', () => {
  afterEach(() => {
    cleanup();
    apiMock.mockReset();
    window.history.replaceState({}, '', '/');
  });

  it('About_tabs_changelog_and_system_versions_render_authorized_content', async () => {
    const appSession = session(workspaceA, ['about', 'feedback', 'users']);
    const decisions = [
      aboutCapability,
      { capability: 'feedback.submit', state: 'hidden', reasonCode: 'role_missing' },
      { capability: 'users.view', state: 'hidden', reasonCode: 'role_missing' },
    ] as const;
    window.history.replaceState({}, '', '/about');
    apiMock.mockResolvedValue(Response.json({
      productVersion: '0.2.0',
      commit: 'api-commit-fixture',
      branch: 'api-release-fixture',
    }));

    render(<App loadCapabilities={async () => capabilities(workspaceA, decisions)} loadSession={async () => appSession} />);

    expect((await screen.findAllByRole('tab')).map(tab => tab.textContent)).toEqual([
      'Overview', 'Security & compliance', 'Architecture', 'Privacy', 'Changelog',
    ]);
    fireEvent.click(screen.getByRole('tab', { name: 'Changelog' }));
    const order = Array.from(document.querySelectorAll<HTMLElement>('[data-changelog-entry]'))
      .map(entry => entry.dataset.changelogEntry);
    for (const type of ['All', 'New', 'Improved', 'Fixed']) {
      expect(screen.getByRole('button', { name: type })).toBeTruthy();
    }
    for (const type of ['New', 'Improved', 'Fixed']) {
      fireEvent.click(screen.getByRole('button', { name: type }));
      const entries = Array.from(document.querySelectorAll<HTMLElement>('[data-changelog-entry]'));
      expect(entries.length).toBeGreaterThan(0);
      expect(entries.every(entry => within(entry).getByText(type))).toBe(true);
      expect(entries.map(entry => entry.dataset.changelogEntry))
        .toEqual(order.filter(id => changelogEntries.find(entry => entry.id === id)?.type === type));
    }
    cleanup();

    render(<ChangelogSection entries={[]} />);
    expect(screen.getByRole('status').textContent).toBe('No changelog entries match this filter.');
    cleanup();

    window.history.replaceState({}, '', '/about/system-versions');
    render(<App loadCapabilities={async () => capabilities(workspaceA, decisions)} loadSession={async () => appSession} />);

    expect(await screen.findByRole('heading', { name: 'System versions' })).toBeTruthy();
    expect(await screen.findByText('api-commit-fixture')).toBeTruthy();
    const webCard = screen.getByRole('region', { name: 'Web application' });
    const apiCard = screen.getByRole('region', { name: 'API' });
    expect(within(apiCard).getByText('api-commit-fixture')).toBeTruthy();
    expect(within(apiCard).getByText('api-release-fixture')).toBeTruthy();
    expect(within(webCard).queryByText('api-commit-fixture')).toBeNull();
    expect(within(webCard).queryByText('api-release-fixture')).toBeNull();
    const inventory = screen.getByRole('region', { name: 'Authorized application routes' });
    const inventoryPaths = Array.from(inventory.querySelectorAll('code'), code => code.textContent);
    expect(inventoryPaths).toEqual([
      '/onboarding',
      '/identity',
      '/about',
      '/about/system-versions',
      '/overview',
    ]);
    expect(apiMock).toHaveBeenCalledWith('/api/about/system-versions', { cache: 'no-store' });
  });

  it('describes malformed successful API metadata as an invalid response, not HTTP 200', async () => {
    apiMock.mockResolvedValue(new Response('{not-json', { status: 200 }));
    renderApp('/about/system-versions', workspaceA, ['about'], [aboutCapability]);

    const error = await screen.findByRole('alert');
    expect(error.textContent).toMatch(/invalid/i);
    expect(error.textContent).not.toContain('HTTP 200');
  });

  it('Feedback_shell_button_and_shortcut_open_the_same_dialog', async () => {
    apiMock.mockResolvedValue(feedbackPage([]));
    renderApp('/feedback', workspaceA, ['feedback'], [feedbackCapability]);

    expect(await screen.findByRole('heading', { name: 'No feedback yet' })).toBeTruthy();
    const launchButton = document.querySelector<HTMLButtonElement>('.app-feedback-fab')!;
    launchButton.focus();
    fireEvent.click(launchButton);
    expect(screen.getByRole('dialog').getAttribute('aria-modal')).toBe('true');
    expect(document.activeElement).toBe(screen.getByLabelText('Category'));

    fireEvent.keyDown(screen.getByLabelText('Subject'), { key: 'f', altKey: true, shiftKey: true });
    fireEvent.keyDown(screen.getByLabelText('Message'), { key: 'f', altKey: true, shiftKey: true, isComposing: true });
    fireEvent.keyDown(screen.getByLabelText('Category'), { key: 'Process', altKey: true, shiftKey: true, keyCode: 229 });
    expect(screen.getAllByRole('dialog')).toHaveLength(1);

    fireEvent.keyDown(document.activeElement!, { key: 'Escape' });
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    await waitFor(() => expect(document.activeElement).toBe(launchButton));
    fireEvent.keyDown(document, { key: 'f', altKey: true, shiftKey: true });
    expect(screen.getByRole('dialog')).toBeTruthy();
    expect(document.activeElement).toBe(screen.getByLabelText('Category'));
  });

  it('Feedback_success_refreshes_own_list_and_never_claims_delivery', async () => {
    const savedItem = feedbackItem('22222222-2222-4222-8222-222222222222', 'Shell submission', 'Visible only in my list');
    let finishSave!: (response: Response) => void;
    apiMock
      .mockResolvedValueOnce(feedbackPage([]))
      .mockImplementationOnce(() => new Promise<Response>(resolve => { finishSave = resolve; }))
      .mockResolvedValueOnce(feedbackPage([savedItem]));
    renderApp('/feedback', workspaceA, ['feedback'], [feedbackCapability]);

    expect(await screen.findByRole('heading', { name: 'No feedback yet' })).toBeTruthy();
    fireEvent.click(document.querySelector('.app-feedback-fab')!);
    fillFeedback('Shell submission', 'Visible only in my list');
    fireEvent.click(screen.getByRole('button', { name: 'Save feedback' }));

    const saving = await screen.findByRole('button', { name: 'Saving…' });
    expect((saving as HTMLButtonElement).disabled).toBe(true);
    finishSave(Response.json({
      id: savedItem.id,
      createdAt: savedItem.createdAt,
      expiresAt: savedItem.expiresAt,
    }, { status: 201 }));
    expect((await screen.findByRole('status')).textContent).toBe('Saved');
    expect(await screen.findByText('Visible only in my list')).toBeTruthy();
    const post = apiMock.mock.calls.find(([, init]) => init?.method === 'POST');
    expect(post).toBeTruthy();
    expect(Object.keys(JSON.parse(post![1].body as string)).sort()).toEqual(['category', 'message', 'subject']);
    expect(new Headers(post![1].headers).get('Idempotency-Key')).toMatch(/.+/);
    expect(apiMock.mock.calls.filter(([, init]) => init?.method !== 'POST')).toHaveLength(2);
    expect(document.body.textContent).not.toMatch(/Sent to support|Under review/i);
  });

  it('Feedback_server_failure_preserves_input_and_never_shows_success', async () => {
    apiMock
      .mockResolvedValueOnce(feedbackPage([]))
      .mockResolvedValueOnce(Response.json({ error: 'persistence_unavailable' }, { status: 503 }));
    renderApp('/feedback', workspaceA, ['feedback'], [feedbackCapability]);

    expect(await screen.findByRole('heading', { name: 'No feedback yet' })).toBeTruthy();
    fireEvent.click(document.querySelector('.app-feedback-fab')!);
    fillFeedback('Keep this subject', 'Keep this message');
    fireEvent.click(screen.getByRole('button', { name: 'Save feedback' }));

    expect((await screen.findByRole('alert')).textContent).toMatch(/could not be saved/i);
    expect((screen.getByLabelText('Subject') as HTMLInputElement).value).toBe('Keep this subject');
    expect((screen.getByLabelText('Message') as HTMLTextAreaElement).value).toBe('Keep this message');
    expect(screen.queryByRole('status')).toBeNull();
  });

  it('Feedback_empty_and_populated_states_are_owner_scoped', async () => {
    let finishInitialLoad!: (response: Response) => void;
    apiMock.mockImplementationOnce(() => new Promise<Response>(resolve => { finishInitialLoad = resolve; }));
    renderApp('/feedback', workspaceA, ['feedback'], [feedbackCapability]);

    expect(screen.getByRole('status').textContent).toContain('Loading');
    await waitFor(() => expect(apiMock).toHaveBeenCalledOnce());
    finishInitialLoad(feedbackPage([]));
    expect(await screen.findByRole('heading', { name: 'No feedback yet' })).toBeTruthy();
    cleanup();

    apiMock.mockReset();
    apiMock.mockResolvedValue(new Response('not json', { status: 200 }));
    renderApp('/feedback', workspaceA, ['feedback'], [feedbackCapability]);
    expect((await screen.findByRole('alert')).textContent).toMatch(/could not be loaded/i);
    expect(screen.queryByRole('heading', { name: 'No feedback yet' })).toBeNull();
    cleanup();

    const firstPage = Array.from({ length: 20 }, (_, index) => feedbackItem(
      `11111111-1111-4111-8111-${String(index).padStart(12, '0')}`,
      `Feedback item ${index + 1}`,
    ));
    const nextPage = [feedbackItem('33333333-3333-4333-8333-333333333333', 'Feedback item 21')];
    apiMock.mockReset();
    apiMock.mockResolvedValueOnce(feedbackPage(firstPage, 'next-page'))
      .mockResolvedValueOnce(feedbackPage(nextPage));
    renderApp('/feedback', workspaceA, ['feedback'], [feedbackCapability]);

    expect(await screen.findByText('Feedback item 20')).toBeTruthy();
    expect(document.querySelectorAll('[data-feedback-subject]')).toHaveLength(20);
    fireEvent.click(screen.getByRole('button', { name: 'Load more feedback' }));
    expect(await screen.findByText('Feedback item 21')).toBeTruthy();
    expect(document.querySelectorAll('[data-feedback-subject]')).toHaveLength(21);
    expect(apiMock.mock.calls[1][0]).toBe('/api/feedback/submissions?cursor=next-page');
  });

  it('discards a pending list response and clears the prior owner when workspace identity changes', async () => {
    let finishOldPage!: (response: Response) => void;
    apiMock
      .mockResolvedValueOnce(feedbackPage([feedbackItem('44444444-4444-4444-8444-444444444444', 'Workspace A private entry')], 'old-cursor'))
      .mockImplementationOnce(() => new Promise<Response>(resolve => { finishOldPage = resolve; }))
      .mockResolvedValueOnce(feedbackPage([feedbackItem('55555555-5555-4555-8555-555555555555', 'Workspace B private entry')]));
    const oldCapabilities = capabilities(workspaceA, [feedbackCapability]);
    const oldSession = session(workspaceA, ['feedback'], '22222222-2222-4222-8222-222222222222');
    window.history.replaceState({}, '', '/feedback');
    const { rerender } = render(<App
      loadCapabilities={async () => oldCapabilities}
      loadSession={async () => oldSession}
    />);

    expect(await screen.findByText('Workspace A private entry')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Load more feedback' }));
    await waitFor(() => expect(apiMock).toHaveBeenCalledTimes(2));

    const newCapabilities = capabilities(workspaceB, [feedbackCapability]);
    const newSession = session(workspaceB, ['feedback'], '33333333-3333-4333-8333-333333333333');
    rerender(<App
      loadCapabilities={async () => newCapabilities}
      loadSession={async () => newSession}
    />);

    expect(await screen.findByText('Workspace B private entry')).toBeTruthy();
    expect(screen.queryByText('Workspace A private entry')).toBeNull();
    finishOldPage(feedbackPage([feedbackItem('66666666-6666-4666-8666-666666666666', 'Late stale response')]));
    await waitFor(() => expect(screen.queryByText('Late stale response')).toBeNull());
    expect(screen.getByText('Workspace B private entry')).toBeTruthy();
  });

  it('discards a pending list response when identity changes within the same workspace', async () => {
    let finishOldPage!: (response: Response) => void;
    apiMock
      .mockResolvedValueOnce(feedbackPage([feedbackItem('77777777-7777-4777-8777-777777777777', 'First identity private entry')], 'old-cursor'))
      .mockImplementationOnce(() => new Promise<Response>(resolve => { finishOldPage = resolve; }))
      .mockResolvedValueOnce(feedbackPage([feedbackItem('88888888-8888-4888-8888-888888888888', 'Second identity private entry')]));
    const firstCapabilities = capabilities(workspaceA, [feedbackCapability]);
    const firstSession = session(workspaceA, ['feedback'], '22222222-2222-4222-8222-222222222222');
    window.history.replaceState({}, '', '/feedback');
    const { rerender } = render(<App
      loadCapabilities={async () => firstCapabilities}
      loadSession={async () => firstSession}
    />);

    expect(await screen.findByText('First identity private entry')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Load more feedback' }));
    await waitFor(() => expect(apiMock).toHaveBeenCalledTimes(2));

    const secondCapabilities = capabilities(workspaceA, [feedbackCapability]);
    const secondSession = session(workspaceA, ['feedback'], '33333333-3333-4333-8333-333333333333');
    rerender(<App
      loadCapabilities={async () => secondCapabilities}
      loadSession={async () => secondSession}
    />);

    expect(await screen.findByText('Second identity private entry')).toBeTruthy();
    expect(screen.queryByText('First identity private entry')).toBeNull();
    finishOldPage(feedbackPage([feedbackItem('99999999-9999-4999-8999-999999999999', 'Late stale identity response')]));
    await waitFor(() => expect(screen.queryByText('Late stale identity response')).toBeNull());
    expect(screen.getByText('Second identity private entry')).toBeTruthy();
  });

  it.each([
    {
      name: 'About module disabled',
      path: '/about/system-versions',
      modules: ['feedback'],
      decisions: [aboutCapability],
      protectedRequest: '/api/about/system-versions',
    },
    {
      name: 'About capability denied',
      path: '/about/system-versions',
      modules: ['about'],
      decisions: [{ capability: 'platform.about.view', state: 'hidden', reasonCode: 'role_missing' }],
      protectedRequest: '/api/about/system-versions',
    },
    {
      name: 'Feedback module disabled',
      path: '/feedback',
      modules: ['about'],
      decisions: [feedbackCapability],
      protectedRequest: '/api/feedback/submissions',
    },
    {
      name: 'Feedback capability denied',
      path: '/feedback',
      modules: ['feedback'],
      decisions: [{ capability: 'feedback.submit', state: 'hidden', reasonCode: 'role_missing' }],
      protectedRequest: '/api/feedback/submissions',
    },
  ])('Unauthorized_about_or_feedback_routes_and_api_calls_fail_closed ($name)', async ({ path, modules, decisions, protectedRequest }) => {
    apiMock.mockResolvedValue(feedbackPage([]));
    renderApp(path, workspaceA, modules, decisions);

    await waitFor(() => expect(screen.getByRole('main').textContent).toMatch(/not enabled|turned off|cannot be shown|don't have access/i));
    expect(apiMock).not.toHaveBeenCalledWith(protectedRequest, expect.anything());
    expect(screen.queryByRole('dialog')).toBeNull();
  });
});
