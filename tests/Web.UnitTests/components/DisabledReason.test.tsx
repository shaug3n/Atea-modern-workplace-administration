import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it } from 'vitest';
import { DisabledReason } from '../../../src/Web/src/components/DisabledReason';

describe('DisabledReason', () => {
  afterEach(cleanup);

  it('a focused wrapper exposes the reason and source for a disabled control', () => {
    render(
      <DisabledReason reason="This action is unavailable." sourceOfTruth="Managed by the source directory.">
        <button type="button" disabled>Disable user</button>
      </DisabledReason>,
    );

    const child = screen.getByRole('button', { name: 'Disable user' }) as HTMLButtonElement;
    const wrapper = child.parentElement as HTMLElement;
    const tooltipId = wrapper.getAttribute('aria-describedby');

    expect(child.disabled).toBe(true);
    expect(wrapper.tabIndex).toBe(0);
    expect(tooltipId).toBeTruthy();
    expect(screen.getByText('This action is unavailable.')).toBeTruthy();
    expect(wrapper.querySelector('.disabled-reason__source')?.textContent).toContain('Managed by the source directory.');

    wrapper.focus();

    expect(document.activeElement).toBe(wrapper);
    expect(screen.getByRole('tooltip').id).toBe(tooltipId);
    expect(screen.getByRole('tooltip').textContent).toContain('This action is unavailable.');
    expect(screen.getByRole('tooltip').textContent).toContain('Managed by the source directory.');
  });

  it('dismisses the tooltip on Escape and reopens when focus or hover reenters', () => {
    render(
      <DisabledReason reason="This action is unavailable.">
        <button type="button" disabled>Disable user</button>
      </DisabledReason>,
    );

    const child = screen.getByRole('button', { name: 'Disable user' }) as HTMLButtonElement;
    const wrapper = child.parentElement as HTMLElement;
    const tooltip = wrapper.querySelector('[role="tooltip"]') as HTMLElement;

    wrapper.focus();
    fireEvent.keyDown(wrapper, { key: 'Escape' });

    expect(tooltip.hasAttribute('hidden')).toBe(true);
    expect(wrapper.querySelector('.disabled-reason__text')?.textContent).toBe('This action is unavailable.');
    expect(child.disabled).toBe(true);

    fireEvent.blur(wrapper);
    fireEvent.focus(wrapper);
    expect(tooltip.hasAttribute('hidden')).toBe(false);

    fireEvent.keyDown(wrapper, { key: 'Escape' });
    fireEvent.mouseLeave(wrapper);
    fireEvent.mouseEnter(wrapper);
    expect(tooltip.hasAttribute('hidden')).toBe(false);
  });
});
