import type { FeatureModuleDefinition } from '../../app/router/featureRegistry';

export const featureDefinition: FeatureModuleDefinition = {
  description: 'Live module state, health, and audit operations.',
  label: 'Platform Console',
  moduleKey: 'platform',
  requiredRoles: ['Admin'],
  routePath: '/modules/platform',
  loadScreen: () => import('./PlatformConsole').then((module) => ({ default: module.PlatformConsole })),
};