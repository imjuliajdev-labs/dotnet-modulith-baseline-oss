import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { BlogFeature } from '../../src/features/blog/BlogFeature';
import type { useGetCurrentActorQuery } from '../../src/shell/auth/authApi';
import type {
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
} from '../../src/features/blog/blogApi';
import type {
    BlogCategoryListResponse,
    BlogPostListResponse,
    BlogPostResponse,
    BlogTagListResponse,
} from '../../src/shared/api/base/contracts';

type GetCurrentActorResult = ReturnType<typeof useGetCurrentActorQuery>;
type ListPublishedBlogPostsResult = ReturnType<typeof useListPublishedBlogPostsQuery>;
type GetPublishedBlogPostBySlugResult = ReturnType<typeof useGetPublishedBlogPostBySlugQuery>;
type ListBlogPostsForManagementResult = ReturnType<typeof useListBlogPostsForManagementQuery>;
type ListBlogCategoriesResult = ReturnType<typeof useListBlogCategoriesQuery>;
type ListBlogTagsResult = ReturnType<typeof useListBlogTagsQuery>;
type CreateBlogCategoryMutationResult = ReturnType<typeof useCreateBlogCategoryMutation>;
type CreateBlogPostMutationResult = ReturnType<typeof useCreateBlogPostMutation>;
type CreateBlogTagMutationResult = ReturnType<typeof useCreateBlogTagMutation>;
type UpdateBlogCategoryMutationResult = ReturnType<typeof useUpdateBlogCategoryMutation>;
type UpdateBlogPostMutationResult = ReturnType<typeof useUpdateBlogPostMutation>;
type UpdateBlogTagMutationResult = ReturnType<typeof useUpdateBlogTagMutation>;
type PublishBlogPostMutationResult = ReturnType<typeof usePublishBlogPostMutation>;
type RegisterBlogPostViewMutationResult = ReturnType<typeof useRegisterBlogPostViewMutation>;
type ScheduleBlogPostLifecycleMutationResult = ReturnType<typeof useScheduleBlogPostLifecycleMutation>;
type SetBlogPostStatusMutationResult = ReturnType<typeof useSetBlogPostStatusMutation>;

const useGetCurrentActorQueryMock = vi.fn<() => GetCurrentActorResult>();
const useListPublishedBlogPostsQueryMock = vi.fn<() => ListPublishedBlogPostsResult>();
const useGetPublishedBlogPostBySlugQueryMock = vi.fn<(slug: string, options?: { skip?: boolean }) => GetPublishedBlogPostBySlugResult>();
const useListBlogPostsForManagementQueryMock = vi.fn<(...args: [void?, { skip?: boolean }?]) => ListBlogPostsForManagementResult>();
const useListBlogCategoriesQueryMock = vi.fn<(...args: [void?, { skip?: boolean }?]) => ListBlogCategoriesResult>();
const useListBlogTagsQueryMock = vi.fn<(...args: [void?, { skip?: boolean }?]) => ListBlogTagsResult>();
const useCreateBlogCategoryMutationMock = vi.fn<() => CreateBlogCategoryMutationResult>();
const useCreateBlogPostMutationMock = vi.fn<() => CreateBlogPostMutationResult>();
const useCreateBlogTagMutationMock = vi.fn<() => CreateBlogTagMutationResult>();
const useUpdateBlogCategoryMutationMock = vi.fn<() => UpdateBlogCategoryMutationResult>();
const useUpdateBlogPostMutationMock = vi.fn<() => UpdateBlogPostMutationResult>();
const useUpdateBlogTagMutationMock = vi.fn<() => UpdateBlogTagMutationResult>();
const usePublishBlogPostMutationMock = vi.fn<() => PublishBlogPostMutationResult>();
const useRegisterBlogPostViewMutationMock = vi.fn<() => RegisterBlogPostViewMutationResult>();
const useScheduleBlogPostLifecycleMutationMock = vi.fn<() => ScheduleBlogPostLifecycleMutationResult>();
const useSetBlogPostStatusMutationMock = vi.fn<() => SetBlogPostStatusMutationResult>();

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

function createEmptyQueryResult<T>(): T {
    return {
        currentData: undefined,
        data: undefined,
        error: undefined,
        isError: false,
        isFetching: false,
        isLoading: false,
        isSuccess: false,
        isUninitialized: true,
        refetch: vi.fn(),
        status: 'uninitialized',
    } as T;
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

function createBlogPostResponse(overrides: Partial<BlogPostResponse> = {}): BlogPostResponse {
    return {
        body: 'Generated contracts should follow the live API surface.',
        categorySlug: 'platform-strategy',
        createdUtc: '2026-04-07T00:00:00Z',
        featured: true,
        postId: '11111111-1111-1111-1111-111111111111',
        publishedByActorId: 'identity:seeded-admin',
        publishedUtc: '2026-04-07T00:00:00Z',
        revisionNumber: 2,
        scheduledPublish: null,
        scheduledUnpublish: null,
        seoMetadata: {
            description: 'A concise post about generated contracts.',
            keywords: 'contracts, architecture',
            title: 'Trust the live API SEO',
        },
        shareTargets: ['linkedin', 'newsletter'],
        slug: 'trust-the-live-api',
        status: 'published',
        summary: 'A concise post about generated contracts.',
        tagNames: ['architecture', 'contracts'],
        title: 'Trust the live API',
        updatedByActorId: 'identity:seeded-admin',
        updatedUtc: '2026-04-07T00:00:00Z',
        version: 2,
        viewCount: 12,
        ...overrides,
    };
}

vi.mock('../../src/shell/auth/authApi', () => ({
    useGetCurrentActorQuery: () => useGetCurrentActorQueryMock(),
}));

vi.mock('../../src/features/blog/blogApi', () => ({
    useCreateBlogCategoryMutation: () => useCreateBlogCategoryMutationMock(),
    useCreateBlogPostMutation: () => useCreateBlogPostMutationMock(),
    useCreateBlogTagMutation: () => useCreateBlogTagMutationMock(),
    useGetPublishedBlogPostBySlugQuery: (slug: string, options?: { skip?: boolean }) => useGetPublishedBlogPostBySlugQueryMock(slug, options),
    useListBlogCategoriesQuery: (arg?: void, options?: { skip?: boolean }) => useListBlogCategoriesQueryMock(arg, options),
    useListBlogPostsForManagementQuery: (arg?: void, options?: { skip?: boolean }) => useListBlogPostsForManagementQueryMock(arg, options),
    useListBlogTagsQuery: (arg?: void, options?: { skip?: boolean }) => useListBlogTagsQueryMock(arg, options),
    useListPublishedBlogPostsQuery: () => useListPublishedBlogPostsQueryMock(),
    usePublishBlogPostMutation: () => usePublishBlogPostMutationMock(),
    useRegisterBlogPostViewMutation: () => useRegisterBlogPostViewMutationMock(),
    useScheduleBlogPostLifecycleMutation: () => useScheduleBlogPostLifecycleMutationMock(),
    useSetBlogPostStatusMutation: () => useSetBlogPostStatusMutationMock(),
    useUpdateBlogCategoryMutation: () => useUpdateBlogCategoryMutationMock(),
    useUpdateBlogPostMutation: () => useUpdateBlogPostMutationMock(),
    useUpdateBlogTagMutation: () => useUpdateBlogTagMutationMock(),
}));

describe('BlogFeature', () => {
    beforeEach(() => {
        useGetCurrentActorQueryMock.mockReset();
        useListPublishedBlogPostsQueryMock.mockReset();
        useGetPublishedBlogPostBySlugQueryMock.mockReset();
        useListBlogPostsForManagementQueryMock.mockReset();
        useListBlogCategoriesQueryMock.mockReset();
        useListBlogTagsQueryMock.mockReset();
        useCreateBlogCategoryMutationMock.mockReset();
        useCreateBlogPostMutationMock.mockReset();
        useCreateBlogTagMutationMock.mockReset();
        useUpdateBlogCategoryMutationMock.mockReset();
        useUpdateBlogPostMutationMock.mockReset();
        useUpdateBlogTagMutationMock.mockReset();
        usePublishBlogPostMutationMock.mockReset();
        useRegisterBlogPostViewMutationMock.mockReset();
        useScheduleBlogPostLifecycleMutationMock.mockReset();
        useSetBlogPostStatusMutationMock.mockReset();

        useGetCurrentActorQueryMock.mockReturnValue(createQueryResult({
            actorId: 'identity:seeded-admin',
            displayName: 'Baseline Admin',
            roles: ['Admin'],
            userName: 'admin',
        }) as GetCurrentActorResult);
        useListPublishedBlogPostsQueryMock.mockReturnValue(createQueryResult({ nextCursor: null, posts: [] }) as ListPublishedBlogPostsResult);
        useGetPublishedBlogPostBySlugQueryMock.mockReturnValue(createEmptyQueryResult<GetPublishedBlogPostBySlugResult>());
        useListBlogPostsForManagementQueryMock.mockReturnValue(createQueryResult({ nextCursor: null, posts: [] }) as ListBlogPostsForManagementResult);
        useListBlogCategoriesQueryMock.mockReturnValue(createQueryResult({ categories: [] }) as ListBlogCategoriesResult);
        useListBlogTagsQueryMock.mockReturnValue(createQueryResult({ tags: [] }) as ListBlogTagsResult);
        useCreateBlogCategoryMutationMock.mockReturnValue(createMutationHookResult<CreateBlogCategoryMutationResult>(vi.fn()));
        useCreateBlogPostMutationMock.mockReturnValue(createMutationHookResult<CreateBlogPostMutationResult>(vi.fn()));
        useCreateBlogTagMutationMock.mockReturnValue(createMutationHookResult<CreateBlogTagMutationResult>(vi.fn()));
        useUpdateBlogCategoryMutationMock.mockReturnValue(createMutationHookResult<UpdateBlogCategoryMutationResult>(vi.fn()));
        useUpdateBlogPostMutationMock.mockReturnValue(createMutationHookResult<UpdateBlogPostMutationResult>(vi.fn()));
        useUpdateBlogTagMutationMock.mockReturnValue(createMutationHookResult<UpdateBlogTagMutationResult>(vi.fn()));
        usePublishBlogPostMutationMock.mockReturnValue(createMutationHookResult<PublishBlogPostMutationResult>(vi.fn()));
        useRegisterBlogPostViewMutationMock.mockReturnValue(createMutationHookResult<RegisterBlogPostViewMutationResult>(vi.fn()));
        useScheduleBlogPostLifecycleMutationMock.mockReturnValue(createMutationHookResult<ScheduleBlogPostLifecycleMutationResult>(vi.fn()));
        useSetBlogPostStatusMutationMock.mockReturnValue(createMutationHookResult<SetBlogPostStatusMutationResult>(vi.fn()));
    });

    it('renders public posts and operator taxonomy controls when the actor has the Admin role', () => {
        const post = createBlogPostResponse();
        const posts: BlogPostListResponse = { nextCursor: null, posts: [post] };
        const categories: BlogCategoryListResponse = {
            categories: [
                {
                    createdUtc: '2026-04-06T00:00:00Z',
                    description: 'Long-range platform direction.',
                    name: 'Platform Strategy',
                    slug: 'platform-strategy',
                    updatedByActorId: 'identity:seeded-admin',
                    updatedUtc: '2026-04-07T00:00:00Z',
                    version: 2,
                },
            ],
        };
        const tags: BlogTagListResponse = {
            tags: [
                {
                    createdUtc: '2026-04-06T00:00:00Z',
                    description: 'Architecture-focused writing.',
                    displayName: 'Architecture',
                    slug: 'architecture',
                    updatedByActorId: 'identity:seeded-admin',
                    updatedUtc: '2026-04-07T00:00:00Z',
                    version: 2,
                },
            ],
        };

        useListPublishedBlogPostsQueryMock.mockReturnValue(createQueryResult(posts) as ListPublishedBlogPostsResult);
        useGetPublishedBlogPostBySlugQueryMock.mockReturnValue(createQueryResult(post) as GetPublishedBlogPostBySlugResult);
        useListBlogPostsForManagementQueryMock.mockReturnValue(createQueryResult(posts) as ListBlogPostsForManagementResult);
        useListBlogCategoriesQueryMock.mockReturnValue(createQueryResult(categories) as ListBlogCategoriesResult);
        useListBlogTagsQueryMock.mockReturnValue(createQueryResult(tags) as ListBlogTagsResult);

        render(<BlogFeature />);

        expect(screen.getByRole('heading', { name: 'Blog' })).toBeInTheDocument();
        expect(screen.getAllByText('category/platform-strategy · 12 views · revision 2')).toHaveLength(3);
        expect(screen.getByRole('heading', { name: 'Operator controls' })).toBeInTheDocument();
        expect(screen.getByRole('heading', { name: 'Categories' })).toBeInTheDocument();
        expect(screen.getByRole('heading', { name: 'Tags' })).toBeInTheDocument();
        expect(screen.getByText('Available categories: platform-strategy')).toBeInTheDocument();
        expect(screen.getByText('Available tags: architecture')).toBeInTheDocument();
    });

    it('submits a new category through the taxonomy management form', async () => {
        const user = userEvent.setup();
        const createBlogCategory = vi.fn().mockReturnValue({
            unwrap: vi.fn().mockResolvedValue({
                createdUtc: '2026-04-07T00:00:00Z',
                description: 'Long-range platform direction.',
                name: 'Platform Strategy',
                slug: 'platform-strategy',
                updatedByActorId: 'identity:seeded-admin',
                updatedUtc: '2026-04-07T00:00:00Z',
                version: 1,
            }),
        });

        useCreateBlogCategoryMutationMock.mockReturnValue(createMutationHookResult<CreateBlogCategoryMutationResult>(createBlogCategory));

        render(<BlogFeature />);

        await user.type(screen.getByLabelText('Category slug'), 'platform-strategy');
        await user.type(screen.getByLabelText('Category name'), 'Platform Strategy');
        await user.type(screen.getByLabelText('Category description'), 'Long-range platform direction.');
        await user.click(screen.getByRole('button', { name: 'Create category' }));

        expect(createBlogCategory).toHaveBeenCalledWith({
            description: 'Long-range platform direction.',
            name: 'Platform Strategy',
            slug: 'platform-strategy',
        });
        expect(await screen.findByText('Category created.')).toBeInTheDocument();
    });

    it('submits a new draft through the blog management endpoint', async () => {
        const user = userEvent.setup();
        const createBlogPost = vi.fn().mockReturnValue({
            unwrap: vi.fn().mockResolvedValue(createBlogPostResponse({
                body: 'Draft body',
                featured: false,
                postId: '33333333-3333-3333-3333-333333333333',
                publishedByActorId: null,
                publishedUtc: null,
                revisionNumber: 1,
                scheduledPublish: null,
                scheduledUnpublish: null,
                seoMetadata: {
                    description: 'Draft SEO description',
                    keywords: 'blog, draft',
                    title: 'Draft SEO title',
                },
                shareTargets: ['linkedin', 'newsletter'],
                slug: 'blog-draft-title',
                status: 'draft',
                summary: 'Draft summary',
                tagNames: ['architecture', 'publishing'],
                title: 'Blog draft title',
                version: 1,
                viewCount: 0,
            })),
        });

        useListBlogCategoriesQueryMock.mockReturnValue(createQueryResult({
            categories: [
                {
                    createdUtc: '2026-04-06T00:00:00Z',
                    description: 'Long-range platform direction.',
                    name: 'Platform Strategy',
                    slug: 'platform-strategy',
                    updatedByActorId: 'identity:seeded-admin',
                    updatedUtc: '2026-04-07T00:00:00Z',
                    version: 1,
                },
            ],
        }) as ListBlogCategoriesResult);
        useListBlogTagsQueryMock.mockReturnValue(createQueryResult({
            tags: [
                {
                    createdUtc: '2026-04-06T00:00:00Z',
                    description: 'Architecture-focused writing.',
                    displayName: 'Architecture',
                    slug: 'architecture',
                    updatedByActorId: 'identity:seeded-admin',
                    updatedUtc: '2026-04-07T00:00:00Z',
                    version: 1,
                },
                {
                    createdUtc: '2026-04-06T00:00:00Z',
                    description: 'Publishing-focused writing.',
                    displayName: 'Publishing',
                    slug: 'publishing',
                    updatedByActorId: 'identity:seeded-admin',
                    updatedUtc: '2026-04-07T00:00:00Z',
                    version: 1,
                },
            ],
        }) as ListBlogTagsResult);
        useCreateBlogPostMutationMock.mockReturnValue(createMutationHookResult<CreateBlogPostMutationResult>(createBlogPost));

        render(<BlogFeature />);

        await user.type(screen.getByLabelText('Title'), 'Blog draft title');
        await user.type(screen.getByLabelText('Category'), 'platform-strategy');
        await user.type(screen.getByLabelText('Summary'), 'Draft summary');
        await user.type(screen.getByLabelText('Body'), 'Draft body');
        await user.type(screen.getByLabelText('Tags'), 'architecture, publishing');
        await user.type(screen.getByLabelText('Share targets'), 'linkedin, newsletter');
        await user.type(screen.getByLabelText('SEO title'), 'Draft SEO title');
        await user.type(screen.getByLabelText('SEO description'), 'Draft SEO description');
        await user.type(screen.getByLabelText('SEO keywords'), 'blog, draft');
        await user.click(screen.getByRole('button', { name: 'Create draft' }));

        expect(createBlogPost).toHaveBeenCalledWith({
            body: 'Draft body',
            categorySlug: 'platform-strategy',
            featured: false,
            seoMetadata: {
                description: 'Draft SEO description',
                keywords: 'blog, draft',
                title: 'Draft SEO title',
            },
            shareTargets: ['linkedin', 'newsletter'],
            slug: null,
            summary: 'Draft summary',
            tagNames: ['architecture', 'publishing'],
            title: 'Blog draft title',
        });
        expect(await screen.findByText('Draft saved successfully.')).toBeInTheDocument();
    });

    it('submits a lifecycle schedule for the selected draft', async () => {
        const user = userEvent.setup();
        const draftPost = createBlogPostResponse({
            featured: false,
            postId: '44444444-4444-4444-4444-444444444444',
            publishedByActorId: null,
            publishedUtc: null,
            revisionNumber: 3,
            slug: 'scheduled-draft',
            status: 'draft',
            version: 3,
            viewCount: 0,
        });
        const scheduleBlogPostLifecycle = vi.fn().mockReturnValue({
            unwrap: vi.fn().mockResolvedValue(createBlogPostResponse({
                ...draftPost,
                scheduledPublish: {
                    localTimeResolution: 'exact',
                    scheduledByActorId: 'identity:seeded-admin',
                    scheduledForUtc: '2026-04-12T09:15:00Z',
                    scheduledLocalDate: '2026-04-12',
                    scheduledLocalTime: '09:15:00',
                    timeZoneId: 'Etc/UTC',
                },
                scheduledUnpublish: {
                    localTimeResolution: 'exact',
                    scheduledByActorId: 'identity:seeded-admin',
                    scheduledForUtc: '2026-04-14T17:30:00Z',
                    scheduledLocalDate: '2026-04-14',
                    scheduledLocalTime: '17:30:00',
                    timeZoneId: 'Etc/UTC',
                },
            })),
        });

        useListBlogPostsForManagementQueryMock.mockReturnValue(createQueryResult({ nextCursor: null, posts: [draftPost] }) as ListBlogPostsForManagementResult);
        useScheduleBlogPostLifecycleMutationMock.mockReturnValue(createMutationHookResult<ScheduleBlogPostLifecycleMutationResult>(scheduleBlogPostLifecycle));

        render(<BlogFeature />);

        await user.click(screen.getByRole('button', { name: 'Edit' }));

        fireEvent.change(screen.getByLabelText('Publish date'), { target: { value: '2026-04-12' } });
        fireEvent.change(screen.getByLabelText('Publish time'), { target: { value: '09:15' } });
        fireEvent.change(screen.getByLabelText('Unpublish date'), { target: { value: '2026-04-14' } });
        fireEvent.change(screen.getByLabelText('Unpublish time'), { target: { value: '17:30' } });
        await user.click(screen.getByRole('button', { name: 'Save schedule' }));

        expect(scheduleBlogPostLifecycle).toHaveBeenCalledWith({
            body: {
                expectedVersion: 3,
                publishLocalDate: '2026-04-12',
                publishLocalTime: '09:15',
                unpublishLocalDate: '2026-04-14',
                unpublishLocalTime: '17:30',
            },
            postId: '44444444-4444-4444-4444-444444444444',
        });
        expect(await screen.findByText('Lifecycle schedule saved.')).toBeInTheDocument();
    });
});
