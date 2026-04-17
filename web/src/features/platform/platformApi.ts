import { starterApi } from '../../shared/api/base/starterBaseApi';
import type {
  BootstrapManifestResponse,
  ModuleStateListResponse,
  ModuleStateResponse,
  OperationalHealthSummaryResponse,
  PlatformAuditTrailResponse,
} from '../../shared/api/base/contracts';

export const platformApi = starterApi.injectEndpoints({
  endpoints: (builder) => ({
    disableModule: builder.mutation<ModuleStateResponse, string>({
      invalidatesTags: ['AuditTrail', 'ModuleStates', 'OperationalHealth'],
      query: (moduleKey) => ({
        method: 'POST',
        url: `/api/v1/platform/modules/${moduleKey}/disable`,
      }),
    }),
    enableModule: builder.mutation<ModuleStateResponse, string>({
      invalidatesTags: ['AuditTrail', 'ModuleStates', 'OperationalHealth'],
      query: (moduleKey) => ({
        method: 'POST',
        url: `/api/v1/platform/modules/${moduleKey}/enable`,
      }),
    }),
    getAuditTrail: builder.query<PlatformAuditTrailResponse, number | void>({
      providesTags: ['AuditTrail'],
      query: (limit = 16) => {
        const normalizedLimit = limit ?? 16;
        return `/api/v1/platform/audit-events?limit=${normalizedLimit}`;
      },
    }),
    getBootstrapManifest: builder.query<BootstrapManifestResponse, void>({
      providesTags: ['BootstrapManifest'],
      query: () => '/api/v1/platform/bootstrap',
    }),
    getModuleStates: builder.query<ModuleStateListResponse, void>({
      providesTags: ['ModuleStates'],
      query: () => '/api/v1/platform/modules',
    }),
    getOperationalHealth: builder.query<OperationalHealthSummaryResponse, void>({
      providesTags: ['OperationalHealth'],
      query: () => '/api/v1/platform/health',
    }),
  }),
});

export const {
  useDisableModuleMutation,
  useEnableModuleMutation,
  useGetAuditTrailQuery,
  useGetBootstrapManifestQuery,
  useGetModuleStatesQuery,
  useGetOperationalHealthQuery,
} = platformApi;