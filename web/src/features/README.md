# features

Feature-owned frontend slices live here.

Each feature folder owns its manifest, screens, RTK Query wrappers, and any feature-local browser orchestration that sits on top of the shared frontend infrastructure.

Every immediate child directory must contain an `index.ts` feature manifest (BP-017). App-shell infrastructure — auth panel, layout, error boundary, anything that is always loaded rather than lazy — belongs under `web/src/shell/`, not here. `FeaturesDirectoryMustContainOnlyManifestedFeatures` enforces this.
