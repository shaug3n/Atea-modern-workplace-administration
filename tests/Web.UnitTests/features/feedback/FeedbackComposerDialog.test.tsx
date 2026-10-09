import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { FeedbackComposerDialog } from '../../../../src/Web/src/features/feedback/FeedbackComposerDialog';

const apiMock = vi.hoisted(() => vi.fn());
vi.mock('../../../../src/Web/src/auth/useApi', () => ({ useApi: () => apiMock }));

function renderDialog() {
  const onOpenChange = vi.fn();
  const onSaved = vi.fn();
  const trigger = document.createElement('button');
  trigger.textContent = 'Open feedback';
  document.body.append(trigger);
  trigger.focus();
  const result = render(
    <FeedbackComposerDialog
      open
      onOpenChange={onOpenChange}
      workspaceId="workspace-1"
      submitterObjectId="user-1"
      onSaved={onSaved}
    />,
  );
  return { ...result, onOpenChange, onSaved, trigger };
}

function fillValidForm(subject = 'Subject', message = 'Message') {
  fireEvent.change(screen.getByLabelText('Category'), { target: { value: 'General' } });
  fireEvent.change(screen.getByLabelText('Subject'), { target: { value: subject } });
  fireEvent.change(screen.getByLabelText('Message'), { target: { value: message } });
}

describe('FeedbackComposerDialog', () => {
  afterEach(() => {
    cleanup();
    document.body.querySelector('button')?.remove();
    apiMock.mockReset();
  });

  it('is an accessible modal, focuses its first field, contains Tab, and restores focus on Escape', async () => {
    const { onOpenChange, trigger, rerender } = renderDialog();
    const dialog = screen.getByRole('dialog');

    expect(dialog.getAttribute('aria-modal')).toBe('true');
    expect(dialog.getAttribute('aria-labelledby')).toBeTruthy();
    expect(document.activeElement).toBe(screen.getByLabelText('Category'));
    fireEvent.keyDown(document.activeElement!, { key: 'Tab', shiftKey: true });
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Save feedback' }));
    fireEvent.keyDown(document.activeElement!, { key: 'Escape' });

    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
    rerender(<FeedbackComposerDialog open={false} onOpenChange={onOpenChange} workspaceId="workspace-1" submitterObjectId="user-1" onSaved={vi.fn()} />);
    await waitFor(() => expect(document.activeElement).toBe(trigger));
  });

  it('requires category, rejects whitespace-only values using server whitespace rules, and enforces exact UTF-16 limits', async () => {
    renderDialog();
    const category = screen.getByLabelText('Category') as HTMLSelectElement;
    const subject = screen.getByLabelText('Subject') as HTMLInputElement;
    const message = screen.getByLabelText('Message') as HTMLTextAreaElement;

    fireEvent.change(subject, { target: { value: '\u0085\u00a0\u2000' } });
    fireEvent.change(message, { target: { value: ' \t\r\n' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save feedback' }));
    expect(screen.getByText('Choose a category before saving.')).toBeTruthy();
    expect(screen.getAllByText(/enter .* before saving/i).length).toBeGreaterThanOrEqual(1);
    expect(apiMock).not.toHaveBeenCalled();

    fireEvent.change(category, { target: { value: 'Bug' } });
    fireEvent.change(subject, { target: { value: '😀'.repeat(60) } });
    fireEvent.change(message, { target: { value: '😀'.repeat(2000) } });
    expect(subject.value).toHaveLength(120);
    expect(message.value).toHaveLength(4000);
    fireEvent.change(subject, { target: { value: `${'😀'.repeat(60)}x` } });
    fireEvent.change(message, { target: { value: `${'😀'.repeat(2000)}x` } });
    fireEvent.click(screen.getByRole('button', { name: 'Save feedback' }));

    expect(await screen.findByText(/subject must be 120 characters or fewer/i)).toBeTruthy();
    expect(screen.getByText(/message must be 4,000 characters or fewer/i)).toBeTruthy();
    expect(apiMock).not.toHaveBeenCalled();
  });

  it('shows the privacy and retention terms before submit and confirms only Saved on success', async () => {
    apiMock.mockResolvedValue(Response.json({ id: 'submission-1', createdAt: '2026-10-09T08:00:00Z', expiresAt: '2027-01-07T08:00:00Z' }, { status: 201 }));
    renderDialog();
    fillValidForm();

    expect(screen.getByText(/do not enter secrets or sensitive tenant or person data/i)).toBeTruthy();
    expect(screen.getByText(/saved in this application/i)).toBeTruthy();
    expect(screen.getByText(/90 days/i)).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Save feedback' }));

    expect((await screen.findByRole('status')).textContent).toBe('Saved');
    expect(apiMock).toHaveBeenCalledOnce();
    expect((screen.getByLabelText('Subject') as HTMLInputElement).value).toBe('');
    expect(apiMock).toHaveBeenCalledWith('/api/feedback/submissions', expect.objectContaining({
      method: 'POST',
      headers: expect.objectContaining({ 'Idempotency-Key': expect.any(String) }),
    }));
  });

  it('accepts subject and message at the exact UTF-16 server limits', async () => {
    apiMock.mockResolvedValue(Response.json({ id: 'submission-1', createdAt: '2026-10-09T08:00:00Z', expiresAt: '2027-01-07T08:00:00Z' }, { status: 201 }));
    renderDialog();
    fillValidForm('😀'.repeat(60), '😀'.repeat(2000));
    fireEvent.click(screen.getByRole('button', { name: 'Save feedback' }));

    await screen.findByRole('status');
    const request = JSON.parse(apiMock.mock.calls[0][1].body);
    expect(request.subject.length).toBe(120);
    expect(request.message.length).toBe(4000);
  });

  it('preserves form values and retries an uncertain request with the same key until the payload changes', async () => {
    apiMock.mockRejectedValueOnce(new TypeError('network offline')).mockResolvedValueOnce(Response.json({
      id: 'submission-1',
      createdAt: '2026-10-09T08:00:00Z',
      expiresAt: '2027-01-07T08:00:00Z',
    }, { status: 201 }));
    renderDialog();
    fillValidForm('Kept subject', 'Kept message');

    fireEvent.click(screen.getByRole('button', { name: 'Save feedback' }));
    expect((await screen.findByRole('alert')).textContent).toMatch(/could not be saved/i);
    expect((screen.getByLabelText('Subject') as HTMLInputElement).value).toBe('Kept subject');
    expect((screen.getByLabelText('Message') as HTMLTextAreaElement).value).toBe('Kept message');
    fireEvent.click(screen.getByRole('button', { name: 'Save feedback' }));
    await screen.findByRole('status');
    const firstKey = apiMock.mock.calls[0][1].headers['Idempotency-Key'];
    expect(apiMock.mock.calls[1][1].headers['Idempotency-Key']).toBe(firstKey);
  });

  it('starts a new retry key after any field edit', async () => {
    apiMock.mockRejectedValueOnce(new TypeError('network offline')).mockRejectedValueOnce(new TypeError('network offline'));
    renderDialog();
    fillValidForm();
    fireEvent.click(screen.getByRole('button', { name: 'Save feedback' }));
    await screen.findByRole('alert');
    const firstKey = apiMock.mock.calls[0][1].headers['Idempotency-Key'];
    fireEvent.change(screen.getByLabelText('Message'), { target: { value: 'Edited message' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save feedback' }));
    await waitFor(() => expect(apiMock).toHaveBeenCalledTimes(2));

    expect(apiMock.mock.calls[1][1].headers['Idempotency-Key']).not.toBe(firstKey);
  });

  it('resets form state when the authenticated workspace or submitter changes', async () => {
    const { rerender, onOpenChange, onSaved } = renderDialog();
    fillValidForm('Private draft', 'Draft message');
    rerender(<FeedbackComposerDialog open onOpenChange={onOpenChange} workspaceId="workspace-2" submitterObjectId="user-2" onSaved={onSaved} />);

    await waitFor(() => expect((screen.getByLabelText('Subject') as HTMLInputElement).value).toBe(''));
    expect((screen.getByLabelText('Message') as HTMLTextAreaElement).value).toBe('');
  });

  it('replaces an expired key only for an explicit retry and treats changed-payload conflicts as failures', async () => {
    apiMock
      .mockResolvedValueOnce(Response.json({ error: 'idempotency_key_expired' }, { status: 409 }))
      .mockResolvedValueOnce(Response.json({ error: 'idempotency_key_reused' }, { status: 409 }));
    renderDialog();
    fillValidForm();
    fireEvent.click(screen.getByRole('button', { name: 'Save feedback' }));
    expect((await screen.findByRole('alert')).textContent).toMatch(/retry to save/i);
    expect(apiMock).toHaveBeenCalledTimes(1);
    const expiredKey = apiMock.mock.calls[0][1].headers['Idempotency-Key'];
    fireEvent.click(screen.getByRole('button', { name: 'Save feedback' }));
    expect((await screen.findByRole('alert')).textContent).toMatch(/already used with different details/i);
    expect(apiMock.mock.calls[1][1].headers['Idempotency-Key']).not.toBe(expiredKey);
    expect(screen.queryByRole('status')).toBeNull();
    expect((screen.getByLabelText('Subject') as HTMLInputElement).value).toBe('Subject');
  });

  it('does not report success when a successful HTTP response contains invalid JSON', async () => {
    apiMock.mockResolvedValue(new Response('not json', { status: 201 }));
    renderDialog();
    fillValidForm();
    fireEvent.click(screen.getByRole('button', { name: 'Save feedback' }));

    expect((await screen.findByRole('alert')).textContent).toMatch(/could not be saved/i);
    expect(screen.queryByRole('status')).toBeNull();
    expect((screen.getByLabelText('Subject') as HTMLInputElement).value).toBe('Subject');
  });

  it('disables repeated submission while the request is pending', async () => {
    let resolveRequest!: (response: Response) => void;
    apiMock.mockImplementation(() => new Promise<Response>(resolve => { resolveRequest = resolve; }));
    renderDialog();
    fillValidForm();
    const submit = screen.getByRole('button', { name: 'Save feedback' });
    fireEvent.click(submit);

    await waitFor(() => expect((submit as HTMLButtonElement).disabled).toBe(true));
    fireEvent.click(submit);
    expect(apiMock).toHaveBeenCalledOnce();
    resolveRequest(Response.json({ id: 'submission-1', createdAt: '2026-10-09T08:00:00Z', expiresAt: '2027-01-07T08:00:00Z' }, { status: 201 }));
    await screen.findByRole('status');
  });
});
