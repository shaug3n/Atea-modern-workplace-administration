import { cleanup, render } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it } from 'vitest';
import { Icon, type IconName } from '../../../src/Web/src/components/icons';

const names: IconName[] = ['overview', 'users', 'licenses', 'devices', 'mail', 'activity', 'settings', 'bell', 'sun', 'moon', 'lock', 'alert-triangle', 'alert-circle', 'clock', 'copy', 'chevron', 'external', 'menu', 'close', 'check'];

describe('Icon', () => {
  afterEach(cleanup);
  it.each(names)('renders %s as a decorative svg with shapes', (name) => {
    const { container } = render(<Icon name={name} />);
    const svg = container.querySelector('svg[aria-hidden="true"]');
    expect(svg).not.toBeNull();
    expect(svg!.querySelector('path, rect, circle')).not.toBeNull();
  });
});
