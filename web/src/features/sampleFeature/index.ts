import type { FeatureModuleDefinition } from '../../app/router/featureRegistry';

export const featureDefinition: FeatureModuleDefinition = {
  description: 'Reference feature slice loaded only when its module is active.',
  label: 'Sample Feature',
  moduleKey: 'sample-feature',
  requiredRoles: ['Admin'],
  routePath: '/modules/sample-feature',
  loadScreen: () => import('./SampleFeature').then((module) => ({ default: module.SampleFeature })),
};