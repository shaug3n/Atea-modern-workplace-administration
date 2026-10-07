import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { CopyValue } from '../../../src/Web/src/components/CopyValue';

const guid = '3f2b8c1e-1111-2222-3333-444455556666';

describe('CopyValue', () => {
  afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

  it('truncates long values visibly while keeping the full value for assistive tech', () => {
    vi.stubGlobal('navigator', { clipboard: { writeText: vi.fn().mockResolvedValue(undefined) } });
    const { container } = render(<CopyValue value={guid} label="Device ID" truncate />);
    expect(container.textContent).toContain('3f2b8c1e…6666');
    expect(container.querySelector('.sr-only')?.textContent).toBe(guid);
  });

  it('copies the full value and announces Copied', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    vi.stubGlobal('navigator', { clipboard: { writeText } });
    render(<CopyValue value={guid} label="Device ID" truncate />);
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Copy Device ID' })); });
    expect(writeText).toHaveBeenCalledWith(guid);
    expect(screen.getByText('Copied')).toBeTruthy();
  });

  it('hides the copy button when the clipboard API is missing', () => {
    vi.stubGlobal('navigator', {});
    render(<CopyValue value={guid} label="Device ID" truncate />);
    expect(screen.queryByRole('button')).toBeNull();
  });

  it('does not throw or announce Copied when writing is rejected', async () => {
    vi.stubGlobal('navigator', { clipboard: { writeText: vi.fn().mockRejectedValue(new Error('denied')) } });
    render(<CopyValue value={guid} label="Device ID" />);
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Copy Device ID' })); });
    expect(screen.queryByText('Copied')).toBeNull();
  });
});
