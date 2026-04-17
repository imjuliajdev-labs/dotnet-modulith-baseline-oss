import { createApi } from '@reduxjs/toolkit/query/react';
import { starterBaseQuery } from './starterBaseQuery';

export const starterApi = createApi({
  reducerPath: 'starterApi',
  baseQuery: starterBaseQuery,
  tagTypes: ['AuditTrail', 'BootstrapManifest', 'IdentityUsers', 'KnowledgeBaseEntries', 'KnowledgeBaseSettings', 'ModuleStates', 'OperationalHealth', 'SampleFeatureAnnouncements', 'Session'],
  endpoints: () => ({}),
});