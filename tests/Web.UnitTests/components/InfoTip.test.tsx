import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it } from 'vitest';
import { InfoTip } from '../../../src/Web/src/components/InfoTip';

describe('InfoTip', () => {
  afterEach(cleanup);

  it('opens by keyboard and Escape closes while retaining trigger focus', () => {
    render(<InfoTip label="More information" content={<a href="/help">Read the help</a>} />);
    const trigger = screen.getByRole('button', { name: 'More information' });

    trigger.focus();
    fireEvent.keyDown(trigger, { key: 'Enter' });

    const popover = screen.getByRole('dialog', { name: 'More information' });
    expect(document.activeElement).toBe(trigger);
    expect(screen.getByRole('link', { name: 'Read the help' })).toBeTruthy();

    screen.getByRole('link', { name: 'Read the help' }).focus();
    fireEvent.keyDown(popover, { key: 'Escape' });

    expect(screen.queryByRole('dialog')).toBeNull();
    expect(document.activeElement).toBe(trigger);

    fireEvent.click(trigger);
    expect(screen.getByRole('dialog', { name: 'More information' })).toBeTruthy();
  });
});
