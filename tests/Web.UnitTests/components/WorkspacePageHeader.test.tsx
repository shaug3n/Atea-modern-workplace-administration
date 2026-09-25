import { cleanup, render, screen } from '@testing-library/react';
import React from 'react';
import { afterEach, expect, it } from 'vitest';
import { WorkspacePageHeader } from '../../../src/Web/src/components/WorkspacePageHeader';

afterEach(cleanup);

it('places the page title before a named primary action', () => {
  const { container } = render(<WorkspacePageHeader title="Users" eyebrow="People" description="Manage your users" actions={<button type="button">Add user</button>}>Selected users</WorkspacePageHeader>);

  expect(screen.getByRole('heading', { level: 1, name: 'Users' })).toBeTruthy();
  expect(screen.getByRole('button', { name: 'Add user' })).toBeTruthy();
  expect(container.querySelector('h1')?.compareDocumentPosition(screen.getByRole('button', { name: 'Add user' })) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  expect(screen.getByText('Manage your users')).toBeTruthy();
  expect(screen.getByText('Selected users')).toBeTruthy();
});
