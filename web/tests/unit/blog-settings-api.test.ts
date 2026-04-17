import { configureStore } from '@reduxjs/toolkit';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { resetAntiforgeryTokenCache } from '../../src/shared/api/base/starterBaseQuery';
import { BlogSettingsProjectionApi } from '../../src/features/blog/BlogSettingsProjection';

type MutableBlogSettingsResponse = {
    operatorSummary: string;
    previewLimit: number;
    updatedByActorId: string;
    updatedUtc: string;
    version: number;
};

function createStore() {
    return configureStore({
        reducer: {
            [BlogSettingsProjectionApi.reducerPath]: BlogSettingsProjectionApi.reducer,
        },
        middleware: (getDefaultMiddleware) => getDefaultMiddleware().concat(BlogSettingsProjectionApi.middleware),
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

describe('BlogSettingsProjectionApi', () => {
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
        let currentSettingsResponse: MutableBlogSettingsResponse = {
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

            if (requestUrl.endsWith('/api/v1/blog/settings') && requestMethod === 'GET') {
                settingsGetRequestCount += 1;
                return createJsonResponse(currentSettingsResponse);
            }

            if (requestUrl.endsWith('/api/v1/blog/settings') && requestMethod === 'PUT') {
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
            BlogSettingsProjectionApi.endpoints.getBlogSettingsProjection.initiate(),
        );

        await projectionSubscription.unwrap();

        expect(settingsGetRequestCount).toBe(1);
        expect(BlogSettingsProjectionApi.endpoints.getBlogSettingsProjection.select()(store.getState()).data).toEqual({
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
            BlogSettingsProjectionApi.endpoints.updateBlogSettings.initiate(updateRequest),
        ).unwrap();

        expect(mutationResult.version).toBe(3);

        await Promise.all(store.dispatch(BlogSettingsProjectionApi.util.getRunningQueriesThunk()));

        expect(lastUpdateRequest).toEqual(updateRequest);
        expect(settingsGetRequestCount).toBeGreaterThan(1);
        expect(BlogSettingsProjectionApi.endpoints.getBlogSettingsProjection.select()(store.getState()).data).toEqual({
            operatorSummaryLine: 'Operator summary: Updated from scaffolded unit-test anchor',
            previewLimitLine: 'Preview limit: 6',
            version: 3,
        });

        projectionSubscription.unsubscribe();
        store.dispatch(BlogSettingsProjectionApi.util.resetApiState());
    });
});
