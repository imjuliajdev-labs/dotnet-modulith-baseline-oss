function New-FrontendFeatureManifestContent {
@"
import type { FeatureModuleDefinition } from '../../app/router/featureRegistry';

export const featureDefinition: FeatureModuleDefinition = {
  description: 'Scaffolded shell route for the $moduleDisplayName module.',
  label: '$moduleDisplayName',
  moduleKey: '$moduleKey',
  routePath: '/modules/$moduleKey',
  loadScreen: () => import('./${moduleFeatureComponentTypeName}').then((module) => ({ default: module.${moduleFeatureComponentTypeName} })),
};
"@
}

function New-FrontendFeatureComponentContent {
        $settingsImports = if ([bool]$spec['hasOperatorManagedSettings']) {
@"
import { type FormEvent, useEffect, useState } from 'react';
import { getErrorMessage } from '../../shared/lib/queryErrors';
import { useGet${moduleName}SettingsProjectionQuery } from './${moduleSettingsProjectionFileName}';
import {
    create${moduleName}SettingsEditor,
    to${moduleName}SettingsUpdateRequest,
    useGet${moduleName}SettingsQuery,
    useUpdate${moduleName}SettingsMutation,
    type ${moduleName}SettingsEditor,
} from './${moduleSettingsApiFileName}';
"@
        }
        else {
                ''
        }

        $settingsState = if ([bool]$spec['hasOperatorManagedSettings']) {
@"
    const settingsQuery = useGet${moduleName}SettingsQuery();
    const settingsProjectionQuery = useGet${moduleName}SettingsProjectionQuery();
    const [updateSettings, updateSettingsState] = useUpdate${moduleName}SettingsMutation();
    const [settingsEditor, setSettingsEditor] = useState<${moduleName}SettingsEditor | null>(null);
    const [settingsMutationError, setSettingsMutationError] = useState<string | null>(null);

    useEffect(() => {
        if (settingsQuery.data) {
            setSettingsEditor(create${moduleName}SettingsEditor(settingsQuery.data));
        }
    }, [settingsQuery.data]);

    async function onSettingsSubmit(event: FormEvent<HTMLFormElement>) {
        event.preventDefault();
        if (!settingsEditor) {
            return;
        }

        setSettingsMutationError(null);

        try {
            const response = await updateSettings(to${moduleName}SettingsUpdateRequest(settingsEditor)).unwrap();
            setSettingsEditor(create${moduleName}SettingsEditor(response));
        }
        catch (error) {
            setSettingsMutationError(getErrorMessage(error));
        }
    }
"@
        }
        else {
                ''
        }

        $settingsPanel = if ([bool]$spec['hasOperatorManagedSettings']) {
                $managedSettingItems = if (@($spec['operatorManagedSettings']).Count -eq 0) {
                    '          <li><span>No operator-managed settings were declared in the module spec yet.</span><span className="pill">spec-driven</span></li>'
                }
                else {
                    $managedSettingLines = foreach ($managedSetting in @($spec['operatorManagedSettings'])) {
                        $pillLabel = if ([bool]$managedSetting['reloadSafe']) {
                            [string]$managedSetting['type'] + ' | reload-safe'
                        }
                        else {
                            [string]$managedSetting['type']
                        }

                        '          <li><span>{''' + [string]$managedSetting['displayName'] + '''}</span><span className="pill">' + $pillLabel + '</span></li>'
                    }

                    $managedSettingLines -join "`r`n"
                }

@"

            <section className="feature-panel">
                <div className="feature-panel__header">
                    <div>
                        <h3 className="feature-panel__title">Operational settings shell</h3>
                        <p className="feature-panel__copy">This scaffolded panel is wired to the generated settings hooks. Refresh the HTTP contracts after the module endpoint shape changes so the hook types tighten from their scaffold fallback.</p>
                    </div>
                    <span className="pill" data-tone="warning">operator-managed settings</span>
                </div>

                {settingsMutationError ? <p className="message" data-tone="danger">{settingsMutationError}</p> : null}
                {settingsQuery.isLoading && !settingsEditor ? <p className="message">Loading scaffolded runtime settings.</p> : null}

                <form className="shell-form" onSubmit={(event) => { void onSettingsSubmit(event); }}>
                    <label className="field">
                        <span className="field__label">Operator summary</span>
                        <input
                            className="field__input"
                            onChange={(event) => setSettingsEditor((current) => current ? { ...current, operatorSummary: event.target.value } : current)}
                            value={settingsEditor?.operatorSummary ?? ''}
                        />
                    </label>

                    <label className="field">
                        <span className="field__label">Preview limit</span>
                        <input
                            className="field__input"
                            min={1}
                            onChange={(event) => setSettingsEditor((current) => current ? { ...current, previewLimit: Number(event.target.value) || 0 } : current)}
                            type="number"
                            value={settingsEditor?.previewLimit ?? 0}
                        />
                    </label>

                    <div className="feature-panel__header">
                        <p className="module-card__meta">
                            Version {settingsEditor?.expectedVersion ?? 0} | Updated by {settingsEditor?.updatedByActorId ?? 'unknown'}
                        </p>
                        <button className="button" disabled={!settingsEditor || updateSettingsState.isLoading} type="submit">
                            {updateSettingsState.isLoading ? 'Saving...' : 'Save scaffolded settings'}
                        </button>
                    </div>
                </form>

                <ul className="shell-list">
$managedSettingItems
                </ul>

                {settingsProjectionQuery.isLoading && !settingsProjectionQuery.data ? <p className="message">Loading the scaffolded dependent settings preview.</p> : null}
                {settingsProjectionQuery.data ? (
                    <p className="message">
                        Dependent preview: {settingsProjectionQuery.data.operatorSummaryLine} | {settingsProjectionQuery.data.previewLimitLine}
                    </p>
                ) : null}

                {settingsProjectionQuery.error ? <p className="message" data-tone="danger">The scaffolded dependent settings preview failed. Check the feature-owned fanout tags before wiring real dependent reads.</p> : null}
                {settingsQuery.error ? <p className="message" data-tone="danger">The scaffolded settings query failed. Refresh generated contracts after the module endpoint is live, then retry.</p> : null}
            </section>
"@
        }
        else {
                ''
        }

@"
$settingsImports
export function ${moduleFeatureComponentTypeName}() {
$settingsState
    return (
        <div className="dashboard-grid">
            <section className="feature-panel dashboard-grid__full">
                <div className="feature-panel__header">
                    <div>
                        <h2 className="feature-panel__title">$moduleDisplayName</h2>
                        <p className="feature-panel__copy">This scaffolded feature shell is auto-discovered from the module feature manifest and only becomes meaningful as the module’s browser behavior arrives.</p>
                    </div>
                    <span className="pill" data-tone="warning">scaffolded shell</span>
                </div>

                <ul className="shell-list">
                    <li>
                        <span>The route is lazy-loaded through the governed feature manifest contract.</span>
                        <span className="pill">lazy route</span>
                    </li>
                    <li>
                        <span>Real UI behavior should stay inside this feature directory as the module grows.</span>
                        <span className="pill">feature-owned</span>
                    </li>
                </ul>
            </section>$settingsPanel
        </div>
    );
}
"@
}

function New-FrontendRealtimeContent {
@"
export const realtimeFeatureKey = "$moduleKey";
"@
}

