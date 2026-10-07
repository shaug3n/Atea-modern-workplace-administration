import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ConfirmationDialog } from '../../../src/Web/src/components/ConfirmationDialog';

const base = { title: 'Wipe device', target: 'Laptop 1', proposedChange: 'Erase the device', requiredCapability: 'devices.privileged.manage', onConfirm: vi.fn() };

describe('ConfirmationDialog', () => {
  afterEach(cleanup);

  it('uses an action-specific confirm label gated by phrase and review', () => {
    render(<ConfirmationDialog {...base} confirmLabel="Wipe device" tone="danger" destructivePhrase="WIPE" destructivePhraseLabel="Type WIPE to confirm" consequence="This erases all data." onCancel={vi.fn()} />);
    const confirm = screen.getByRole('button', { name: 'Wipe device' }) as HTMLButtonElement;
    expect(confirm.disabled).toBe(true);
    expect(confirm.className).toContain('button--danger-solid');
    fireEvent.click(screen.getByRole('checkbox'));
    expect(confirm.disabled).toBe(true);
    fireEvent.change(screen.getByLabelText('Type WIPE to confirm'), { target: { value: 'WIPE' } });
    expect(confirm.disabled).toBe(false);
    expect(screen.getByRole('dialog').getAttribute('aria-describedby')).toBeTruthy();
    expect(screen.getByText('This erases all data.')).toBeTruthy();
  });

  it('shows the busy state with the verb label', () => {
    render(<ConfirmationDialog {...base} confirmLabel="Wipe device" busy />);
    expect(screen.getByRole('button', { name: 'Wipe device…' })).toBeTruthy();
  });

  it('shows a human permission name and keeps the raw key inside collapsed technical details', () => {
    const { container } = render(<ConfirmationDialog {...base} requiredCapability="users.update" confirmLabel="Save changes" />);
    expect(screen.getByText('Requires permission')).toBeTruthy();
    expect(screen.getByText('Edit users')).toBeTruthy();
    const details = container.querySelector('details') as HTMLDetailsElement;
    expect(details.open).toBe(false);
    expect(details.textContent).toContain('users.update');
    expect(container.querySelector('dl.detail-list')?.textContent).not.toContain('users.update');
  });

  it('places the checkbox in an inline checkbox field and labels the phrase input', () => {
    const { container } = render(<ConfirmationDialog {...base} destructivePhrase="WIPE" destructivePhraseLabel="Type WIPE to confirm" confirmLabel="Wipe device" />);
    expect(container.querySelector('label.checkbox-field input[type="checkbox"]')).not.toBeNull();
    expect(screen.getByRole('textbox', { name: 'Type WIPE to confirm' })).toBeTruthy();
  });

  it('cancels on Escape unless busy', () => {
    const onCancel = vi.fn();
    const { rerender } = render(<ConfirmationDialog {...base} confirmLabel="Wipe device" onCancel={onCancel} />);
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(onCancel).toHaveBeenCalledTimes(1);
    rerender(<ConfirmationDialog {...base} confirmLabel="Wipe device" onCancel={onCancel} busy />);
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(onCancel).toHaveBeenCalledTimes(1);
  });
});
