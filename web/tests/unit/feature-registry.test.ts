import { describe, expect, it } from 'vitest';
import { featureRegistry } from '../../src/app/router/featureRegistry';

describe('featureRegistry', () => {
  it('discovers feature manifests with stable module routing and unique keys', () => {
    const moduleKeys = Object.keys(featureRegistry);
    const routePaths = Object.values(featureRegistry).map((feature) => feature.routePath);

    expect(moduleKeys.length).toBeGreaterThan(0);
    expect(new Set(moduleKeys).size).toBe(moduleKeys.length);
    expect(new Set(routePaths).size).toBe(routePaths.length);
    expect(featureRegistry.platform).toBeDefined();
    expect(featureRegistry.platform?.requiredRoles).toEqual(['Admin']);

    if (featureRegistry.admin) {
      expect(featureRegistry.admin.label).toBe('Admin Flight Deck');
    }

    if (featureRegistry.blog) {
      expect(featureRegistry.blog.routePath).toBe('/modules/blog');
    }

    if (featureRegistry['knowledge-base']) {
      expect(featureRegistry['knowledge-base'].routePath).toBe('/knowledge-base');
    }

    if (featureRegistry['sample-feature']) {
      expect(featureRegistry['sample-feature'].routePath).toBe('/modules/sample-feature');
    }
  });
});
