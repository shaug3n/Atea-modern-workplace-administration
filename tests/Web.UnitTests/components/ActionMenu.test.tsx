import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ActionMenu } from '../../../src/Web/src/components/ActionMenu';

describe('ActionMenu', () => {
  afterEach(cleanup);

  it('opens from a button, closes on Escape, and returns focus to the trigger', () => {
    render(<ActionMenu label="Actions" items={[{ label: 'Revoke sessions', onSelect: vi.fn() }]} />);
    const trigger = screen.getByRole('button', { name: 'Actions' });

    fireEvent.click(trigger);
    expect(screen.getByRole('menu')).toBeTruthy();
    fireEvent.keyDown(screen.getByRole('menu'), { key: 'Escape' });

    expect(screen.queryByRole('menu')).toBeNull();
    expect(document.activeElement).toBe(trigger);
  });

  it('closes after invoking an action and only disables its busy item', () => {
    const onSelect = vi.fn();
    render(<ActionMenu label="Actions" items={[
      { label: 'Issue TAP', onSelect, busy: true },
      { label: 'Revoke sessions', onSelect },
    ]} />);

    fireEvent.click(screen.getByRole('button', { name: 'Actions' }));
    expect((screen.getByRole('menuitem', { name: 'Issue TAP' }) as HTMLButtonElement).disabled).toBe(true);
    fireEvent.click(screen.getByRole('menuitem', { name: 'Revoke sessions' }));

    expect(onSelect).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole('menu')).toBeNull();
  });

  it('dismisses from outside pointer selection, reports the state change, and returns focus to the trigger', () => {
    const onOpenChange = vi.fn();
    render(<><ActionMenu label="Actions" onOpenChange={onOpenChange} items={[{ label: 'Revoke sessions', onSelect: vi.fn() }]} /><button type="button">Outside</button></>);
    const trigger = screen.getByRole('button', { name: 'Actions' });

    fireEvent.click(trigger);
    expect(onOpenChange).toHaveBeenLastCalledWith(true);
    fireEvent.pointerDown(screen.getByRole('button', { name: 'Outside' }));

    expect(screen.queryByRole('menu')).toBeNull();
    expect(onOpenChange).toHaveBeenLastCalledWith(false);
    expect(document.activeElement).toBe(trigger);
  });

  it('keeps menu items keyboard reachable as native buttons', () => {
    render(<ActionMenu label="Actions" items={[{ label: 'Revoke sessions', onSelect: vi.fn() }]} />);
    fireEvent.click(screen.getByRole('button', { name: 'Actions' }));

    const item = screen.getByRole('menuitem', { name: 'Revoke sessions' });
    expect(item.tagName).toBe('BUTTON');
    expect(item.getAttribute('tabindex')).not.toBe('-1');
  });
});
