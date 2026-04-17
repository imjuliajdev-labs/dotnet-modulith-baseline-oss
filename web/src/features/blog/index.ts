import type { FeatureModuleDefinition } from '../../app/router/featureRegistry';

export const featureDefinition: FeatureModuleDefinition = {
  description: 'Editorial workflow and public publishing surface for the Blog module.',
  label: 'Blog',
  moduleKey: 'blog',
  routePath: '/modules/blog',
  loadScreen: () => import('./BlogFeature').then((module) => ({ default: module.BlogFeature })),
};
