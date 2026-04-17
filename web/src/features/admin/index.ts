import type { FeatureModuleDefinition } from '../../app/router/featureRegistry';

export const featureDefinition: FeatureModuleDefinition = {
  description: 'Administrative shell surface for privileged operators.',
  label: 'Admin Flight Deck',
  moduleKey: 'admin',
  requiredRoles: ['Admin'],
  routePath: '/modules/admin',
  loadScreen: () => import('./AdminFeature').then((module) => ({ default: module.AdminFeature })),
};