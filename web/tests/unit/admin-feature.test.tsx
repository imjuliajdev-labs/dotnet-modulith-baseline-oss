import { act, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AdminFeature } from '../../src/features/admin/AdminFeature';
import type { useGetCurrentActorQuery } from '../../src/shell/auth/authApi';
import type {
  useCreateIdentityUserMutation,
  useListAdminAnnouncementsQuery,
  useListAdminGuidanceQuery,
  useListIdentityUsersQuery,
  useResetIdentityUserPasswordMutation,
  useRevokeIdentityUserSessionsMutation,
  useUpdateIdentityUserRolesMutation,
  useUpdateIdentityUserStatusMutation,
} from '../../src/features/admin/adminApi';
import type { BrowserRealtimeAdapter } from '../../src/shared/realtime/browserRealtime';
import type { AdminAnnouncementReadModel, AdminGuidanceListResponse, IdentityUserAccountListResponse } from '../../src/shared/api/base/contracts';

type GetCurrentActorResult = ReturnType<typeof useGetCurrentActorQuery>;
type ListAdminAnnouncementsResult = ReturnType<typeof useListAdminAnnouncementsQuery>;
type ListAdminGuidanceResult = ReturnType<typeof useListAdminGuidanceQuery>;
type ListIdentityUsersResult = ReturnType<typeof useListIdentityUsersQuery>;
type CreateIdentityUserMutationResult = ReturnType<typeof useCreateIdentityUserMutation>;
type ResetIdentityUserPasswordMutationResult = ReturnType<typeof useResetIdentityUserPasswordMutation>;
type RevokeIdentityUserSessionsMutationResult = ReturnType<typeof useRevokeIdentityUserSessionsMutation>;
type UpdateIdentityUserRolesMutationResult = ReturnType<typeof useUpdateIdentityUserRolesMutation>;
type UpdateIdentityUserStatusMutationResult = ReturnType<typeof useUpdateIdentityUserStatusMutation>;
type RealtimeAdapter = Pick<BrowserRealtimeAdapter, 'connect' | 'disconnect' | 'subscribe'>;

const useGetCurrentActorQueryMock = vi.fn<() => GetCurrentActorResult>();
const useListAdminAnnouncementsQueryMock = vi.fn<(...args: [void?, { skip?: boolean }?]) => ListAdminAnnouncementsResult>();
const useListAdminGuidanceQueryMock = vi.fn<(...args: [void?, { skip?: boolean }?]) => ListAdminGuidanceResult>();
const useListIdentityUsersQueryMock = vi.fn<(...args: [void?, { skip?: boolean }?]) => ListIdentityUsersResult>();
const useCreateIdentityUserMutationMock = vi.fn<() => CreateIdentityUserMutationResult>();
const useResetIdentityUserPasswordMutationMock = vi.fn<() => ResetIdentityUserPasswordMutationResult>();
const useRevokeIdentityUserSessionsMutationMock = vi.fn<() => RevokeIdentityUserSessionsMutationResult>();
const useUpdateIdentityUserRolesMutationMock = vi.fn<() => UpdateIdentityUserRolesMutationResult>();
const useUpdateIdentityUserStatusMutationMock = vi.fn<() => UpdateIdentityUserStatusMutationResult>();
const connectMock = vi.fn(() => Promise.resolve());
const disconnectMock = vi.fn(() => Promise.resolve());
const subscribeMock = vi.fn();
const createAdminRealtimeAdapterMock = vi.fn<() => RealtimeAdapter>();

function createCurrentActorQueryResult(data: GetCurrentActorResult['data']): GetCurrentActorResult {
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
  } as GetCurrentActorResult;
}

function createAnnouncementsQueryResult(data: ListAdminAnnouncementsResult['data']): ListAdminAnnouncementsResult {
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
  } as ListAdminAnnouncementsResult;
}

function createIdentityUsersQueryResult(data: IdentityUserAccountListResponse): ListIdentityUsersResult {
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
  } as ListIdentityUsersResult;
}

function createGuidanceQueryResult(data: AdminGuidanceListResponse): ListAdminGuidanceResult {
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
  } as ListAdminGuidanceResult;
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

vi.mock('../../src/features/admin/adminApi', () => ({
  useListAdminAnnouncementsQuery: (arg?: void, options?: { skip?: boolean }) => useListAdminAnnouncementsQueryMock(arg, options),
  useListAdminGuidanceQuery: (arg?: void, options?: { skip?: boolean }) => useListAdminGuidanceQueryMock(arg, options),
  useListIdentityUsersQuery: (arg?: void, options?: { skip?: boolean }) => useListIdentityUsersQueryMock(arg, options),
  useCreateIdentityUserMutation: () => useCreateIdentityUserMutationMock(),
  useResetIdentityUserPasswordMutation: () => useResetIdentityUserPasswordMutationMock(),
  useRevokeIdentityUserSessionsMutation: () => useRevokeIdentityUserSessionsMutationMock(),
  useUpdateIdentityUserRolesMutation: () => useUpdateIdentityUserRolesMutationMock(),
  useUpdateIdentityUserStatusMutation: () => useUpdateIdentityUserStatusMutationMock(),
}));

vi.mock('../../src/features/admin/adminRealtime', async () => {
  const actual = await vi.importActual<typeof import('../../src/features/admin/adminRealtime')>('../../src/features/admin/adminRealtime');

  return {
    ...actual,
    createAdminRealtimeAdapter: () => createAdminRealtimeAdapterMock(),
  };
});

describe('AdminFeature', () => {
  beforeEach(() => {
    connectMock.mockClear();
    disconnectMock.mockClear();
    subscribeMock.mockClear();
    createAdminRealtimeAdapterMock.mockClear();
    useGetCurrentActorQueryMock.mockReset();
    useListAdminAnnouncementsQueryMock.mockReset();
    useListAdminGuidanceQueryMock.mockReset();
    useListIdentityUsersQueryMock.mockReset();
    useCreateIdentityUserMutationMock.mockReset();
    useResetIdentityUserPasswordMutationMock.mockReset();
    useRevokeIdentityUserSessionsMutationMock.mockReset();
    useUpdateIdentityUserRolesMutationMock.mockReset();
    useUpdateIdentityUserStatusMutationMock.mockReset();

    createAdminRealtimeAdapterMock.mockReturnValue({
      connect: connectMock,
      disconnect: disconnectMock,
      subscribe: subscribeMock,
    });

    useCreateIdentityUserMutationMock.mockReturnValue(createMutationHookResult<CreateIdentityUserMutationResult>(vi.fn()));
    useResetIdentityUserPasswordMutationMock.mockReturnValue(createMutationHookResult<ResetIdentityUserPasswordMutationResult>(vi.fn()));
    useRevokeIdentityUserSessionsMutationMock.mockReturnValue(createMutationHookResult<RevokeIdentityUserSessionsMutationResult>(vi.fn()));
    useUpdateIdentityUserRolesMutationMock.mockReturnValue(createMutationHookResult<UpdateIdentityUserRolesMutationResult>(vi.fn()));
    useUpdateIdentityUserStatusMutationMock.mockReturnValue(createMutationHookResult<UpdateIdentityUserStatusMutationResult>(vi.fn()));
  });

  it('renders projected announcements and merges live realtime updates into the feed', async () => {
    let projectedHandler: ((announcement: AdminAnnouncementReadModel) => void) | null = null;
    subscribeMock.mockImplementation((_channel: string, handler: (announcement: AdminAnnouncementReadModel) => void) => {
      projectedHandler = handler;
      return () => {
        projectedHandler = null;
      };
    });

    useGetCurrentActorQueryMock.mockReturnValue(createCurrentActorQueryResult({
      actorId: 'identity:seeded-admin',
      displayName: 'Baseline Admin',
      roles: ['Admin'],
      userName: 'admin',
    }));

    useListAdminAnnouncementsQueryMock.mockReturnValue(createAnnouncementsQueryResult({
        announcements: [
          {
            announcementId: '11111111-1111-1111-1111-111111111111',
            title: 'Initial projection',
            body: 'Loaded from the Admin read model.',
            publishedUtc: '2026-04-04T12:30:00Z',
            publishedByActorId: 'admin',
            sourceModuleKey: 'sample-feature',
            sourceReference: null,
          },
        ],
      }));

    useListAdminGuidanceQueryMock.mockReturnValue(createGuidanceQueryResult({
      entries: [
        {
          entryId: '33333333-3333-3333-3333-333333333333',
          slug: 'what-is-this-baseline',
          title: 'What is this baseline?',
          body: 'A governed modular monolith reference with shared query seams.',
          category: 'Getting started',
          featured: true,
          publishedUtc: '2026-04-04T12:15:00Z',
        },
      ],
    }));

    useListIdentityUsersQueryMock.mockReturnValue(createIdentityUsersQueryResult({
      users: [
        {
          actorId: 'identity:seeded-admin',
          createdUtc: '2026-04-04T12:00:00Z',
          displayName: 'Baseline Admin',
          enabled: true,
          roles: ['Admin'],
          updatedUtc: '2026-04-04T12:00:00Z',
          userName: 'admin',
        },
      ],
    }));

    const { unmount } = render(<AdminFeature />);

    expect(screen.getByText('Initial projection')).toBeInTheDocument();
  expect(screen.getByText('Operator guidance')).toBeInTheDocument();
  expect(screen.getByText('What is this baseline?')).toBeInTheDocument();
  expect(screen.getByText('knowledge-base/what-is-this-baseline')).toBeInTheDocument();
    expect(screen.getByText('Operator accounts')).toBeInTheDocument();
    expect(screen.getByText('Baseline Admin')).toBeInTheDocument();
    await waitFor(() => expect(connectMock).toHaveBeenCalled());

    act(() => {
      projectedHandler?.({
        announcementId: '22222222-2222-2222-2222-222222222222',
        title: 'Live projection',
        body: 'Delivered from the shared realtime adapter.',
        publishedUtc: '2026-04-04T12:31:00Z',
        publishedByActorId: 'admin',
        sourceModuleKey: 'knowledge-base',
        sourceReference: 'live-projection',
      });
    });

    expect(screen.getByText('Live projection')).toBeInTheDocument();
    expect(screen.getByText('knowledge-base/live-projection')).toBeInTheDocument();
    expect(screen.getByText(/Last live projection:/i)).toBeInTheDocument();

    unmount();
    await waitFor(() => expect(disconnectMock).toHaveBeenCalled());
  });
});