import { useEffect, useMemo, useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useGetCurrentActorQuery } from '../../shell/auth/authApi';
import { getErrorMessage, isUnauthorizedError } from '../../shared/lib/queryErrors';
import type { BlogCategoryResponse, BlogPostResponse, BlogTagResponse } from '../../shared/api/base/contracts';
import {
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
} from './blogApi';
import {
    blogPostSchema,
    categorySchema,
    describeSchedule,
    emptyCategoryFormData,
    emptyCategoryMeta,
    emptyPostFormData,
    emptyPostMeta,
    emptyScheduleFormData,
    emptyTagFormData,
    emptyTagMeta,
    parseDelimitedValues,
    postToFormData,
    postToMeta,
    postToScheduleData,
    scheduleSchema,
    tagSchema,
    toNullableTrimmed,
    type BlogPostFormData,
    type BlogPostMeta,
    type CategoryFormData,
    type CategoryMeta,
    type FlashMessage,
    type ScheduleFormData,
    type TagFormData,
    type TagMeta,
} from './blogFormModels';

export function BlogFeature() {
    const sessionQuery = useGetCurrentActorQuery();
    const session = isUnauthorizedError(sessionQuery.error) ? null : sessionQuery.data ?? null;
    const hasAdminAccess = session?.roles?.includes('Admin') ?? false;
    const canReadManagement = hasAdminAccess;
    const canManage = hasAdminAccess;

    const publishedQuery = useListPublishedBlogPostsQuery();
    const managementQuery = useListBlogPostsForManagementQuery(undefined, { skip: !canReadManagement });
    const categoryQuery = useListBlogCategoriesQuery(undefined, { skip: !canReadManagement });
    const tagQuery = useListBlogTagsQuery(undefined, { skip: !canReadManagement });

    const [createBlogCategory, createBlogCategoryState] = useCreateBlogCategoryMutation();
    const [createBlogPost, createBlogPostState] = useCreateBlogPostMutation();
    const [createBlogTag, createBlogTagState] = useCreateBlogTagMutation();
    const [updateBlogCategory, updateBlogCategoryState] = useUpdateBlogCategoryMutation();
    const [updateBlogPost, updateBlogPostState] = useUpdateBlogPostMutation();
    const [updateBlogTag, updateBlogTagState] = useUpdateBlogTagMutation();
    const [publishBlogPost] = usePublishBlogPostMutation();
    const [registerBlogPostView] = useRegisterBlogPostViewMutation();
    const [setBlogPostStatus] = useSetBlogPostStatusMutation();
    const [scheduleBlogPostLifecycle, scheduleBlogPostLifecycleState] = useScheduleBlogPostLifecycleMutation();

    const [postMeta, setPostMeta] = useState<BlogPostMeta>(emptyPostMeta);
    const [categoryMeta, setCategoryMeta] = useState<CategoryMeta>(emptyCategoryMeta);
    const [tagMeta, setTagMeta] = useState<TagMeta>(emptyTagMeta);
    const [mutationError, setMutationError] = useState<string | null>(null);
    const [flashMessage, setFlashMessage] = useState<FlashMessage | null>(null);
    const [selectedSlug, setSelectedSlug] = useState<string | null>(null);
    const [trackedViewSlug, setTrackedViewSlug] = useState<string | null>(null);
    const detailQuery = useGetPublishedBlogPostBySlugQuery(selectedSlug ?? '', { skip: !selectedSlug });

    const postForm = useForm<BlogPostFormData>({
        defaultValues: emptyPostFormData,
        resolver: zodResolver(blogPostSchema),
    });

    const categoryForm = useForm<CategoryFormData>({
        defaultValues: emptyCategoryFormData,
        resolver: zodResolver(categorySchema),
    });

    const tagForm = useForm<TagFormData>({
        defaultValues: emptyTagFormData,
        resolver: zodResolver(tagSchema),
    });

    const scheduleForm = useForm<ScheduleFormData>({
        defaultValues: emptyScheduleFormData,
        resolver: zodResolver(scheduleSchema),
    });

    const publishedPostItems = publishedQuery.data?.posts;
    const publishedPosts = useMemo(() => publishedPostItems ?? [], [publishedPostItems]);
    const managementPosts = managementQuery.data?.posts ?? [];
    const categories = categoryQuery.data?.categories ?? [];
    const tags = tagQuery.data?.tags ?? [];
    const pending = createBlogCategoryState.isLoading
        || createBlogPostState.isLoading
        || createBlogTagState.isLoading
        || updateBlogCategoryState.isLoading
        || updateBlogPostState.isLoading
        || updateBlogTagState.isLoading
        || scheduleBlogPostLifecycleState.isLoading;

    function loadPostIntoForms(post: BlogPostResponse) {
        postForm.reset(postToFormData(post));
        scheduleForm.reset(postToScheduleData(post));
        setPostMeta(postToMeta(post));
    }

    function loadCategoryIntoForm(category: BlogCategoryResponse) {
        categoryForm.reset({
            description: category.description ?? '',
            name: category.name,
            slug: category.slug,
        });
        setCategoryMeta({ expectedVersion: Number(category.version), slug: category.slug });
    }

    function loadTagIntoForm(tag: BlogTagResponse) {
        tagForm.reset({
            description: tag.description ?? '',
            displayName: tag.displayName,
            slug: tag.slug,
        });
        setTagMeta({ expectedVersion: Number(tag.version), slug: tag.slug });
    }

    useEffect(() => {
        if (!selectedSlug && publishedPosts.length > 0) {
            setSelectedSlug(publishedPosts[0]?.slug ?? null);
        }
    }, [publishedPosts, selectedSlug]);

    useEffect(() => {
        if (!selectedSlug || !detailQuery.data || trackedViewSlug === selectedSlug) {
            return;
        }

        setTrackedViewSlug(selectedSlug);
        void registerBlogPostView({ slug: selectedSlug });
    }, [detailQuery.data, registerBlogPostView, selectedSlug, trackedViewSlug]);

    function clearFeedback() {
        setMutationError(null);
        setFlashMessage(null);
    }

    function showFlash(text: string, tone: FlashMessage['tone'] = 'healthy') {
        setFlashMessage({ text, tone });
    }

    async function onSubmit(data: BlogPostFormData) {
        clearFeedback();

        try {
            const payload = {
                body: data.body,
                categorySlug: toNullableTrimmed(data.categorySlug),
                featured: data.featured,
                seoMetadata: {
                    description: toNullableTrimmed(data.seoDescription),
                    keywords: toNullableTrimmed(data.seoKeywords),
                    title: toNullableTrimmed(data.seoTitle),
                },
                shareTargets: parseDelimitedValues(data.shareTargetsText),
                slug: toNullableTrimmed(data.slug),
                summary: data.summary,
                tagNames: parseDelimitedValues(data.tagNamesText),
                title: data.title,
            };

            const response = postMeta.postId
                ? await updateBlogPost({
                    body: {
                        ...payload,
                        expectedVersion: postMeta.expectedVersion,
                    },
                    postId: postMeta.postId,
                }).unwrap()
                : await createBlogPost(payload).unwrap();

            loadPostIntoForms(response);
            setSelectedSlug(response.slug);
            showFlash('Draft saved successfully.');
        }
        catch (error) {
            setMutationError(getErrorMessage(error));
        }
    }

    async function onCategorySubmit(data: CategoryFormData) {
        clearFeedback();

        try {
            const response = categoryMeta.expectedVersion > 0
                ? await updateBlogCategory({
                    body: {
                        description: toNullableTrimmed(data.description),
                        expectedVersion: categoryMeta.expectedVersion,
                        name: data.name,
                    },
                    slug: categoryMeta.slug,
                }).unwrap()
                : await createBlogCategory({
                    description: toNullableTrimmed(data.description),
                    name: data.name,
                    slug: toNullableTrimmed(data.slug),
                }).unwrap();

            loadCategoryIntoForm(response);
            showFlash(categoryMeta.expectedVersion > 0 ? 'Category updated.' : 'Category created.');
        }
        catch (error) {
            setMutationError(getErrorMessage(error));
        }
    }

    async function onTagSubmit(data: TagFormData) {
        clearFeedback();

        try {
            const response = tagMeta.expectedVersion > 0
                ? await updateBlogTag({
                    body: {
                        description: toNullableTrimmed(data.description),
                        displayName: data.displayName,
                        expectedVersion: tagMeta.expectedVersion,
                    },
                    slug: tagMeta.slug,
                }).unwrap()
                : await createBlogTag({
                    description: toNullableTrimmed(data.description),
                    displayName: data.displayName,
                    slug: toNullableTrimmed(data.slug),
                }).unwrap();

            loadTagIntoForm(response);
            showFlash(tagMeta.expectedVersion > 0 ? 'Tag updated.' : 'Tag created.');
        }
        catch (error) {
            setMutationError(getErrorMessage(error));
        }
    }

    async function onScheduleSubmit(data: ScheduleFormData) {
        if (!postMeta.postId) {
            return;
        }

        clearFeedback();

        try {
            const response = await scheduleBlogPostLifecycle({
                body: {
                    expectedVersion: postMeta.expectedVersion,
                    publishLocalDate: toNullableTrimmed(data.publishLocalDate),
                    publishLocalTime: toNullableTrimmed(data.publishLocalTime),
                    unpublishLocalDate: toNullableTrimmed(data.unpublishLocalDate),
                    unpublishLocalTime: toNullableTrimmed(data.unpublishLocalTime),
                },
                postId: postMeta.postId,
            }).unwrap();

            loadPostIntoForms(response);
            setSelectedSlug(response.slug);
            showFlash('Lifecycle schedule saved.');
        }
        catch (error) {
            setMutationError(getErrorMessage(error));
        }
    }

    async function onClearSchedule() {
        if (!postMeta.postId) {
            return;
        }

        clearFeedback();

        try {
            const response = await scheduleBlogPostLifecycle({
                body: {
                    expectedVersion: postMeta.expectedVersion,
                    publishLocalDate: null,
                    publishLocalTime: null,
                    unpublishLocalDate: null,
                    unpublishLocalTime: null,
                },
                postId: postMeta.postId,
            }).unwrap();

            loadPostIntoForms(response);
            setSelectedSlug(response.slug);
            showFlash('Lifecycle schedule cleared.', 'warning');
        }
        catch (error) {
            setMutationError(getErrorMessage(error));
        }
    }

    async function onStatusChange(post: BlogPostResponse, status: 'archived' | 'draft' | 'published') {
        clearFeedback();

        try {
            const response = status === 'published'
                ? await publishBlogPost({
                    body: { expectedVersion: post.version },
                    postId: post.postId,
                }).unwrap()
                : await setBlogPostStatus({
                    body: {
                        expectedVersion: post.version,
                        status,
                    },
                    postId: post.postId,
                }).unwrap();

            if (postMeta.postId === response.postId) {
                loadPostIntoForms(response);
            }

            setSelectedSlug(response.slug);
            showFlash(status === 'published' ? 'Post published.' : `Post moved to ${status}.`);
        }
        catch (error) {
            setMutationError(getErrorMessage(error));
        }
    }

    return (
        <div className="dashboard-grid">
            <section className="feature-panel dashboard-grid__full">
                <div className="feature-panel__header">
                    <div>
                        <h2 className="feature-panel__title">Blog</h2>
                        <p className="feature-panel__copy">Public posts stay readable for everyone, while operators can shape taxonomy, draft editorials, and schedule lifecycle changes from the same module surface.</p>
                    </div>
                    <span className="pill" data-tone={canManage ? 'healthy' : 'warning'}>{canManage ? 'operator mode' : 'public read'}</span>
                </div>

                {publishedQuery.isLoading && !publishedQuery.data ? <p className="message">Loading blog posts.</p> : null}

                <div className="shell-list">
                    {publishedPosts.map((post) => (
                        <button className="feature-panel" key={post.postId} onClick={() => setSelectedSlug(post.slug)} type="button">
                            <div className="feature-panel__header">
                                <div>
                                    <h3 className="feature-panel__title">{post.title}</h3>
                                    <p className="module-card__meta">{post.summary}</p>
                                </div>
                                <span className="pill" data-tone={post.featured ? 'healthy' : 'warning'}>{post.featured ? 'featured' : post.status}</span>
                            </div>

                            <p className="module-card__meta">{post.categorySlug ? `category/${post.categorySlug}` : 'uncategorized'} · {post.viewCount} views · revision {post.revisionNumber}</p>
                        </button>
                    ))}
                </div>

                {!publishedQuery.isLoading && publishedPosts.length === 0 ? <p className="message">No published blog posts are available yet.</p> : null}

                {detailQuery.data ? (
                    <article className="feature-panel">
                        <div className="feature-panel__header">
                            <div>
                                <h3 className="feature-panel__title">{detailQuery.data.title}</h3>
                                <p className="module-card__meta">{detailQuery.data.summary}</p>
                            </div>
                            <span className="pill">blog/{detailQuery.data.slug}</span>
                        </div>

                        <p className="module-card__meta">{detailQuery.data.categorySlug ? `category/${detailQuery.data.categorySlug}` : 'uncategorized'} · {detailQuery.data.viewCount} views · revision {detailQuery.data.revisionNumber}</p>

                        {detailQuery.data.tagNames.length > 0 ? <p className="module-card__meta">Tags: {detailQuery.data.tagNames.join(', ')}</p> : null}
                        {detailQuery.data.shareTargets.length > 0 ? <p className="module-card__meta">Share to: {detailQuery.data.shareTargets.join(', ')}</p> : null}
                        {detailQuery.data.scheduledUnpublish ? <p className="module-card__meta">{describeSchedule('Unpublish', detailQuery.data.scheduledUnpublish)}</p> : null}
                        {detailQuery.data.seoMetadata.title || detailQuery.data.seoMetadata.description || detailQuery.data.seoMetadata.keywords ? (
                            <p className="module-card__meta">SEO: {detailQuery.data.seoMetadata.title ?? 'untitled'} / {detailQuery.data.seoMetadata.description ?? 'no description'} / {detailQuery.data.seoMetadata.keywords ?? 'no keywords'}</p>
                        ) : null}

                        <p className="feature-panel__copy" style={{ whiteSpace: 'pre-wrap' }}>{detailQuery.data.body}</p>
                    </article>
                ) : null}
            </section>

            {canReadManagement ? (
                <section className="feature-panel dashboard-grid__full">
                    <div className="feature-panel__header">
                        <div>
                            <h3 className="feature-panel__title">Operator controls</h3>
                            <p className="feature-panel__copy">Canonical taxonomy keeps editorial metadata governed, while lifecycle scheduling resolves through the current operator time zone.</p>
                        </div>
                        <span className="pill" data-tone={canManage ? 'healthy' : 'warning'}>{canManage ? 'manage granted' : 'read only'}</span>
                    </div>

                    {mutationError ? <p className="message" data-tone="danger">{mutationError}</p> : null}
                    {flashMessage ? <p className="message" data-tone={flashMessage.tone}>{flashMessage.text}</p> : null}

                    <div className="shell-list">
                        <article className="feature-panel">
                            <div className="feature-panel__header">
                                <div>
                                    <h4 className="feature-panel__title">Categories</h4>
                                    <p className="feature-panel__copy">Posts reference category slugs, but operators manage the canonical label and description here.</p>
                                </div>
                                <span className="pill">{categories.length} total</span>
                            </div>

                            {categories.length > 0 ? (
                                <div className="shell-list">
                                    {categories.map((category) => (
                                        <article className="feature-panel" key={category.slug}>
                                            <div className="feature-panel__header">
                                                <div>
                                                    <h5 className="feature-panel__title">{category.name}</h5>
                                                    <p className="module-card__meta">category/{category.slug}</p>
                                                </div>
                                                <button className="button-ghost" disabled={!canManage || pending} onClick={() => loadCategoryIntoForm(category)} type="button">Edit</button>
                                            </div>
                                            {category.description ? <p className="module-card__meta">{category.description}</p> : null}
                                        </article>
                                    ))}
                                </div>
                            ) : (
                                <p className="message">No categories yet. Create one before assigning posts.</p>
                            )}

                            <form className="auth-panel__form" onSubmit={(event) => {
                                void categoryForm.handleSubmit(onCategorySubmit)(event);
                            }}>
                                <label className="field">
                                    <span className="field__label">Category slug</span>
                                    <input
                                        className="field__input"
                                        disabled={!canManage || pending || categoryMeta.expectedVersion > 0}
                                        placeholder="platform-strategy"
                                        {...categoryForm.register('slug')}
                                    />
                                </label>

                                <label className="field">
                                    <span className="field__label">Category name</span>
                                    <input
                                        className="field__input"
                                        disabled={!canManage || pending}
                                        {...categoryForm.register('name')}
                                        aria-invalid={!!categoryForm.formState.errors.name}
                                        aria-describedby={categoryForm.formState.errors.name ? 'cat-name-error' : undefined}
                                    />
                                </label>
                                {categoryForm.formState.errors.name && (
                                    <span id="cat-name-error" role="alert" className="message" data-tone="danger">{categoryForm.formState.errors.name.message}</span>
                                )}

                                <label className="field">
                                    <span className="field__label">Category description</span>
                                    <textarea
                                        className="field__input"
                                        disabled={!canManage || pending}
                                        rows={3}
                                        {...categoryForm.register('description')}
                                    />
                                </label>

                                <div className="module-card__actions">
                                    <button className="button" disabled={!canManage || pending} type="submit">{categoryMeta.expectedVersion > 0 ? 'Update category' : 'Create category'}</button>
                                    <button className="button-ghost" onClick={() => { categoryForm.reset(emptyCategoryFormData); setCategoryMeta(emptyCategoryMeta); }} type="button">Clear category form</button>
                                    {categoryMeta.expectedVersion > 0 ? <span className="module-card__meta">Version {categoryMeta.expectedVersion}</span> : null}
                                </div>
                            </form>
                        </article>

                        <article className="feature-panel">
                            <div className="feature-panel__header">
                                <div>
                                    <h4 className="feature-panel__title">Tags</h4>
                                    <p className="feature-panel__copy">Tags stay canonical too, so posts carry stable slugs instead of free-form labels.</p>
                                </div>
                                <span className="pill">{tags.length} total</span>
                            </div>

                            {tags.length > 0 ? (
                                <div className="shell-list">
                                    {tags.map((tag) => (
                                        <article className="feature-panel" key={tag.slug}>
                                            <div className="feature-panel__header">
                                                <div>
                                                    <h5 className="feature-panel__title">{tag.displayName}</h5>
                                                    <p className="module-card__meta">tag/{tag.slug}</p>
                                                </div>
                                                <button className="button-ghost" disabled={!canManage || pending} onClick={() => loadTagIntoForm(tag)} type="button">Edit</button>
                                            </div>
                                            {tag.description ? <p className="module-card__meta">{tag.description}</p> : null}
                                        </article>
                                    ))}
                                </div>
                            ) : (
                                <p className="message">No tags yet. Create one before assigning it to a post.</p>
                            )}

                            <form className="auth-panel__form" onSubmit={(event) => {
                                void tagForm.handleSubmit(onTagSubmit)(event);
                            }}>
                                <label className="field">
                                    <span className="field__label">Tag slug</span>
                                    <input
                                        className="field__input"
                                        disabled={!canManage || pending || tagMeta.expectedVersion > 0}
                                        placeholder="architecture"
                                        {...tagForm.register('slug')}
                                    />
                                </label>

                                <label className="field">
                                    <span className="field__label">Tag name</span>
                                    <input
                                        className="field__input"
                                        disabled={!canManage || pending}
                                        {...tagForm.register('displayName')}
                                        aria-invalid={!!tagForm.formState.errors.displayName}
                                        aria-describedby={tagForm.formState.errors.displayName ? 'tag-name-error' : undefined}
                                    />
                                </label>
                                {tagForm.formState.errors.displayName && (
                                    <span id="tag-name-error" role="alert" className="message" data-tone="danger">{tagForm.formState.errors.displayName.message}</span>
                                )}

                                <label className="field">
                                    <span className="field__label">Tag description</span>
                                    <textarea
                                        className="field__input"
                                        disabled={!canManage || pending}
                                        rows={3}
                                        {...tagForm.register('description')}
                                    />
                                </label>

                                <div className="module-card__actions">
                                    <button className="button" disabled={!canManage || pending} type="submit">{tagMeta.expectedVersion > 0 ? 'Update tag' : 'Create tag'}</button>
                                    <button className="button-ghost" onClick={() => { tagForm.reset(emptyTagFormData); setTagMeta(emptyTagMeta); }} type="button">Clear tag form</button>
                                    {tagMeta.expectedVersion > 0 ? <span className="module-card__meta">Version {tagMeta.expectedVersion}</span> : null}
                                </div>
                            </form>
                        </article>
                    </div>

                    <form className="auth-panel__form" onSubmit={(event) => {
                        void postForm.handleSubmit(onSubmit)(event);
                    }}>
                        <label className="field">
                            <span className="field__label">Title</span>
                            <input
                                className="field__input"
                                disabled={!canManage || pending}
                                {...postForm.register('title')}
                                aria-invalid={!!postForm.formState.errors.title}
                                aria-describedby={postForm.formState.errors.title ? 'post-title-error' : undefined}
                            />
                        </label>
                        {postForm.formState.errors.title && (
                            <span id="post-title-error" role="alert" className="message" data-tone="danger">{postForm.formState.errors.title.message}</span>
                        )}

                        <label className="field">
                            <span className="field__label">Slug</span>
                            <input
                                className="field__input"
                                disabled={!canManage || pending}
                                placeholder="generated from title when blank"
                                {...postForm.register('slug')}
                            />
                        </label>

                        <label className="field">
                            <span className="field__label">Category</span>
                            <input
                                className="field__input"
                                disabled={!canManage || pending}
                                placeholder="platform-strategy"
                                {...postForm.register('categorySlug')}
                            />
                        </label>

                        {categories.length > 0 ? <p className="module-card__meta">Available categories: {categories.map((category) => category.slug).join(', ')}</p> : null}

                        <label className="field">
                            <span className="field__label">Summary</span>
                            <textarea
                                className="field__input"
                                disabled={!canManage || pending}
                                rows={3}
                                {...postForm.register('summary')}
                                aria-invalid={!!postForm.formState.errors.summary}
                                aria-describedby={postForm.formState.errors.summary ? 'post-summary-error' : undefined}
                            />
                        </label>
                        {postForm.formState.errors.summary && (
                            <span id="post-summary-error" role="alert" className="message" data-tone="danger">{postForm.formState.errors.summary.message}</span>
                        )}

                        <label className="field">
                            <span className="field__label">Body</span>
                            <textarea
                                className="field__input"
                                disabled={!canManage || pending}
                                rows={10}
                                {...postForm.register('body')}
                                aria-invalid={!!postForm.formState.errors.body}
                                aria-describedby={postForm.formState.errors.body ? 'post-body-error' : undefined}
                            />
                        </label>
                        {postForm.formState.errors.body && (
                            <span id="post-body-error" role="alert" className="message" data-tone="danger">{postForm.formState.errors.body.message}</span>
                        )}

                        <label className="field">
                            <span className="field__label">Tags</span>
                            <input
                                className="field__input"
                                disabled={!canManage || pending}
                                placeholder="architecture, publishing"
                                {...postForm.register('tagNamesText')}
                            />
                        </label>

                        {tags.length > 0 ? <p className="module-card__meta">Available tags: {tags.map((tag) => tag.slug).join(', ')}</p> : null}

                        <label className="field">
                            <span className="field__label">Share targets</span>
                            <input
                                className="field__input"
                                disabled={!canManage || pending}
                                placeholder="linkedin, email newsletter"
                                {...postForm.register('shareTargetsText')}
                            />
                        </label>

                        <label className="field">
                            <span className="field__label">SEO title</span>
                            <input
                                className="field__input"
                                disabled={!canManage || pending}
                                {...postForm.register('seoTitle')}
                            />
                        </label>

                        <label className="field">
                            <span className="field__label">SEO description</span>
                            <textarea
                                className="field__input"
                                disabled={!canManage || pending}
                                rows={3}
                                {...postForm.register('seoDescription')}
                            />
                        </label>

                        <label className="field">
                            <span className="field__label">SEO keywords</span>
                            <input
                                className="field__input"
                                disabled={!canManage || pending}
                                placeholder="governed, blog"
                                {...postForm.register('seoKeywords')}
                            />
                        </label>

                        <label className="field">
                            <span className="field__label">Featured</span>
                            <input
                                disabled={!canManage || pending}
                                type="checkbox"
                                {...postForm.register('featured')}
                            />
                        </label>

                        <div className="module-card__actions">
                            <button className="button" disabled={!canManage || pending} type="submit">{postMeta.postId ? 'Save draft' : 'Create draft'}</button>
                            <button className="button-ghost" onClick={() => { postForm.reset(emptyPostFormData); scheduleForm.reset(emptyScheduleFormData); setPostMeta(emptyPostMeta); }} type="button">Clear</button>
                            {postMeta.postId ? <span className="module-card__meta">Version {postMeta.expectedVersion}</span> : null}
                        </div>
                    </form>

                    {postMeta.postId ? (
                        <article className="feature-panel">
                            <div className="feature-panel__header">
                                <div>
                                    <h4 className="feature-panel__title">Lifecycle schedule</h4>
                                    <p className="feature-panel__copy">Schedule publish and unpublish transitions using your current preferred time zone.</p>
                                </div>
                                <span className="pill">draft {postForm.getValues('slug') || 'unsaved'}</span>
                            </div>

                            <form className="auth-panel__form" onSubmit={(event) => {
                                void scheduleForm.handleSubmit(onScheduleSubmit)(event);
                            }}>
                                <label className="field">
                                    <span className="field__label">Publish date</span>
                                    <input
                                        className="field__input"
                                        disabled={!canManage || pending}
                                        type="date"
                                        {...scheduleForm.register('publishLocalDate')}
                                    />
                                </label>

                                <label className="field">
                                    <span className="field__label">Publish time</span>
                                    <input
                                        className="field__input"
                                        disabled={!canManage || pending}
                                        type="time"
                                        {...scheduleForm.register('publishLocalTime')}
                                    />
                                </label>

                                <label className="field">
                                    <span className="field__label">Unpublish date</span>
                                    <input
                                        className="field__input"
                                        disabled={!canManage || pending}
                                        type="date"
                                        {...scheduleForm.register('unpublishLocalDate')}
                                    />
                                </label>

                                <label className="field">
                                    <span className="field__label">Unpublish time</span>
                                    <input
                                        className="field__input"
                                        disabled={!canManage || pending}
                                        type="time"
                                        {...scheduleForm.register('unpublishLocalTime')}
                                    />
                                </label>

                                <div className="module-card__actions">
                                    <button className="button" disabled={!canManage || pending} type="submit">Save schedule</button>
                                    <button className="button-ghost" disabled={!canManage || pending} onClick={() => void onClearSchedule()} type="button">Clear schedule</button>
                                </div>
                            </form>
                        </article>
                    ) : null}

                    <div className="shell-list">
                        {managementPosts.map((post) => {
                            const scheduledPublish = describeSchedule('Publish', post.scheduledPublish);
                            const scheduledUnpublish = describeSchedule('Unpublish', post.scheduledUnpublish);

                            return (
                                <article className="feature-panel" key={post.postId}>
                                    <div className="feature-panel__header">
                                        <div>
                                            <h4 className="feature-panel__title">{post.title}</h4>
                                            <p className="module-card__meta">{post.summary}</p>
                                        </div>
                                        <span className="pill" data-tone={post.status === 'published' ? 'healthy' : 'warning'}>{post.status}</span>
                                    </div>

                                    <p className="module-card__meta">{post.categorySlug ? `category/${post.categorySlug}` : 'uncategorized'} · {post.viewCount} views · revision {post.revisionNumber}</p>
                                    {post.tagNames.length > 0 ? <p className="module-card__meta">Tags: {post.tagNames.join(', ')}</p> : null}
                                    {post.shareTargets.length > 0 ? <p className="module-card__meta">Share to: {post.shareTargets.join(', ')}</p> : null}
                                    {scheduledPublish ? <p className="module-card__meta">{scheduledPublish}</p> : null}
                                    {scheduledUnpublish ? <p className="module-card__meta">{scheduledUnpublish}</p> : null}

                                    <div className="module-card__actions">
                                        <button className="button-ghost" onClick={() => { loadPostIntoForms(post); setSelectedSlug(post.slug); }} type="button">Edit</button>
                                        <button className="button" disabled={!canManage} onClick={() => void onStatusChange(post, 'published')} type="button">Publish</button>
                                        <button className="button-ghost" disabled={!canManage} onClick={() => void onStatusChange(post, 'draft')} type="button">Move to draft</button>
                                        <button className="button-ghost" disabled={!canManage} onClick={() => void onStatusChange(post, 'archived')} type="button">Archive</button>
                                    </div>
                                </article>
                            );
                        })}
                    </div>
                </section>
            ) : null}
        </div>
    );
}
