import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ReasonDialog } from '../../../src/Web/src/components/ReasonDialog';

const base = {
  title: 'Disable user',
  target: 'Taylor Morgan',
  proposedChange: 'Disable sign-in',
  requiredCapability: 'users.disable',
  confirmLabel: 'Disable user',
};

describe('ReasonDialog', () => {
  afterEach(cleanup);

  it('whitespace-only reason cannot confirm and valid reason is trimmed', () => {
    const onConfirm = vi.fn();
    render(<ReasonDialog {...base} onConfirm={onConfirm} />);

    const confirm = screen.getByRole('button', { name: 'Disable user' }) as HTMLButtonElement;
    const reason = screen.getByRole('textbox', { name: 'Reason' });
    fireEvent.click(screen.getByRole('checkbox'));
    fireEvent.change(reason, { target: { value: '   \n  ' } });
    expect(confirm.disabled).toBe(true);
    fireEvent.click(confirm);
    expect(onConfirm).not.toHaveBeenCalled();

    fireEvent.change(reason, { target: { value: '  Offboarding complete  ' } });
    expect(confirm.disabled).toBe(false);
    fireEvent.click(confirm);
    expect(onConfirm).toHaveBeenCalledWith('Offboarding complete');
  });

  it('clears the reason when the target changes', () => {
    const onConfirm = vi.fn();
    const { rerender } = render(<ReasonDialog {...base} onConfirm={onConfirm} />);
    const confirm = screen.getByRole('button', { name: 'Disable user' }) as HTMLButtonElement;
    const reason = screen.getByRole('textbox', { name: 'Reason' });

    fireEvent.change(reason, { target: { value: 'Approved for Taylor' } });
    fireEvent.click(screen.getByRole('checkbox'));
    expect(confirm.disabled).toBe(false);

    rerender(<ReasonDialog {...base} target="Jordan Lee" onConfirm={onConfirm} />);

    expect((screen.getByRole('textbox', { name: 'Reason' }) as HTMLTextAreaElement).value).toBe('');
    expect(confirm.disabled).toBe(true);
    fireEvent.click(screen.getByRole('checkbox'));
    expect(confirm.disabled).toBe(true);
    fireEvent.click(confirm);
    expect(onConfirm).not.toHaveBeenCalled();

    fireEvent.change(screen.getByRole('textbox', { name: 'Reason' }), { target: { value: 'Approved for Jordan' } });
    expect(confirm.disabled).toBe(false);
    fireEvent.click(confirm);
    expect(onConfirm).toHaveBeenCalledWith('Approved for Jordan');
  });

  it('shows the required reason and audit notice', () => {
    render(<ReasonDialog {...base} onConfirm={vi.fn()} reasonHint="Include the approved request reference." />);

    expect(screen.getByText('Enter a reason before continuing.')).toBeTruthy();
    expect(screen.getByText('Include the approved request reference.')).toBeTruthy();
    expect(screen.getByText('This action will be written to the platform audit stream without secrets.')).toBeTruthy();
  });

  it('traps keyboard focus and restores it after cancel', () => {
    const onConfirm = vi.fn();
    function DialogHarness() {
      const [open, setOpen] = React.useState(false);
      return (
        <>
          <button type="button" onClick={() => setOpen(true)}>Open dialog</button>
          {open && <ReasonDialog {...base} onConfirm={onConfirm} onCancel={() => setOpen(false)} />}
        </>
      );
    }

    render(<DialogHarness />);
    const trigger = screen.getByRole('button', { name: 'Open dialog' });
    trigger.focus();
    fireEvent.click(trigger);
    const cancel = screen.getByRole('button', { name: 'Cancel' });
    const reason = screen.getByRole('textbox', { name: 'Reason' });
    fireEvent.change(reason, { target: { value: 'Approved request' } });
    fireEvent.click(screen.getByRole('checkbox'));
    const confirm = screen.getByRole('button', { name: 'Disable user' });
    confirm.focus();
    fireEvent.keyDown(confirm, { key: 'Tab' });
    expect(document.activeElement).toBe(reason);

    fireEvent.click(cancel);
    expect(document.activeElement).toBe(trigger);
  });
});
