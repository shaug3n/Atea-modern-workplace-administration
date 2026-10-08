import { cleanup, render, screen } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it } from 'vitest';
import { ProvenanceChip } from '../../../src/Web/src/components/ProvenanceChip';

describe('ProvenanceChip', () => {
  afterEach(cleanup);

  it('shows only supplied provenance', () => {
    render(<ProvenanceChip source="Microsoft Graph" explanation={<span>Retrieved from tenant inventory.</span>} />);

    expect(screen.getByText('Microsoft Graph')).toBeTruthy();
    expect(screen.getByText('Retrieved from tenant inventory.')).toBeTruthy();
    expect(screen.queryByText(/verified|fetched at|last checked/i)).toBeNull();
  });
});
