import { starterApi } from '../../shared/api/base/starterBaseApi';
import type { paths } from '../../shared/api/generated/contracts.generated';
import {
  BlogSettingsTags,
  getBlogSettingsInvalidationTags,
  getBlogSettingsTagTypes,
} from './BlogSettingsTags';

type BlogSettingsRoute = Extract<'/api/v1/blog/settings', keyof paths>;
export type BlogSettingsResponse = [BlogSettingsRoute] extends [never]
  ? Record<string, unknown>
  : paths[BlogSettingsRoute]['get']['responses'][200]['content']['application/json'];
export type BlogSettingsUpdateRequest = [BlogSettingsRoute] extends [never]
  ? Record<string, unknown>
  : paths[BlogSettingsRoute]['put']['requestBody']['content']['application/json'];

export type BlogSettingsEditor = {
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

export function createBlogSettingsEditor(settings: BlogSettingsResponse): BlogSettingsEditor {
  const source: Record<string, unknown> = settings;

  return {
    expectedVersion: readNumber(source, 'version', 0),
    operatorSummary: readString(source, 'operatorSummary', ''),
    previewLimit: readNumber(source, 'previewLimit', 10),
    updatedByActorId: readString(source, 'updatedByActorId', 'unknown'),
    updatedUtc: readString(source, 'updatedUtc', ''),
  };
}

export function toBlogSettingsUpdateRequest(editor: BlogSettingsEditor): BlogSettingsUpdateRequest {
  return {
    expectedVersion: editor.expectedVersion,
    operatorSummary: editor.operatorSummary,
    previewLimit: editor.previewLimit,
  };
}

const BlogSettingsTagTypes = getBlogSettingsTagTypes();

export const BlogSettingsApi = starterApi.enhanceEndpoints({
  addTagTypes: [...BlogSettingsTagTypes],
}).injectEndpoints({
  endpoints: (builder) => ({
    getBlogSettings: builder.query<BlogSettingsResponse, void>({
      providesTags: [BlogSettingsTags.settings],
      query: () => '/api/v1/blog/settings',
    }),
    updateBlogSettings: builder.mutation<BlogSettingsResponse, BlogSettingsUpdateRequest>({
      invalidatesTags: [...getBlogSettingsInvalidationTags()],
      query: (body) => ({
        body,
        method: 'PUT',
        url: '/api/v1/blog/settings',
      }),
    }),
  }),
});

export const {
  useGetBlogSettingsQuery,
  useUpdateBlogSettingsMutation,
} = BlogSettingsApi;
