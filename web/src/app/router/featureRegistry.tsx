import { lazy, type ComponentType, type LazyExoticComponent } from 'react';

export type FeatureModuleDefinition = {
  description: string;
  label: string;
  moduleKey: string;
  requiredRoles?: string[];
  routePath: string;
  loadScreen: () => Promise<{ default: ComponentType }>;
};

export type FeatureDefinition = Omit<FeatureModuleDefinition, 'loadScreen'> & {
  screen: LazyExoticComponent<ComponentType>;
};

type FeatureManifestModule = {
  featureDefinition?: FeatureModuleDefinition;
};

export const featureRegistry = buildFeatureRegistry(
  import.meta.glob<FeatureManifestModule>('../../features/**/index.ts', { eager: true }),
);

function buildFeatureRegistry(featureModules: Record<string, FeatureManifestModule>): Record<string, FeatureDefinition> {
  const registry = new Map<string, FeatureDefinition>();

  for (const [modulePath, featureModule] of Object.entries(featureModules)) {
    const featureDefinition = featureModule.featureDefinition;
    if (!featureDefinition) {
      continue;
    }

    if (registry.has(featureDefinition.moduleKey)) {
      throw new Error(`Duplicate frontend feature manifest for module '${featureDefinition.moduleKey}' at '${modulePath}'.`);
    }

    registry.set(featureDefinition.moduleKey, {
      description: featureDefinition.description,
      label: featureDefinition.label,
      moduleKey: featureDefinition.moduleKey,
      requiredRoles: featureDefinition.requiredRoles,
      routePath: featureDefinition.routePath,
      screen: lazy(featureDefinition.loadScreen),
    });
  }

  return Object.fromEntries(
    [...registry.entries()].sort(([left], [right]) => left.localeCompare(right, 'en', { sensitivity: 'base' })),
  );
}