import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ModuleUnavailable } from '../../../src/Web/src/components/ModuleUnavailable';

describe('ModuleUnavailable', () => {
  afterEach(cleanup);

  it('offers module settings to managers', () => {
    const onNavigate = vi.fn();
    render(<ModuleUnavailable kind="module-off" moduleName="Exchange" canManageModules onNavigate={onNavigate} />);
    expect(screen.getByText('Exchange is turned off for this workspace')).toBeTruthy();
    fireEvent.click(screen.getByRole('link', { name: 'Open module settings' }));
    expect(onNavigate).toHaveBeenCalledWith('/settings#modules');
    fireEvent.click(screen.getByRole('link', { name: 'Back to overview' }));
    expect(onNavigate).toHaveBeenCalledWith('/overview');
  });

  it('asks non-managers to contact an owner', () => {
    render(<ModuleUnavailable kind="module-off" moduleName="Exchange" canManageModules={false} onNavigate={() => {}} />);
    expect(screen.getByText('Ask a workspace owner to turn it on.')).toBeTruthy();
    expect(screen.queryByText('Open module settings')).toBeNull();
    expect(screen.getByRole('link', { name: 'Back to overview' })).toBeTruthy();
  });

  it('explains missing access with a single h1', () => {
    render(<ModuleUnavailable kind="no-access" moduleName="Licenses" canManageModules onNavigate={() => {}} />);
    expect(screen.getByText("You don't have access to Licenses")).toBeTruthy();
    expect(screen.getAllByRole('heading', { level: 1 })).toHaveLength(1);
    expect(screen.queryByText('Open module settings')).toBeNull();
  });
});
