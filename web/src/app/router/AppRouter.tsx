import { Suspense, useEffect } from 'react';
import { BrowserRouter, Navigate, Route, Routes } from 'react-router';
import { AppShell } from '../layout/AppShell';
import { StartupScreen } from '../layout/StartupScreen';
import { AuthPanel } from '../../shell/auth/AuthPanel';
import { useGetCurrentActorQuery } from '../../shell/auth/authApi';
import {
  useGetBootstrapManifestQuery,
  useGetModuleStatesQuery,
} from '../../features/platform/platformApi';
import { reportBrowserFault } from '../../shared/telemetry/browserTelemetry';
import { getErrorMessage, isUnauthorizedError } from '../../shared/lib/queryErrors';
import { FeatureErrorBoundary } from '../../shared/ui/FeatureErrorBoundary';
import { featureRegistry } from './featureRegistry';

function HomeScreen() {
  const bootstrapQuery = useGetBootstrapManifestQuery();
  const moduleStateQuery = useGetModuleStatesQuery();

  if (!bootstrapQuery.data || !moduleStateQuery.data) {
    return null;
  }

  const statesByKey = new Map(moduleStateQuery.data.modules.map((module) => [module.key, module]));

  return (
    <div className="dashboard-grid">
      <section className="shell-card dashboard-grid__full">
        <div className="shell-card__header">
          <div>
            <h2 className="shell-card__title">Module directory</h2>
            <p className="shell-card__subcopy">The frontend shell consumes generated contracts and only advertises features whose modules are currently live.</p>
          </div>
        </div>

        <div className="module-grid">
          {bootstrapQuery.data.modules.map((module) => {
            const state = statesByKey.get(module.key);
            const runtimeState = state?.runtimeState ?? 'disabled';

            return (
              <article className="module-card" key={module.key}>
                <div className="module-card__header">
                  <div>
                    <p className="module-card__meta">{module.moduleNamespace}</p>
                    <h3 className="module-card__title">{module.displayName}</h3>
                  </div>

                  <span className="pill" data-tone={runtimeState === 'enabled' ? 'healthy' : runtimeState === 'disabled' ? 'warning' : 'danger'}>
                    {runtimeState}
                  </span>
                </div>

                <div className="module-card__body">
                  <span>Backend route prefix: {module.routePrefix}</span>
                  <span>Schema: {module.schemaName}</span>
                  <span>Default enabled: {module.defaultEnabled ? 'yes' : 'no'}</span>
                </div>
              </article>
            );
          })}
        </div>
      </section>
    </div>
  );
}

export function AppRouter() {
  const bootstrapQuery = useGetBootstrapManifestQuery();
  const moduleStateQuery = useGetModuleStatesQuery();
  const sessionQuery = useGetCurrentActorQuery();

  useEffect(() => {
    if (bootstrapQuery.error) {
      reportBrowserFault('shell.bootstrap_failed', {
        boundary: 'bootstrap-manifest',
        detail: getErrorMessage(bootstrapQuery.error),
      });
    }

    if (moduleStateQuery.error) {
      reportBrowserFault('shell.bootstrap_failed', {
        boundary: 'module-state',
        detail: getErrorMessage(moduleStateQuery.error),
      });
    }
  }, [bootstrapQuery.error, moduleStateQuery.error]);

  if ((bootstrapQuery.isLoading && !bootstrapQuery.data) || (moduleStateQuery.isLoading && !moduleStateQuery.data)) {
    return (
      <BrowserRouter>
        <StartupScreen detail="Reading the platform bootstrap manifest and the live module-state ledger." title="Bootstrapping the shell." />
      </BrowserRouter>
    );
  }

  if (bootstrapQuery.error || moduleStateQuery.error || !bootstrapQuery.data || !moduleStateQuery.data) {
    const message = bootstrapQuery.error ? getErrorMessage(bootstrapQuery.error) : moduleStateQuery.error ? getErrorMessage(moduleStateQuery.error) : 'The bootstrap manifest is unavailable.';

    return (
      <BrowserRouter>
        <StartupScreen actionLabel="Retry bootstrap" detail={message} onAction={() => {
          void bootstrapQuery.refetch();
          void moduleStateQuery.refetch();
        }} title="The shell could not trust its startup contract." />
      </BrowserRouter>
    );
  }

  const session = isUnauthorizedError(sessionQuery.error) ? null : sessionQuery.data ?? null;
  const roles = new Set(session?.roles ?? []);
  const statesByKey = new Map(moduleStateQuery.data.modules.map((module) => [module.key, module]));

  const navigation = bootstrapQuery.data.modules
    .map((module) => {
      const feature = featureRegistry[module.key];
      const state = statesByKey.get(module.key);
      if (!feature || !state || state.runtimeState === 'disabled') {
        return null;
      }

      if (feature.requiredRoles && !feature.requiredRoles.some((role) => roles.has(role))) {
        return null;
      }

      return {
        description: feature.description,
        label: feature.label,
        to: feature.routePath,
      };
    })
    .filter((entry): entry is { description: string; label: string; to: string } => entry !== null);

  const highlightedModules = (
    <section className="shell-card">
      <div className="shell-card__header">
        <div>
          <h2 className="shell-card__title">Active feature surface</h2>
          <p className="shell-card__subcopy">Feature routes are discovered from the Platform manifest and filtered by live module state plus role visibility.</p>
        </div>
      </div>

      <ul className="feature-list">
        {navigation.length === 0 ? (
          <li>
            <span>No feature routes are currently visible for this browser session.</span>
            <span className="pill" data-tone="warning">restricted</span>
          </li>
        ) : (
          navigation.map((entry) => (
            <li key={entry.to}>
              <div>
                <div className="page-link">{entry.label}</div>
                <div className="module-card__meta">{entry.description}</div>
              </div>
              <span className="pill" data-tone="healthy">live</span>
            </li>
          ))
        )}
      </ul>
    </section>
  );

  const stats = (
    <>
      <article className="stat-card">
        <span className="stat-card__label">registered modules</span>
        <p className="stat-card__value">{bootstrapQuery.data.modules.length}</p>
      </article>
      <article className="stat-card">
        <span className="stat-card__label">active routes</span>
        <p className="stat-card__value">{navigation.length}</p>
      </article>
      <article className="stat-card">
        <span className="stat-card__label">session mode</span>
        <p className="stat-card__value">{session ? 'cookie' : 'anonymous'}</p>
      </article>
    </>
  );

  return (
    <BrowserRouter>
      <AppShell authPanel={<AuthPanel />} highlightedModules={highlightedModules} navigation={navigation} stats={stats}>
        <Routes>
          <Route element={<HomeScreen />} path="/" />
          {navigation.map((entry) => {
            const feature = Object.values(featureRegistry).find((candidate) => candidate.routePath === entry.to);

            if (!feature) {
              return null;
            }

            const Screen = feature.screen;

            return (
              <Route
                element={
                  <FeatureErrorBoundary featureLabel={entry.label}>
                    <Suspense fallback={<StartupScreen detail={`Loading the ${entry.label} feature bundle.`} title={`Opening ${entry.label}.`} />}>
                      <Screen />
                    </Suspense>
                  </FeatureErrorBoundary>
                }
                key={entry.to}
                path={entry.to}
              />
            );
          })}
          <Route element={<Navigate replace to="/" />} path="*" />
        </Routes>
      </AppShell>
    </BrowserRouter>
  );
}