import type { FeatureModuleDefinition } from '../../app/router/featureRegistry';

export const featureDefinition: FeatureModuleDefinition = {
  description: 'Public knowledge hub with FAQ-style entries and operator-managed publishing controls.',
  label: 'Knowledge Base',
  moduleKey: 'knowledge-base',
  routePath: '/knowledge-base',
  loadScreen: () => import('./KnowledgeBaseFeature').then((module) => ({ default: module.KnowledgeBaseFeature })),
};
