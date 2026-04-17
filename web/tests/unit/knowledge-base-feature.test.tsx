import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { KnowledgeBaseFeature } from '../../src/features/knowledge-base/KnowledgeBaseFeature';
import type { useGetCurrentActorQuery } from '../../src/shell/auth/authApi';
import type {
  useCreateKnowledgeEntryMutation,
  useGetKnowledgeBaseManagementSettingsQuery,
  useGetKnowledgeBasePublicSettingsQuery,
  useListKnowledgeEntriesForManagementQuery,
  useListPublishedKnowledgeEntriesQuery,
  usePublishKnowledgeEntryMutation,
  useSetKnowledgeEntryStatusMutation,
  useUpdateKnowledgeEntryMutation,
  useUpdateKnowledgeBaseSettingsMutation,
} from '../../src/features/knowledge-base/knowledgeBaseApi';
import type { KnowledgeBaseManagementSettingsResponse, KnowledgeBasePublicSettingsResponse, KnowledgeEntryListResponse } from '../../src/shared/api/base/contracts';

type GetCurrentActorResult = ReturnType<typeof useGetCurrentActorQuery>;
type GetKnowledgeBaseManagementSettingsResult = ReturnType<typeof useGetKnowledgeBaseManagementSettingsQuery>;
type GetKnowledgeBasePublicSettingsResult = ReturnType<typeof useGetKnowledgeBasePublicSettingsQuery>;
type ListPublishedKnowledgeEntriesResult = ReturnType<typeof useListPublishedKnowledgeEntriesQuery>;
type ListKnowledgeEntriesForManagementResult = ReturnType<typeof useListKnowledgeEntriesForManagementQuery>;
type CreateKnowledgeEntryMutationResult = ReturnType<typeof useCreateKnowledgeEntryMutation>;
type PublishKnowledgeEntryMutationResult = ReturnType<typeof usePublishKnowledgeEntryMutation>;
type SetKnowledgeEntryStatusMutationResult = ReturnType<typeof useSetKnowledgeEntryStatusMutation>;
type UpdateKnowledgeEntryMutationResult = ReturnType<typeof useUpdateKnowledgeEntryMutation>;
type UpdateKnowledgeBaseSettingsMutationResult = ReturnType<typeof useUpdateKnowledgeBaseSettingsMutation>;

const useGetCurrentActorQueryMock = vi.fn<() => GetCurrentActorResult>();
const useGetKnowledgeBaseManagementSettingsQueryMock = vi.fn<(...args: [void?, { skip?: boolean }?]) => GetKnowledgeBaseManagementSettingsResult>();
const useGetKnowledgeBasePublicSettingsQueryMock = vi.fn<(...args: [void?]) => GetKnowledgeBasePublicSettingsResult>();
const useListPublishedKnowledgeEntriesQueryMock = vi.fn<(...args: [void?]) => ListPublishedKnowledgeEntriesResult>();
const useListKnowledgeEntriesForManagementQueryMock = vi.fn<(...args: [void?, { skip?: boolean }?]) => ListKnowledgeEntriesForManagementResult>();
const useCreateKnowledgeEntryMutationMock = vi.fn<() => CreateKnowledgeEntryMutationResult>();
const usePublishKnowledgeEntryMutationMock = vi.fn<() => PublishKnowledgeEntryMutationResult>();
const useSetKnowledgeEntryStatusMutationMock = vi.fn<() => SetKnowledgeEntryStatusMutationResult>();
const useUpdateKnowledgeEntryMutationMock = vi.fn<() => UpdateKnowledgeEntryMutationResult>();
const useUpdateKnowledgeBaseSettingsMutationMock = vi.fn<() => UpdateKnowledgeBaseSettingsMutationResult>();

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

vi.mock('../../src/shell/auth/authApi', () => ({
  useGetCurrentActorQuery: () => useGetCurrentActorQueryMock(),
}));

vi.mock('../../src/features/knowledge-base/knowledgeBaseApi', () => ({
  useCreateKnowledgeEntryMutation: () => useCreateKnowledgeEntryMutationMock(),
  useGetKnowledgeBaseManagementSettingsQuery: (arg?: void, options?: { skip?: boolean }) => useGetKnowledgeBaseManagementSettingsQueryMock(arg, options),
  useGetKnowledgeBasePublicSettingsQuery: (arg?: void) => useGetKnowledgeBasePublicSettingsQueryMock(arg),
  useListKnowledgeEntriesForManagementQuery: (arg?: void, options?: { skip?: boolean }) => useListKnowledgeEntriesForManagementQueryMock(arg, options),
  useListPublishedKnowledgeEntriesQuery: (arg?: void) => useListPublishedKnowledgeEntriesQueryMock(arg),
  usePublishKnowledgeEntryMutation: () => usePublishKnowledgeEntryMutationMock(),
  useSetKnowledgeEntryStatusMutation: () => useSetKnowledgeEntryStatusMutationMock(),
  useUpdateKnowledgeEntryMutation: () => useUpdateKnowledgeEntryMutationMock(),
  useUpdateKnowledgeBaseSettingsMutation: () => useUpdateKnowledgeBaseSettingsMutationMock(),
}));

describe('KnowledgeBaseFeature', () => {
  beforeEach(() => {
    useGetCurrentActorQueryMock.mockReset();
    useGetKnowledgeBaseManagementSettingsQueryMock.mockReset();
    useGetKnowledgeBasePublicSettingsQueryMock.mockReset();
    useListPublishedKnowledgeEntriesQueryMock.mockReset();
    useListKnowledgeEntriesForManagementQueryMock.mockReset();
    useCreateKnowledgeEntryMutationMock.mockReset();
    usePublishKnowledgeEntryMutationMock.mockReset();
    useSetKnowledgeEntryStatusMutationMock.mockReset();
    useUpdateKnowledgeEntryMutationMock.mockReset();
    useUpdateKnowledgeBaseSettingsMutationMock.mockReset();

    useCreateKnowledgeEntryMutationMock.mockReturnValue(createMutationHookResult<CreateKnowledgeEntryMutationResult>(vi.fn()));
    usePublishKnowledgeEntryMutationMock.mockReturnValue(createMutationHookResult<PublishKnowledgeEntryMutationResult>(vi.fn()));
    useSetKnowledgeEntryStatusMutationMock.mockReturnValue(createMutationHookResult<SetKnowledgeEntryStatusMutationResult>(vi.fn()));
    useUpdateKnowledgeEntryMutationMock.mockReturnValue(createMutationHookResult<UpdateKnowledgeEntryMutationResult>(vi.fn()));
    useUpdateKnowledgeBaseSettingsMutationMock.mockReturnValue(createMutationHookResult<UpdateKnowledgeBaseSettingsMutationResult>(vi.fn()));
  });

  it('renders public entries and operator controls when the Admin role is present', () => {
    const entries: KnowledgeEntryListResponse = {
      entries: [
        {
          body: 'Generated contracts should be refreshed from the backend OpenAPI snapshot.',
          category: 'Contracts',
          createdUtc: '2026-04-05T00:00:00Z',
          entryId: '11111111-1111-1111-1111-111111111111',
          featured: true,
          publishedByActorId: 'identity:seeded-admin',
          publishedUtc: '2026-04-05T00:00:00Z',
          slug: 'trust-the-contract',
          sortOrder: 10,
          status: 'published',
          title: 'How do I trust the frontend contract?',
          updatedByActorId: 'identity:seeded-admin',
          updatedUtc: '2026-04-05T00:00:00Z',
          version: 2,
        },
      ],
      nextCursor: null,
    };

    const publicSettings: KnowledgeBasePublicSettingsResponse = {
      publicExperienceBlurb: 'Live module settings drive the public copy.',
      publicExperienceTitle: 'Knowledge Hub',
      searchEnabled: false,
      searchPlaceholder: 'Search governed guidance',
    };

    const managementSettings: KnowledgeBaseManagementSettingsResponse = {
      managementPreviewLimit: 3,
      publicExperienceBlurb: publicSettings.publicExperienceBlurb,
      publicExperienceTitle: publicSettings.publicExperienceTitle,
      searchEnabled: publicSettings.searchEnabled,
      searchPlaceholder: publicSettings.searchPlaceholder,
      updatedByActorId: 'identity:seeded-admin',
      updatedUtc: '2026-04-05T00:00:00Z',
      version: 2,
    };

    useGetCurrentActorQueryMock.mockReturnValue(createQueryResult({
      actorId: 'identity:seeded-admin',
      displayName: 'Baseline Admin',
      roles: ['Admin'],
      userName: 'admin',
    }) as GetCurrentActorResult);
    useGetKnowledgeBasePublicSettingsQueryMock.mockReturnValue(createQueryResult(publicSettings) as GetKnowledgeBasePublicSettingsResult);
    useGetKnowledgeBaseManagementSettingsQueryMock.mockReturnValue(createQueryResult(managementSettings) as GetKnowledgeBaseManagementSettingsResult);
    useListPublishedKnowledgeEntriesQueryMock.mockReturnValue(createQueryResult(entries) as ListPublishedKnowledgeEntriesResult);
    useListKnowledgeEntriesForManagementQueryMock.mockReturnValue(createQueryResult(entries) as ListKnowledgeEntriesForManagementResult);

    render(<KnowledgeBaseFeature />);

    expect(screen.getByRole('heading', { name: 'Knowledge Hub' })).toBeInTheDocument();
    expect(screen.getAllByText('How do I trust the frontend contract?')).toHaveLength(2);
    expect(screen.getByRole('heading', { name: 'Operator controls' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Publish' })).toBeInTheDocument();
    expect(screen.queryByLabelText('Search entries')).not.toBeInTheDocument();
    expect(screen.getByLabelText('Public title')).toHaveValue('Knowledge Hub');
  });

  it('shows version indicator in settings form', () => {
    const publicSettings: KnowledgeBasePublicSettingsResponse = {
      publicExperienceBlurb: 'Live module settings drive the public copy.',
      publicExperienceTitle: 'Knowledge Hub',
      searchEnabled: true,
      searchPlaceholder: 'Search governed guidance',
    };

    const managementSettings: KnowledgeBaseManagementSettingsResponse = {
      managementPreviewLimit: 3,
      publicExperienceBlurb: publicSettings.publicExperienceBlurb,
      publicExperienceTitle: publicSettings.publicExperienceTitle,
      searchEnabled: publicSettings.searchEnabled,
      searchPlaceholder: publicSettings.searchPlaceholder,
      updatedByActorId: 'identity:seeded-admin',
      updatedUtc: '2026-04-05T00:00:00Z',
      version: 5,
    };

    useGetCurrentActorQueryMock.mockReturnValue(createQueryResult({
      actorId: 'identity:seeded-admin',
      displayName: 'Baseline Admin',
      roles: ['Admin'],
      userName: 'admin',
    }) as GetCurrentActorResult);
    useGetKnowledgeBasePublicSettingsQueryMock.mockReturnValue(createQueryResult(publicSettings) as GetKnowledgeBasePublicSettingsResult);
    useGetKnowledgeBaseManagementSettingsQueryMock.mockReturnValue(createQueryResult(managementSettings) as GetKnowledgeBaseManagementSettingsResult);
    useListPublishedKnowledgeEntriesQueryMock.mockReturnValue(createQueryResult({ entries: [], nextCursor: null }) as ListPublishedKnowledgeEntriesResult);
    useListKnowledgeEntriesForManagementQueryMock.mockReturnValue(createQueryResult({ entries: [], nextCursor: null }) as ListKnowledgeEntriesForManagementResult);

    render(<KnowledgeBaseFeature />);

    expect(screen.getByText('Version 5')).toBeInTheDocument();
  });

  it('renders settings save button when the Admin role is present', () => {
    const publicSettings: KnowledgeBasePublicSettingsResponse = {
      publicExperienceBlurb: 'Live module settings drive the public copy.',
      publicExperienceTitle: 'Knowledge Hub',
      searchEnabled: true,
      searchPlaceholder: 'Search governed guidance',
    };

    const managementSettings: KnowledgeBaseManagementSettingsResponse = {
      managementPreviewLimit: 3,
      publicExperienceBlurb: publicSettings.publicExperienceBlurb,
      publicExperienceTitle: publicSettings.publicExperienceTitle,
      searchEnabled: publicSettings.searchEnabled,
      searchPlaceholder: publicSettings.searchPlaceholder,
      updatedByActorId: 'identity:seeded-admin',
      updatedUtc: '2026-04-05T00:00:00Z',
      version: 2,
    };

    useGetCurrentActorQueryMock.mockReturnValue(createQueryResult({
      actorId: 'identity:seeded-admin',
      displayName: 'Baseline Admin',
      roles: ['Admin'],
      userName: 'admin',
    }) as GetCurrentActorResult);
    useGetKnowledgeBasePublicSettingsQueryMock.mockReturnValue(createQueryResult(publicSettings) as GetKnowledgeBasePublicSettingsResult);
    useGetKnowledgeBaseManagementSettingsQueryMock.mockReturnValue(createQueryResult(managementSettings) as GetKnowledgeBaseManagementSettingsResult);
    useListPublishedKnowledgeEntriesQueryMock.mockReturnValue(createQueryResult({ entries: [], nextCursor: null }) as ListPublishedKnowledgeEntriesResult);
    useListKnowledgeEntriesForManagementQueryMock.mockReturnValue(createQueryResult({ entries: [], nextCursor: null }) as ListKnowledgeEntriesForManagementResult);

    render(<KnowledgeBaseFeature />);

    expect(screen.getByRole('button', { name: 'Save settings' })).toBeInTheDocument();
  });
});
