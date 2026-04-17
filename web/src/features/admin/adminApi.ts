import { starterApi } from '../../shared/api/base/starterBaseApi';
import type {
  AdminAnnouncementListResponse,
  AdminGuidanceListResponse,
  CreateIdentityUserRequest,
  IdentityUserAccountListResponse,
  IdentityUserAccountResponse,
  ResetIdentityUserPasswordRequest,
  UpdateIdentityUserRolesRequest,
  UpdateIdentityUserStatusRequest,
} from '../../shared/api/base/contracts';

export const adminApi = starterApi.injectEndpoints({
  endpoints: (builder) => ({
    createIdentityUser: builder.mutation<IdentityUserAccountResponse, CreateIdentityUserRequest>({
      invalidatesTags: ['IdentityUsers'],
      query: (body) => ({
        body,
        method: 'POST',
        url: '/api/v1/identity/users',
      }),
    }),
    listAdminAnnouncements: builder.query<AdminAnnouncementListResponse, void>({
      query: () => '/api/v1/admin/announcements',
    }),
    listAdminGuidance: builder.query<AdminGuidanceListResponse, void>({
      query: () => '/api/v1/admin/guidance',
    }),
    listIdentityUsers: builder.query<IdentityUserAccountListResponse, void>({
      providesTags: ['IdentityUsers'],
      query: () => '/api/v1/identity/users',
    }),
    resetIdentityUserPassword: builder.mutation<IdentityUserAccountResponse, { actorId: string; body: ResetIdentityUserPasswordRequest }>({
      invalidatesTags: ['IdentityUsers', 'Session'],
      query: ({ actorId, body }) => ({
        body,
        method: 'PUT',
        url: `/api/v1/identity/users/${actorId}/password`,
      }),
    }),
    revokeIdentityUserSessions: builder.mutation<IdentityUserAccountResponse, { actorId: string }>({
      invalidatesTags: ['IdentityUsers', 'Session'],
      query: ({ actorId }) => ({
        method: 'POST',
        url: `/api/v1/identity/users/${actorId}/revoke-sessions`,
      }),
    }),
    updateIdentityUserRoles: builder.mutation<IdentityUserAccountResponse, { actorId: string; body: UpdateIdentityUserRolesRequest }>({
      invalidatesTags: ['IdentityUsers', 'Session'],
      query: ({ actorId, body }) => ({
        body,
        method: 'PUT',
        url: `/api/v1/identity/users/${actorId}/roles`,
      }),
    }),
    updateIdentityUserStatus: builder.mutation<IdentityUserAccountResponse, { actorId: string; body: UpdateIdentityUserStatusRequest }>({
      invalidatesTags: ['IdentityUsers', 'Session'],
      query: ({ actorId, body }) => ({
        body,
        method: 'PUT',
        url: `/api/v1/identity/users/${actorId}/status`,
      }),
    }),
  }),
});

export const {
  useCreateIdentityUserMutation,
  useListAdminAnnouncementsQuery,
  useListAdminGuidanceQuery,
  useListIdentityUsersQuery,
  useResetIdentityUserPasswordMutation,
  useRevokeIdentityUserSessionsMutation,
  useUpdateIdentityUserRolesMutation,
  useUpdateIdentityUserStatusMutation,
} = adminApi;
