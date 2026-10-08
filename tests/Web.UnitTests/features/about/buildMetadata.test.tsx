import { describe, expect, it } from 'vitest';
import { readWebBuildMetadata } from '../../../../src/Web/src/features/about/buildMetadata';

describe('readWebBuildMetadata', () => {
  it('preserves the exact supplied web build values', () => {
    expect(readWebBuildMetadata({
      VITE_BUILD_PRODUCT_VERSION: ' 0.1.0 ',
      VITE_BUILD_COMMIT: ' abc123 ',
      VITE_BUILD_BRANCH: ' feature/f6 ',
    })).toEqual({
      productVersion: ' 0.1.0 ',
      commit: ' abc123 ',
      branch: ' feature/f6 ',
    });
  });

  it('labels each missing or whitespace-only build input as unavailable', () => {
    const cases = [
      ['VITE_BUILD_PRODUCT_VERSION', 'productVersion'],
      ['VITE_BUILD_COMMIT', 'commit'],
      ['VITE_BUILD_BRANCH', 'branch'],
    ] as const;
    const base = {
      VITE_BUILD_PRODUCT_VERSION: '0.1.0',
      VITE_BUILD_COMMIT: 'abc123',
      VITE_BUILD_BRANCH: 'main',
    };

    for (const [input, output] of cases) {
      expect(readWebBuildMetadata({ ...base, [input]: undefined })[output]).toBe('Unavailable');
      expect(readWebBuildMetadata({ ...base, [input]: '  \t ' })[output]).toBe('Unavailable');
    }
  });

  it('uses only the supplied build environment instead of looking up runtime Git data', () => {
    const env = {
      VITE_BUILD_PRODUCT_VERSION: '0.1.0',
      VITE_BUILD_COMMIT: 'from-build',
      VITE_BUILD_BRANCH: 'from-build',
    };

    expect(readWebBuildMetadata(env).commit).toBe('from-build');
    expect(readWebBuildMetadata(env).branch).toBe('from-build');
    expect(readWebBuildMetadata.toString()).not.toMatch(/\b(?:git|child_process|execSync|spawnSync)\b/i);
  });
});
