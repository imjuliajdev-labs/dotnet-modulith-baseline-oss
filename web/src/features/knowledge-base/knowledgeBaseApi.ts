import { starterApi } from '../../shared/api/base/starterBaseApi';
import { createIdempotencyKey } from '../../shared/api/base/idempotencyKeys';
import type {
  CreateKnowledgeEntryRequest,
  KnowledgeEntryListResponse,
  KnowledgeEntryResponse,
  KnowledgeBaseManagementSettingsResponse,
  KnowledgeBasePublicSettingsResponse,
  PublishKnowledgeEntryRequest,
  UpdateKnowledgeEntryRequest,
  UpdateKnowledgeBaseSettingsRequest,
  UpdateKnowledgeEntryStatusRequest,
} from '../../shared/api/base/contracts';

export const knowledgeBaseApi = starterApi.injectEndpoints({
  endpoints: (builder) => ({
    createKnowledgeEntry: builder.mutation<KnowledgeEntryResponse, CreateKnowledgeEntryRequest>({
      invalidatesTags: ['KnowledgeBaseEntries', 'KnowledgeBaseSettings'],
      query: (body) => ({
        body,
        method: 'POST',
        url: '/api/v1/knowledge-base/manage/entries',
      }),
    }),
    getKnowledgeBaseManagementSettings: builder.query<KnowledgeBaseManagementSettingsResponse, void>({
      providesTags: ['KnowledgeBaseSettings'],
      query: () => '/api/v1/knowledge-base/manage/settings',
    }),
    getKnowledgeBasePublicSettings: builder.query<KnowledgeBasePublicSettingsResponse, void>({
      providesTags: ['KnowledgeBaseSettings'],
      query: () => '/api/v1/knowledge-base/settings',
    }),
    listKnowledgeEntriesForManagement: builder.query<KnowledgeEntryListResponse, void>({
      providesTags: ['KnowledgeBaseEntries'],
      query: () => '/api/v1/knowledge-base/manage/entries',
    }),
    listPublishedKnowledgeEntries: builder.query<KnowledgeEntryListResponse, void>({
      providesTags: ['KnowledgeBaseEntries'],
      query: () => '/api/v1/knowledge-base/entries',
    }),
    publishKnowledgeEntry: builder.mutation<KnowledgeEntryResponse, { body: PublishKnowledgeEntryRequest; entryId: string }>({
      invalidatesTags: ['KnowledgeBaseEntries', 'KnowledgeBaseSettings'],
      query: ({ body, entryId }) => ({
        body,
        headers: {
          'Idempotency-Key': createIdempotencyKey(),
        },
        method: 'PUT',
        url: `/api/v1/knowledge-base/manage/entries/${entryId}/publish`,
      }),
    }),
    setKnowledgeEntryStatus: builder.mutation<KnowledgeEntryResponse, { body: UpdateKnowledgeEntryStatusRequest; entryId: string }>({
      invalidatesTags: ['KnowledgeBaseEntries', 'KnowledgeBaseSettings'],
      query: ({ body, entryId }) => ({
        body,
        method: 'PUT',
        url: `/api/v1/knowledge-base/manage/entries/${entryId}/status`,
      }),
    }),
    updateKnowledgeEntry: builder.mutation<KnowledgeEntryResponse, { body: UpdateKnowledgeEntryRequest; entryId: string }>({
      invalidatesTags: ['KnowledgeBaseEntries', 'KnowledgeBaseSettings'],
      query: ({ body, entryId }) => ({
        body,
        method: 'PUT',
        url: `/api/v1/knowledge-base/manage/entries/${entryId}`,
      }),
    }),
    updateKnowledgeBaseSettings: builder.mutation<KnowledgeBaseManagementSettingsResponse, UpdateKnowledgeBaseSettingsRequest>({
      invalidatesTags: ['KnowledgeBaseEntries', 'KnowledgeBaseSettings'],
      query: (body) => ({
        body,
        method: 'PUT',
        url: '/api/v1/knowledge-base/manage/settings',
      }),
    }),
  }),
});

export const {
  useCreateKnowledgeEntryMutation,
  useGetKnowledgeBaseManagementSettingsQuery,
  useGetKnowledgeBasePublicSettingsQuery,
  useListKnowledgeEntriesForManagementQuery,
  useListPublishedKnowledgeEntriesQuery,
  usePublishKnowledgeEntryMutation,
  useSetKnowledgeEntryStatusMutation,
  useUpdateKnowledgeEntryMutation,
  useUpdateKnowledgeBaseSettingsMutation,
} = knowledgeBaseApi;