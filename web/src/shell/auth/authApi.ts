import { starterApi } from '../../shared/api/base/starterBaseApi';
import type {
  IdentityActorSessionResponse,
  PasswordSignInRequest,
  PreferredTimeZoneResponse,
  StepUpCurrentActorRequest,
  UpdatePreferredTimeZoneRequest,
} from '../../shared/api/base/contracts';

export const authApi = starterApi.injectEndpoints({
  endpoints: (builder) => ({
    getCurrentActor: builder.query<IdentityActorSessionResponse, void>({
      providesTags: ['Session'],
      query: () => '/api/v1/identity/me',
    }),
    getPreferredTimeZone: builder.query<PreferredTimeZoneResponse, void>({
      providesTags: ['Session'],
      query: () => '/api/v1/identity/me/preferences/time-zone',
    }),
    signIn: builder.mutation<IdentityActorSessionResponse, PasswordSignInRequest>({
      invalidatesTags: ['Session'],
      query: (body) => ({
        body,
        method: 'POST',
        url: '/api/v1/identity/session/login',
      }),
    }),
    signOut: builder.mutation<void, void>({
      invalidatesTags: ['Session'],
      query: () => ({
        method: 'POST',
        url: '/api/v1/identity/session/logout',
      }),
    }),
    stepUp: builder.mutation<IdentityActorSessionResponse, StepUpCurrentActorRequest>({
      invalidatesTags: ['Session'],
      query: (body) => ({
        body,
        method: 'POST',
        url: '/api/v1/identity/session/step-up',
      }),
    }),
    updatePreferredTimeZone: builder.mutation<PreferredTimeZoneResponse, UpdatePreferredTimeZoneRequest>({
      invalidatesTags: ['Session', 'IdentityUsers'],
      query: (body) => ({
        body,
        method: 'PUT',
        url: '/api/v1/identity/me/preferences/time-zone',
      }),
    }),
  }),
});

export const {
  useGetCurrentActorQuery,
  useGetPreferredTimeZoneQuery,
  useSignInMutation,
  useSignOutMutation,
  useStepUpMutation,
  useUpdatePreferredTimeZoneMutation,
} = authApi;
