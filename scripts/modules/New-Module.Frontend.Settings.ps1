function New-FrontendSettingsTagsContent {
    $settingsTagTypeName = "${moduleName}Settings"
    $fanoutTagTypes = @($spec['settingsFanoutTags'])
    $dependentReadTagTypes = if ($fanoutTagTypes.Count -eq 0) {
        @($settingsTagTypeName)
    }
    else {
        $fanoutTagTypes
    }
    $fanoutTagEntries = if ($fanoutTagTypes.Count -eq 0) {
        ''
    }
    else {
        ($fanoutTagTypes | ForEach-Object { "    '" + [string]$_ + "'," }) -join "`r`n"
    }
    $dependentReadTagEntries = ($dependentReadTagTypes | ForEach-Object { "    '" + [string]$_ + "'," }) -join "`r`n"

@"
export const ${moduleName}SettingsTags = {
  fanout: [
$fanoutTagEntries
  ] as const,
  settings: '$settingsTagTypeName',
} as const;

export function get${moduleName}SettingsTagTypes() {
  return [${moduleName}SettingsTags.settings, ...${moduleName}SettingsTags.fanout] as const;
}

export type ${moduleName}SettingsTagType = ReturnType<typeof get${moduleName}SettingsTagTypes>[number];

export function get${moduleName}SettingsInvalidationTags() {
  return [${moduleName}SettingsTags.settings, ...${moduleName}SettingsTags.fanout] as const;
}

export function get${moduleName}SettingsDependentReadTags() {
  return [
$dependentReadTagEntries
  ] as const;
}
"@
}

function New-FrontendSettingsApiContent {
@"
import { starterApi } from '../../shared/api/base/starterBaseApi';
import type { paths } from '../../shared/api/generated/contracts.generated';
import {
  ${moduleName}SettingsTags,
  get${moduleName}SettingsInvalidationTags,
  get${moduleName}SettingsTagTypes,
} from './${moduleSettingsTagsFileName}';

type ${moduleName}SettingsRoute = Extract<'/api/v1$([string]$spec['routePrefix'])/settings', keyof paths>;
export type ${moduleName}SettingsResponse = [${moduleName}SettingsRoute] extends [never]
  ? Record<string, unknown>
  : paths[${moduleName}SettingsRoute]['get']['responses'][200]['content']['application/json'];
export type ${moduleName}SettingsUpdateRequest = [${moduleName}SettingsRoute] extends [never]
  ? Record<string, unknown>
  : paths[${moduleName}SettingsRoute]['put']['requestBody']['content']['application/json'];

export type ${moduleName}SettingsEditor = {
  expectedVersion: number;
  operatorSummary: string;
  previewLimit: number;
  updatedByActorId: string;
  updatedUtc: string;
};

function readNumber(source: Record<string, unknown>, propertyName: string, fallbackValue: number): number {
  const value = source[propertyName];
  return typeof value === 'number' && Number.isFinite(value) ? value : fallbackValue;
}

function readString(source: Record<string, unknown>, propertyName: string, fallbackValue: string): string {
  const value = source[propertyName];
  return typeof value === 'string' ? value : fallbackValue;
}

export function create${moduleName}SettingsEditor(settings: ${moduleName}SettingsResponse): ${moduleName}SettingsEditor {
  const source: Record<string, unknown> = settings;

  return {
    expectedVersion: readNumber(source, 'version', 0),
    operatorSummary: readString(source, 'operatorSummary', ''),
    previewLimit: readNumber(source, 'previewLimit', 10),
    updatedByActorId: readString(source, 'updatedByActorId', 'unknown'),
    updatedUtc: readString(source, 'updatedUtc', ''),
  };
}

export function to${moduleName}SettingsUpdateRequest(editor: ${moduleName}SettingsEditor): ${moduleName}SettingsUpdateRequest {
  return {
    expectedVersion: editor.expectedVersion,
    operatorSummary: editor.operatorSummary,
    previewLimit: editor.previewLimit,
  };
}

const ${moduleName}SettingsTagTypes = get${moduleName}SettingsTagTypes();

export const ${moduleName}SettingsApi = starterApi.enhanceEndpoints({
  addTagTypes: [...${moduleName}SettingsTagTypes],
}).injectEndpoints({
  endpoints: (builder) => ({
    get${moduleName}Settings: builder.query<${moduleName}SettingsResponse, void>({
      providesTags: [${moduleName}SettingsTags.settings],
      query: () => '/api/v1$([string]$spec['routePrefix'])/settings',
    }),
    update${moduleName}Settings: builder.mutation<${moduleName}SettingsResponse, ${moduleName}SettingsUpdateRequest>({
      invalidatesTags: [...get${moduleName}SettingsInvalidationTags()],
      query: (body) => ({
        body,
        method: 'PUT',
        url: '/api/v1$([string]$spec['routePrefix'])/settings',
      }),
    }),
  }),
});

export const {
  useGet${moduleName}SettingsQuery,
  useUpdate${moduleName}SettingsMutation,
} = ${moduleName}SettingsApi;
"@
}

function New-FrontendSettingsProjectionContent {
@"
import {
  ${moduleName}SettingsApi,
  create${moduleName}SettingsEditor,
  type ${moduleName}SettingsResponse,
} from './${moduleSettingsApiFileName}';
import { get${moduleName}SettingsDependentReadTags } from './${moduleSettingsTagsFileName}';

export type ${moduleName}SettingsProjection = {
  operatorSummaryLine: string;
  previewLimitLine: string;
  version: number;
};

export function to${moduleName}SettingsProjection(settings: ${moduleName}SettingsResponse): ${moduleName}SettingsProjection {
  const editor = create${moduleName}SettingsEditor(settings);

  return {
    operatorSummaryLine: 'Operator summary: ' + editor.operatorSummary,
    previewLimitLine: 'Preview limit: ' + editor.previewLimit,
    version: editor.expectedVersion,
  };
}

export const ${moduleName}SettingsProjectionApi = ${moduleName}SettingsApi.injectEndpoints({
  endpoints: (builder) => ({
    get${moduleName}SettingsProjection: builder.query<${moduleName}SettingsProjection, void>({
      providesTags: [...get${moduleName}SettingsDependentReadTags()],
      query: () => '/api/v1$([string]$spec['routePrefix'])/settings',
      transformResponse: (response: ${moduleName}SettingsResponse) => to${moduleName}SettingsProjection(response),
    }),
  }),
});

export const {
  useGet${moduleName}SettingsProjectionQuery,
} = ${moduleName}SettingsProjectionApi;
"@
}

function New-FrontendSettingsFeatureTestContent {
@"
import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ${moduleFeatureComponentTypeName} } from '../../src/features/$moduleKey/${moduleFeatureComponentTypeName}';
import type { useGet${moduleName}SettingsProjectionQuery } from '../../src/features/$moduleKey/${moduleSettingsProjectionFileName}';
import type { useGet${moduleName}SettingsQuery, useUpdate${moduleName}SettingsMutation } from '../../src/features/$moduleKey/${moduleName}SettingsApi';

type Get${moduleName}SettingsProjectionResult = ReturnType<typeof useGet${moduleName}SettingsProjectionQuery>;
type Get${moduleName}SettingsResult = ReturnType<typeof useGet${moduleName}SettingsQuery>;
type Update${moduleName}SettingsMutationResult = ReturnType<typeof useUpdate${moduleName}SettingsMutation>;

const useGet${moduleName}SettingsProjectionQueryMock = vi.fn<() => Get${moduleName}SettingsProjectionResult>();
const useGet${moduleName}SettingsQueryMock = vi.fn<() => Get${moduleName}SettingsResult>();
const useUpdate${moduleName}SettingsMutationMock = vi.fn<() => Update${moduleName}SettingsMutationResult>();

function createQueryResult<T>(data: T) {
    return {
        currentData: data,
        data,
        error: undefined,
        isError: false,
        isFetching: false,
        isLoading: false,
        isSuccess: true,
        isUninitialized: false,
        refetch: vi.fn(),
        status: 'fulfilled',
    };
}

function createMutationHookResult<TMutationResult>(mutation: ReturnType<typeof vi.fn>): TMutationResult {
    return [
        mutation,
        {
            error: undefined,
            isError: false,
            isLoading: false,
            isSuccess: false,
            isUninitialized: true,
            reset: vi.fn(),
            status: 'uninitialized',
        },
    ] as TMutationResult;
}

vi.mock('../../src/shared/lib/queryErrors', () => ({
    getErrorMessage: () => 'Generated scaffold error',
}));

vi.mock('../../src/features/$moduleKey/${moduleSettingsProjectionFileName}', () => ({
    useGet${moduleName}SettingsProjectionQuery: () => useGet${moduleName}SettingsProjectionQueryMock(),
}));

vi.mock('../../src/features/$moduleKey/${moduleName}SettingsApi', () => ({
    create${moduleName}SettingsEditor: vi.fn((settings: Record<string, unknown>) => ({
        expectedVersion: Number(settings['version'] ?? 0),
        operatorSummary: typeof settings['operatorSummary'] === 'string' ? settings['operatorSummary'] : '',
        previewLimit: Number(settings['previewLimit'] ?? 0),
        updatedByActorId: typeof settings['updatedByActorId'] === 'string' ? settings['updatedByActorId'] : 'unknown',
        updatedUtc: typeof settings['updatedUtc'] === 'string' ? settings['updatedUtc'] : '',
    })),
    to${moduleName}SettingsUpdateRequest: vi.fn((editor: { expectedVersion: number; operatorSummary: string; previewLimit: number }) => ({
        expectedVersion: editor.expectedVersion,
        operatorSummary: editor.operatorSummary,
        previewLimit: editor.previewLimit,
    })),
    useGet${moduleName}SettingsQuery: () => useGet${moduleName}SettingsQueryMock(),
    useUpdate${moduleName}SettingsMutation: () => useUpdate${moduleName}SettingsMutationMock(),
}));

describe('${moduleFeatureComponentTypeName}', () => {
    beforeEach(() => {
        useGet${moduleName}SettingsProjectionQueryMock.mockReset();
        useGet${moduleName}SettingsQueryMock.mockReset();
        useUpdate${moduleName}SettingsMutationMock.mockReset();

        useGet${moduleName}SettingsProjectionQueryMock.mockReturnValue(createQueryResult({
            operatorSummaryLine: 'Operator summary: Scaffolded operator summary',
            previewLimitLine: 'Preview limit: 5',
            version: 2,
        }) as Get${moduleName}SettingsProjectionResult);
        useGet${moduleName}SettingsQueryMock.mockReturnValue(createQueryResult({
            operatorSummary: 'Scaffolded operator summary',
            previewLimit: 5,
            updatedByActorId: 'identity:operator',
            updatedUtc: '2026-04-05T12:00:00Z',
            version: 2,
        }) as Get${moduleName}SettingsResult);

        useUpdate${moduleName}SettingsMutationMock.mockReturnValue(createMutationHookResult<Update${moduleName}SettingsMutationResult>(vi.fn()));
    });

    it('renders the scaffolded settings flow and save action', () => {
        render(<${moduleFeatureComponentTypeName} />);

        expect(screen.getByRole('heading', { name: '$moduleDisplayName' })).toBeInTheDocument();
        expect(screen.getByRole('heading', { name: 'Operational settings shell' })).toBeInTheDocument();
        expect(screen.getByLabelText('Operator summary')).toHaveValue('Scaffolded operator summary');
        expect(screen.getByRole('button', { name: 'Save scaffolded settings' })).toBeInTheDocument();
        expect(screen.getByText('Dependent preview: Operator summary: Scaffolded operator summary | Preview limit: 5')).toBeInTheDocument();
    });
});
"@
}

function New-FrontendSettingsTagsTestContent {
    $settingsTagTypeName = "${moduleName}Settings"
    $fanoutTagTypes = @($spec['settingsFanoutTags'])
    $allTagTypes = @($settingsTagTypeName) + $fanoutTagTypes
    $dependentReadTagTypes = if ($fanoutTagTypes.Count -eq 0) {
        @($settingsTagTypeName)
    }
    else {
        $fanoutTagTypes
    }

    $fanoutTagEntries = ($fanoutTagTypes | ForEach-Object { "            '" + [string]$_ + "'" }) -join ",`r`n"
    $allTagEntries = ($allTagTypes | ForEach-Object { "            '" + [string]$_ + "'" }) -join ",`r`n"
    $dependentReadTagEntries = ($dependentReadTagTypes | ForEach-Object { "            '" + [string]$_ + "'" }) -join ",`r`n"

@"
import { describe, expect, it } from 'vitest';
import {
    ${moduleName}SettingsTags,
    get${moduleName}SettingsDependentReadTags,
    get${moduleName}SettingsInvalidationTags,
    get${moduleName}SettingsTagTypes,
} from '../../src/features/$moduleKey/${moduleSettingsTagsFileName}';

describe('${moduleName}SettingsTags', () => {
    it('keeps the generated settings tag helpers aligned with the module spec', () => {
        expect(${moduleName}SettingsTags.settings).toBe('$settingsTagTypeName');
        expect([...${moduleName}SettingsTags.fanout]).toEqual([
$fanoutTagEntries
        ]);
        expect([...get${moduleName}SettingsTagTypes()]).toEqual([
$allTagEntries
        ]);
        expect([...get${moduleName}SettingsInvalidationTags()]).toEqual([
$allTagEntries
        ]);
        expect([...get${moduleName}SettingsDependentReadTags()]).toEqual([
$dependentReadTagEntries
        ]);
    });
});
"@
}

function New-FrontendSettingsApiTestContent {
@"
import { configureStore } from '@reduxjs/toolkit';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { resetAntiforgeryTokenCache } from '../../src/shared/api/base/starterBaseQuery';
import { ${moduleName}SettingsProjectionApi } from '../../src/features/$moduleKey/${moduleSettingsProjectionFileName}';

type Mutable${moduleName}SettingsResponse = {
    operatorSummary: string;
    previewLimit: number;
    updatedByActorId: string;
    updatedUtc: string;
    version: number;
};

function createStore() {
    return configureStore({
        reducer: {
            [${moduleName}SettingsProjectionApi.reducerPath]: ${moduleName}SettingsProjectionApi.reducer,
        },
        middleware: (getDefaultMiddleware) => getDefaultMiddleware().concat(${moduleName}SettingsProjectionApi.middleware),
    });
}

function createJsonResponse(body: unknown, init: ResponseInit = {}) {
    return new Response(JSON.stringify(body), {
        headers: {
            'content-type': 'application/json',
            ...(init.headers ?? {}),
        },
        status: init.status ?? 200,
    });
}

describe('${moduleName}SettingsProjectionApi', () => {
    beforeEach(() => {
        resetAntiforgeryTokenCache();
    });

    afterEach(() => {
        resetAntiforgeryTokenCache();
        vi.restoreAllMocks();
        vi.unstubAllGlobals();
    });

    it('invalidates the dependent projection when settings change', async () => {
        const store = createStore();
        let settingsGetRequestCount = 0;
        let lastUpdateRequest: { expectedVersion: number; operatorSummary: string; previewLimit: number } | null = null;
        let currentSettingsResponse: Mutable${moduleName}SettingsResponse = {
            operatorSummary: 'Scaffolded operator summary',
            previewLimit: 5,
            updatedByActorId: 'identity:operator',
            updatedUtc: '2026-04-05T12:00:00Z',
            version: 2,
        };

        const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
            const requestUrl = typeof input === 'string'
                ? input
                : input instanceof URL
                    ? input.toString()
                    : input.url;
            const requestMethod = (init?.method ?? (input instanceof Request ? input.method : 'GET')).toUpperCase();
            const requestBody = typeof init?.body === 'string'
                ? init.body
                : input instanceof Request
                    ? await input.clone().text()
                    : '{}';

            if (requestUrl.endsWith('/api/v1/identity/antiforgery')) {
                return createJsonResponse({
                    headerName: 'X-CSRF-TOKEN',
                    requestToken: 'token-123',
                });
            }

            if (requestUrl.endsWith('/api/v1$([string]$spec['routePrefix'])/settings') && requestMethod === 'GET') {
                settingsGetRequestCount += 1;
                return createJsonResponse(currentSettingsResponse);
            }

            if (requestUrl.endsWith('/api/v1$([string]$spec['routePrefix'])/settings') && requestMethod === 'PUT') {
                lastUpdateRequest = JSON.parse(requestBody) as {
                    expectedVersion: number;
                    operatorSummary: string;
                    previewLimit: number;
                };

                currentSettingsResponse = {
                    operatorSummary: 'Updated from scaffolded unit-test anchor',
                    previewLimit: 6,
                    updatedByActorId: 'identity:operator',
                    updatedUtc: '2026-04-05T12:05:00Z',
                    version: 3,
                };

                return createJsonResponse(currentSettingsResponse);
            }

            throw new Error('Unhandled fetch ' + requestMethod + ' ' + requestUrl);
        });

        vi.stubGlobal('fetch', fetchMock);

        const projectionSubscription = store.dispatch(
            ${moduleName}SettingsProjectionApi.endpoints.get${moduleName}SettingsProjection.initiate(),
        );

        await projectionSubscription.unwrap();

        expect(settingsGetRequestCount).toBe(1);
        expect(${moduleName}SettingsProjectionApi.endpoints.get${moduleName}SettingsProjection.select()(store.getState()).data).toEqual({
            operatorSummaryLine: 'Operator summary: Scaffolded operator summary',
            previewLimitLine: 'Preview limit: 5',
            version: 2,
        });

        const updateRequest = {
            expectedVersion: 2,
            operatorSummary: 'Updated from scaffolded unit-test anchor',
            previewLimit: 6,
        };

        const mutationResult = await store.dispatch(
            ${moduleName}SettingsProjectionApi.endpoints.update${moduleName}Settings.initiate(updateRequest),
        ).unwrap();

        expect(mutationResult.version).toBe(3);

        await Promise.all(store.dispatch(${moduleName}SettingsProjectionApi.util.getRunningQueriesThunk()));

        expect(lastUpdateRequest).toEqual(updateRequest);
        expect(settingsGetRequestCount).toBeGreaterThan(1);
        expect(${moduleName}SettingsProjectionApi.endpoints.get${moduleName}SettingsProjection.select()(store.getState()).data).toEqual({
            operatorSummaryLine: 'Operator summary: Updated from scaffolded unit-test anchor',
            previewLimitLine: 'Preview limit: 6',
            version: 3,
        });

        projectionSubscription.unsubscribe();
        store.dispatch(${moduleName}SettingsProjectionApi.util.resetApiState());
    });
});
"@
}

function New-FrontendSettingsE2eContent {
@"
import { expect, test } from '@playwright/test';

type ParsedSettingsUpdateRequest = {
    expectedVersion: number | null;
    operatorSummary: string | null;
    previewLimit: number | null;
};

function parseSettingsUpdateRequest(requestBody: string | null): ParsedSettingsUpdateRequest {
    if (!requestBody) {
        return {
            expectedVersion: null,
            operatorSummary: null,
            previewLimit: null,
        };
    }

    const parsed: unknown = JSON.parse(requestBody);
    if (!parsed || typeof parsed !== 'object') {
        return {
            expectedVersion: null,
            operatorSummary: null,
            previewLimit: null,
        };
    }

    const record = parsed as Record<string, unknown>;
    return {
        expectedVersion: typeof record.expectedVersion === 'number' ? record.expectedVersion : null,
        operatorSummary: typeof record.operatorSummary === 'string' ? record.operatorSummary : null,
        previewLimit: typeof record.previewLimit === 'number' ? record.previewLimit : null,
    };
}

test('the scaffolded $moduleDisplayName route renders its managed settings shell', async ({ page }) => {
    let lastUpdateRequest: ParsedSettingsUpdateRequest | null = null;
    let settingsGetRequestCount = 0;
    let currentSettingsResponse = {
        operatorSummary: 'Scaffolded operator summary',
        previewLimit: 5,
        updatedByActorId: 'identity:operator',
        updatedUtc: '2026-04-05T12:00:00Z',
        version: 2,
    };

    await page.route('**/api/v1/identity/antiforgery', async (route) => {
        await route.fulfill({
            body: JSON.stringify({
                headerName: 'X-CSRF-TOKEN',
                requestToken: 'token-123',
            }),
            contentType: 'application/json',
            headers: {
                'set-cookie': '.AspNetCore.Antiforgery=token-123; path=/; samesite=lax'
            },
            status: 200,
        });
    });

    await page.route('**/api/v1/identity/me', async (route) => {
        await route.fulfill({
            body: JSON.stringify({
                actorId: 'identity:operator',
                displayName: 'Scaffold Operator',
                permissionSnapshotVersion: 'snapshot-1',
                permissions: ['$moduleKey.read', '$moduleKey.manage'],
                userName: 'operator'
            }),
            contentType: 'application/json',
            status: 200,
        });
    });

    await page.route('**/api/v1/identity/me/preferences/time-zone', async (route) => {
        await route.fulfill({
            body: JSON.stringify({
                preferredTimeZoneId: 'Etc/UTC',
            }),
            contentType: 'application/json',
            status: 200,
        });
    });

    await page.route('**/api/v1/platform/bootstrap', async (route) => {
        await route.fulfill({
            body: JSON.stringify({
                modules: [
                    {
                        key: '$moduleKey',
                        displayName: '$moduleDisplayName',
                        routePrefix: '$([string]$spec['routePrefix'])',
                        schemaName: '$([string]$spec['schemaName'])',
                        moduleNamespace: '$moduleNamespace',
                        defaultEnabled: $defaultEnabledLiteral,
                        canBeDisabled: $canBeDisabledLiteral,
                        hasFrontendSurface: $hasFrontendSurfaceLiteral
                    }
                ],
                permissions: []
            }),
            contentType: 'application/json',
            status: 200,
        });
    });

    await page.route('**/api/v1/platform/modules', async (route) => {
        await route.fulfill({
            body: JSON.stringify({
                modules: [
                    {
                        key: '$moduleKey',
                        displayName: '$moduleDisplayName',
                        routePrefix: '$([string]$spec['routePrefix'])',
                        defaultEnabled: $defaultEnabledLiteral,
                        canBeDisabled: $canBeDisabledLiteral,
                        desiredState: 'enabled',
                        runtimeState: 'enabled',
                        version: 2,
                        transitionId: null,
                        updatedUtc: '2026-04-05T12:00:00Z',
                        updatedByActorId: 'identity:operator',
                        lastErrorCode: null,
                        lastErrorDetail: null,
                        changes: []
                    }
                ]
            }),
            contentType: 'application/json',
            status: 200,
        });
    });

    await page.route('**/api/v1$([string]$spec['routePrefix'])/settings', async (route) => {
        if (route.request().method() === 'PUT') {
            lastUpdateRequest = parseSettingsUpdateRequest(route.request().postData());
            currentSettingsResponse = {
                operatorSummary: 'Updated from scaffolded Playwright anchor',
                previewLimit: 6,
                updatedByActorId: 'identity:operator',
                updatedUtc: '2026-04-05T12:05:00Z',
                version: 3,
            };

            await route.fulfill({
                body: JSON.stringify(currentSettingsResponse),
                contentType: 'application/json',
                status: 200,
            });

            return;
        }

        settingsGetRequestCount += 1;

        await route.fulfill({
            body: JSON.stringify(currentSettingsResponse),
            contentType: 'application/json',
            status: 200,
        });
    });

    await page.goto('/modules/$moduleKey');

    await expect(page.getByRole('heading', { name: '$moduleDisplayName' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Operational settings shell' })).toBeVisible();
    await expect(page.getByLabel('Operator summary')).toHaveValue('Scaffolded operator summary');
    await expect(page.getByText('Dependent preview: Operator summary: Scaffolded operator summary | Preview limit: 5')).toBeVisible();

    const initialSettingsGetRequestCount = settingsGetRequestCount;

    await page.getByLabel('Operator summary').fill('Updated from scaffolded Playwright anchor');
    await page.getByLabel('Preview limit').fill('6');
    await page.getByRole('button', { name: 'Save scaffolded settings' }).click();

    await expect(page.getByLabel('Operator summary')).toHaveValue('Updated from scaffolded Playwright anchor');
    await expect(page.getByLabel('Preview limit')).toHaveValue('6');
    await expect(page.getByText('Dependent preview: Operator summary: Updated from scaffolded Playwright anchor | Preview limit: 6')).toBeVisible();
    await expect(page.getByText(/Version 3 \| Updated by identity:operator/)).toBeVisible();
    await expect.poll(() => settingsGetRequestCount > initialSettingsGetRequestCount).toBe(true);
    await expect.poll(() => lastUpdateRequest?.expectedVersion).toBe(2);
    await expect.poll(() => lastUpdateRequest?.operatorSummary).toBe('Updated from scaffolded Playwright anchor');
    await expect.poll(() => lastUpdateRequest?.previewLimit).toBe(6);
});
"@
}

