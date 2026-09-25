import React from 'react';
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ResponsiveDataView } from '../../../src/Web/src/components/ResponsiveDataView';

describe('ResponsiveDataView', () => {
  afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

  it('presents the same identified record in a desktop table and labelled compact list', () => {
    render(<ResponsiveDataView items={[{ id: 'one', name: 'Ada' }]} keyOf={item => item.id} label="People"
      renderTable={items => <table><thead><tr><th scope="col">Name</th></tr></thead><tbody>{items.map(item => <tr key={item.id}><th scope="row">{item.name}</th></tr>)}</tbody></table>}
      renderCompact={item => <><strong>{item.name}</strong><button type="button">Open {item.name}</button></>} />);
    expect(screen.getByRole('region', { name: 'People' }).querySelector('table')).toBeTruthy();
    cleanup();
    vi.stubGlobal('matchMedia', () => ({ matches: true, addEventListener: () => {}, removeEventListener: () => {} }));
    render(<ResponsiveDataView items={[{ id: 'one', name: 'Ada' }]} keyOf={item => item.id} label="People"
      renderTable={() => <table />} renderCompact={item => <><strong>{item.name}</strong><button type="button">Open {item.name}</button></>} />);
    expect(screen.getByRole('list', { name: 'People' })).toBeTruthy();
    expect(screen.getByRole('listitem').textContent).toContain('Ada');
    expect(screen.getByRole('button', { name: 'Open Ada' })).toBeTruthy();
  });
});
