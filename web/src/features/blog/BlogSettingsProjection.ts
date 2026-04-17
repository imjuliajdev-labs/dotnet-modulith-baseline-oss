import {
  BlogSettingsApi,
  createBlogSettingsEditor,
  type BlogSettingsResponse,
} from './BlogSettingsApi';
import { getBlogSettingsDependentReadTags } from './BlogSettingsTags';

export type BlogSettingsProjection = {
  operatorSummaryLine: string;
  previewLimitLine: string;
  version: number;
};

export function toBlogSettingsProjection(settings: BlogSettingsResponse): BlogSettingsProjection {
  const editor = createBlogSettingsEditor(settings);

  return {
    operatorSummaryLine: 'Operator summary: ' + editor.operatorSummary,
    previewLimitLine: 'Preview limit: ' + editor.previewLimit,
    version: editor.expectedVersion,
  };
}

export const BlogSettingsProjectionApi = BlogSettingsApi.injectEndpoints({
  endpoints: (builder) => ({
    getBlogSettingsProjection: builder.query<BlogSettingsProjection, void>({
      providesTags: [...getBlogSettingsDependentReadTags()],
      query: () => '/api/v1/blog/settings',
      transformResponse: (response: BlogSettingsResponse) => toBlogSettingsProjection(response),
    }),
  }),
});

export const {
  useGetBlogSettingsProjectionQuery,
} = BlogSettingsProjectionApi;
