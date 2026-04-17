import { starterApi } from '../../shared/api/base/starterBaseApi';
import { createIdempotencyKey } from '../../shared/api/base/idempotencyKeys';
import type {
  BlogCategoryListResponse,
  BlogCategoryResponse,
  BlogPostListResponse,
  BlogPostResponse,
  BlogTagListResponse,
  BlogTagResponse,
  CreateBlogCategoryRequest,
  CreateBlogPostRequest,
  CreateBlogTagRequest,
  PublishBlogPostRequest,
  ScheduleBlogPostLifecycleRequest,
  UpdateBlogCategoryRequest,
  UpdateBlogPostRequest,
  UpdateBlogPostStatusRequest,
  UpdateBlogTagRequest,
} from '../../shared/api/base/contracts';

export const blogApi = starterApi.enhanceEndpoints({
  addTagTypes: ['BlogPosts', 'BlogTaxonomy'],
}).injectEndpoints({
  endpoints: (builder) => ({
    createBlogCategory: builder.mutation<BlogCategoryResponse, CreateBlogCategoryRequest>({
      invalidatesTags: ['BlogTaxonomy'],
      query: (body) => ({
        body,
        method: 'POST',
        url: '/api/v1/blog/manage/categories',
      }),
    }),
    createBlogPost: builder.mutation<BlogPostResponse, CreateBlogPostRequest>({
      invalidatesTags: ['BlogPosts'],
      query: (body) => ({
        body,
        method: 'POST',
        url: '/api/v1/blog/manage/posts',
      }),
    }),
    createBlogTag: builder.mutation<BlogTagResponse, CreateBlogTagRequest>({
      invalidatesTags: ['BlogTaxonomy'],
      query: (body) => ({
        body,
        method: 'POST',
        url: '/api/v1/blog/manage/tags',
      }),
    }),
    getPublishedBlogPostBySlug: builder.query<BlogPostResponse, string>({
      providesTags: ['BlogPosts'],
      query: (slug) => `/api/v1/blog/posts/${slug}`,
    }),
    registerBlogPostView: builder.mutation<void, { slug: string }>({
      invalidatesTags: ['BlogPosts'],
      query: ({ slug }) => ({
        method: 'POST',
        url: `/api/v1/blog/posts/${slug}/views`,
      }),
    }),
    listBlogCategories: builder.query<BlogCategoryListResponse, void>({
      providesTags: ['BlogTaxonomy'],
      query: () => '/api/v1/blog/manage/categories',
    }),
    listBlogPostsForManagement: builder.query<BlogPostListResponse, void>({
      providesTags: ['BlogPosts'],
      query: () => '/api/v1/blog/manage/posts',
    }),
    listBlogTags: builder.query<BlogTagListResponse, void>({
      providesTags: ['BlogTaxonomy'],
      query: () => '/api/v1/blog/manage/tags',
    }),
    listPublishedBlogPosts: builder.query<BlogPostListResponse, void>({
      providesTags: ['BlogPosts'],
      query: () => '/api/v1/blog/posts',
    }),
    publishBlogPost: builder.mutation<BlogPostResponse, { body: PublishBlogPostRequest; postId: string }>({
      invalidatesTags: ['BlogPosts'],
      query: ({ body, postId }) => ({
        body,
        headers: {
          'Idempotency-Key': createIdempotencyKey(),
        },
        method: 'PUT',
        url: `/api/v1/blog/manage/posts/${postId}/publish`,
      }),
    }),
    scheduleBlogPostLifecycle: builder.mutation<BlogPostResponse, { body: ScheduleBlogPostLifecycleRequest; postId: string }>({
      invalidatesTags: ['BlogPosts'],
      query: ({ body, postId }) => ({
        body,
        method: 'PUT',
        url: `/api/v1/blog/manage/posts/${postId}/schedule`,
      }),
    }),
    setBlogPostStatus: builder.mutation<BlogPostResponse, { body: UpdateBlogPostStatusRequest; postId: string }>({
      invalidatesTags: ['BlogPosts'],
      query: ({ body, postId }) => ({
        body,
        method: 'PUT',
        url: `/api/v1/blog/manage/posts/${postId}/status`,
      }),
    }),
    updateBlogCategory: builder.mutation<BlogCategoryResponse, { body: UpdateBlogCategoryRequest; slug: string }>({
      invalidatesTags: ['BlogTaxonomy'],
      query: ({ body, slug }) => ({
        body,
        method: 'PUT',
        url: `/api/v1/blog/manage/categories/${slug}`,
      }),
    }),
    updateBlogPost: builder.mutation<BlogPostResponse, { body: UpdateBlogPostRequest; postId: string }>({
      invalidatesTags: ['BlogPosts'],
      query: ({ body, postId }) => ({
        body,
        method: 'PUT',
        url: `/api/v1/blog/manage/posts/${postId}`,
      }),
    }),
    updateBlogTag: builder.mutation<BlogTagResponse, { body: UpdateBlogTagRequest; slug: string }>({
      invalidatesTags: ['BlogTaxonomy'],
      query: ({ body, slug }) => ({
        body,
        method: 'PUT',
        url: `/api/v1/blog/manage/tags/${slug}`,
      }),
    }),
  }),
});

export const {
  useCreateBlogCategoryMutation,
  useCreateBlogPostMutation,
  useCreateBlogTagMutation,
  useGetPublishedBlogPostBySlugQuery,
  useListBlogCategoriesQuery,
  useListBlogPostsForManagementQuery,
  useListBlogTagsQuery,
  useListPublishedBlogPostsQuery,
  usePublishBlogPostMutation,
  useRegisterBlogPostViewMutation,
  useScheduleBlogPostLifecycleMutation,
  useSetBlogPostStatusMutation,
  useUpdateBlogCategoryMutation,
  useUpdateBlogPostMutation,
  useUpdateBlogTagMutation,
} = blogApi;
