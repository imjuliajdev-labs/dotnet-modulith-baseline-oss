import { startTransition, useState } from 'react';
import { useGetCurrentActorQuery } from '../../shell/auth/authApi';
import {
  useDisableModuleMutation,
  useEnableModuleMutation,
  useGetAuditTrailQuery,
  useGetModuleStatesQuery,
  useGetOperationalHealthQuery,
} from './platformApi';
import { getErrorMessage, isUnauthorizedError } from '../../shared/lib/queryErrors';

function toneForStatus(value: string) {
  if (value === 'healthy' || value === 'enabled') {
    return 'healthy';
  }

  if (value === 'unhealthy' || value === 'transitionfailed' || value === 'disabled') {
    return 'danger';
  }

  return 'warning';
}

export function PlatformConsole() {
  const moduleStateQuery = useGetModuleStatesQuery();
  const healthQuery = useGetOperationalHealthQuery();
  const auditQuery = useGetAuditTrailQuery(20);
  const sessionQuery = useGetCurrentActorQuery();
  const [enableModule] = useEnableModuleMutation();
  const [disableModule] = useDisableModuleMutation();
  const [pendingModuleKey, setPendingModuleKey] = useState<string | null>(null);
  const [mutationError, setMutationError] = useState<string | null>(null);

  const session = isUnauthorizedError(sessionQuery.error) ? null : sessionQuery.data ?? null;
  const canManageModules = session?.roles?.includes('Admin') ?? false;

  async function changeModuleState(moduleKey: string, action: 'enable' | 'disable') {
    setMutationError(null);

    startTransition(() => {
      setPendingModuleKey(moduleKey);
    });

    try {
      if (action === 'enable') {
        await enableModule(moduleKey).unwrap();
      } else {
        await disableModule(moduleKey).unwrap();
      }
    }
    catch (error) {
      setMutationError(getErrorMessage(error));
    }
    finally {
      setPendingModuleKey(null);
    }
  }

  return (
    <div className="platform-grid">
      <section className="feature-panel dashboard-grid__full">
        <div className="feature-panel__header">
          <div>
            <h2 className="feature-panel__title">Platform console</h2>
            <p className="feature-panel__copy">Module transitions, health posture, and recent audit activity flow through the same generated transport contracts as the rest of the shell.</p>
          </div>

          {healthQuery.data ? <span className="pill" data-tone={toneForStatus(healthQuery.data.overallStatus)}>{healthQuery.data.overallStatus}</span> : null}
        </div>

        {mutationError ? <p className="message" data-tone="danger">{mutationError}</p> : null}
      </section>

      <section className="feature-panel">
        <div className="platform-section__header">
          <div>
            <h3 className="platform-section__title">Health posture</h3>
            <p className="platform-section__subcopy">Live operational state from the Platform module.</p>
          </div>
        </div>

        <ul className="shell-list">
          {healthQuery.data?.modules.map((module) => (
            <li key={module.key}>
              <div>
                <div className="page-link">{module.displayName}</div>
                <div className="module-card__meta">runtime: {module.runtimeState}</div>
              </div>
              <span className="pill" data-tone={toneForStatus(module.status)}>{module.status}</span>
            </li>
          )) ?? <li>Loading live health posture.</li>}
        </ul>
      </section>

      <section className="feature-panel">
        <div className="platform-section__header">
          <div>
            <h3 className="platform-section__title">Audit trail</h3>
            <p className="platform-section__subcopy">Recent security-sensitive and operational flows.</p>
          </div>
        </div>

        <div className="audit-grid">
          {auditQuery.data?.events.map((entry) => (
            <article className="audit-entry" key={`${entry.occurredUtc}-${entry.action}-${entry.targetId}`}>
              <div>
                <p className="audit-entry__meta">{entry.moduleKey}</p>
                <h4 className="audit-entry__title">{entry.action}</h4>
              </div>
              <p className="module-card__body">{entry.targetType} {entry.targetId}</p>
              <div className="module-card__actions">
                <span className="pill" data-tone={toneForStatus(entry.outcome)}>{entry.outcome}</span>
                <span className="audit-entry__meta">{new Date(entry.occurredUtc).toLocaleString()}</span>
              </div>
            </article>
          )) ?? <p className="message">Loading the recent audit trail.</p>}
        </div>
      </section>

      <section className="feature-panel platform-grid__full">
        <div className="platform-section__header">
          <div>
            <h3 className="platform-section__title">Module transitions</h3>
            <p className="platform-section__subcopy">Administrative actions stay role-aware and respect live runtime state.</p>
          </div>
          <span className="pill" data-tone={canManageModules ? 'healthy' : 'warning'}>{canManageModules ? 'manage granted' : 'read only'}</span>
        </div>

        <div className="module-grid">
          {moduleStateQuery.data?.modules.map((module) => {
            const enableDisabled = !canManageModules || pendingModuleKey === module.key || module.runtimeState === 'enabled' || module.runtimeState === 'enabling';
            const disableDisabled = !canManageModules || pendingModuleKey === module.key || !module.canBeDisabled || module.runtimeState === 'disabled' || module.runtimeState === 'disabling';

            return (
              <article className="module-card" key={module.key}>
                <div className="module-card__header">
                  <div>
                    <p className="module-card__meta">{module.routePrefix}</p>
                    <h4 className="module-card__title">{module.displayName}</h4>
                  </div>
                  <span className="pill" data-tone={toneForStatus(module.runtimeState)}>{module.runtimeState}</span>
                </div>

                <div className="module-card__body">
                  <span>desired: {module.desiredState}</span>
                  <span>version: {module.version}</span>
                  <span>transition: {module.transitionId ?? 'steady'}</span>
                </div>

                {module.lastErrorCode ? (
                  <p className="module-card__error">{module.lastErrorCode}: {module.lastErrorDetail ?? 'No detail recorded.'}</p>
                ) : null}

                <div className="module-card__actions">
                  <button className="button-ghost" disabled={enableDisabled} type="button" onClick={() => {
                    void changeModuleState(module.key, 'enable');
                  }}>
                    Enable
                  </button>
                  <button className="button" disabled={disableDisabled} type="button" onClick={() => {
                    void changeModuleState(module.key, 'disable');
                  }}>
                    Disable
                  </button>
                </div>
              </article>
            );
          }) ?? <p className="message">Loading module state.</p>}
        </div>
      </section>
    </div>
  );
}