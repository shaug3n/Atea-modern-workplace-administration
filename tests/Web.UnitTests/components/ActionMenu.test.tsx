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

  it('focuses_first_non_busy_item_when_opened', () => {
    render(<ActionMenu label="Actions" items={[
      { label: 'Loading action', onSelect: vi.fn(), busy: true },
      { label: 'First available', onSelect: vi.fn() },
    ]} />);
    const trigger = screen.getByRole('button', { name: 'Actions' });
    fireEvent.click(trigger);
    expect(document.activeElement).toBe(screen.getByRole('menuitem', { name: 'First available' }));
  });

  it('opens_from_trigger_arrow_keys', () => {
    const items = [{ label: 'First', onSelect: vi.fn() }, { label: 'Last', onSelect: vi.fn() }];
    const { unmount } = render(<ActionMenu label="Actions" items={items} />);
    const trigger = screen.getByRole('button', { name: 'Actions' });
    fireEvent.keyDown(trigger, { key: 'ArrowDown' });
    expect(document.activeElement).toBe(screen.getByRole('menuitem', { name: 'First' }));
    unmount();

    render(<ActionMenu label="Actions" items={items} />);
    fireEvent.keyDown(screen.getByRole('button', { name: 'Actions' }), { key: 'ArrowUp' });
    expect(document.activeElement).toBe(screen.getByRole('menuitem', { name: 'Last' }));
  });

  it('wraps_arrow_navigation_and_supports_home_end', () => {
    render(<ActionMenu label="Actions" items={[
      { label: 'First', onSelect: vi.fn() },
      { label: 'Busy', onSelect: vi.fn(), busy: true },
      { label: 'Unavailable', onSelect: vi.fn(), disabled: true },
      { label: 'Last', onSelect: vi.fn() },
    ]} />);
    fireEvent.click(screen.getByRole('button', { name: 'Actions' }));
    const first = screen.getByRole('menuitem', { name: 'First' });
    const unavailable = screen.getByRole('menuitem', { name: 'Unavailable' });
    const last = screen.getByRole('menuitem', { name: 'Last' });

    expect(document.activeElement).toBe(first);
    expect(fireEvent.keyDown(first, { key: 'ArrowDown' })).toBe(false);
    expect(document.activeElement).toBe(unavailable);
    fireEvent.keyDown(unavailable, { key: 'ArrowDown' });
    expect(document.activeElement).toBe(last);
    fireEvent.keyDown(last, { key: 'ArrowDown' });
    expect(document.activeElement).toBe(first);
    fireEvent.keyDown(first, { key: 'ArrowUp' });
    expect(document.activeElement).toBe(last);
    fireEvent.keyDown(last, { key: 'Home' });
    expect(document.activeElement).toBe(first);
    fireEvent.keyDown(first, { key: 'End' });
    expect(document.activeElement).toBe(last);
  });

  it('keeps_disabled_items_inert_and_handles_empty_and_all_busy_menus', () => {
    const onSelect = vi.fn();
    render(<ActionMenu label="Actions" items={[
      { label: 'Unavailable', onSelect, disabled: true },
      { label: 'Busy', onSelect, busy: true },
    ]} />);
    const trigger = screen.getByRole('button', { name: 'Actions' });
    fireEvent.click(trigger);
    const unavailable = screen.getByRole('menuitem', { name: 'Unavailable' });
    expect(document.activeElement).toBe(unavailable);
    fireEvent.click(unavailable);
    expect(onSelect).not.toHaveBeenCalled();
    fireEvent.keyDown(screen.getByRole('menu'), { key: 'Escape' });
    expect(screen.queryByRole('menu')).toBeNull();
    expect(document.activeElement).toBe(trigger);

    const busyMenu = render(<ActionMenu label="Busy actions" items={[{ label: 'Loading action', onSelect, busy: true }]} />);
    const busyTrigger = screen.getByRole('button', { name: 'Busy actions' });
    fireEvent.click(busyTrigger);
    expect(screen.getByRole('menu', { name: 'Busy actions' })).toBeTruthy();
    expect(document.activeElement).toBe(busyTrigger);
    fireEvent.keyDown(screen.getByRole('menu', { name: 'Busy actions' }), { key: 'Escape' });
    expect(screen.queryByRole('menu', { name: 'Busy actions' })).toBeNull();
    expect(document.activeElement).toBe(busyTrigger);
    busyMenu.unmount();

    const { unmount } = render(<ActionMenu label="Empty actions" items={[]} />);
    const emptyTrigger = screen.getByRole('button', { name: 'Empty actions' });
    fireEvent.click(emptyTrigger);
    expect(screen.getByRole('menu')).toBeTruthy();
    expect(document.activeElement).toBe(emptyTrigger);
    fireEvent.keyDown(screen.getByRole('menu'), { key: 'Escape' });
    expect(screen.queryByRole('menu')).toBeNull();
    expect(document.activeElement).toBe(emptyTrigger);
    unmount();
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

  it('names the trigger from ariaLabel, shows descriptions and separators, and keeps disabled items inert but described', () => {
    const onSelect = vi.fn();
    render(<ActionMenu label="Actions" ariaLabel="Actions for Ada" items={[
      { label: 'Open details', onSelect },
      { label: 'Disable user', onSelect, danger: true, separatorBefore: true, disabled: true, description: 'Requires permission: Disable users' },
    ]} />);
    fireEvent.click(screen.getByRole('button', { name: 'Actions for Ada' }));
    expect(screen.getByText('Requires permission: Disable users')).toBeTruthy();
    expect(screen.getAllByRole('separator')).toHaveLength(1);
    const disabled = screen.getByRole('menuitem', { name: 'Disable user' });
    expect(disabled.getAttribute('aria-disabled')).toBe('true');
    expect(disabled.getAttribute('aria-describedby')).toBeTruthy();
    fireEvent.click(disabled);
    expect(onSelect).not.toHaveBeenCalled();
  });
});
