import { z } from 'zod';
import type { BlogPostResponse } from '../../shared/api/base/contracts';

export const blogPostSchema = z.object({
    body: z.string().min(1, 'Body is required'),
    categorySlug: z.string(),
    featured: z.boolean(),
    seoDescription: z.string(),
    seoKeywords: z.string(),
    seoTitle: z.string(),
    shareTargetsText: z.string(),
    slug: z.string(),
    summary: z.string().min(1, 'Summary is required'),
    tagNamesText: z.string(),
    title: z.string().min(1, 'Title is required'),
});

export type BlogPostFormData = z.infer<typeof blogPostSchema>;

export const categorySchema = z.object({
    description: z.string(),
    name: z.string().min(1, 'Category name is required'),
    slug: z.string(),
});

export type CategoryFormData = z.infer<typeof categorySchema>;

export const tagSchema = z.object({
    description: z.string(),
    displayName: z.string().min(1, 'Tag name is required'),
    slug: z.string(),
});

export type TagFormData = z.infer<typeof tagSchema>;

export const scheduleSchema = z.object({
    publishLocalDate: z.string(),
    publishLocalTime: z.string(),
    unpublishLocalDate: z.string(),
    unpublishLocalTime: z.string(),
});

export type ScheduleFormData = z.infer<typeof scheduleSchema>;

export type BlogPostMeta = {
    expectedVersion: number;
    postId: string | null;
};

export type CategoryMeta = {
    expectedVersion: number;
    slug: string;
};

export type TagMeta = {
    expectedVersion: number;
    slug: string;
};

export type FlashMessage = {
    text: string;
    tone: 'healthy' | 'warning';
};

export const emptyPostFormData: BlogPostFormData = {
    body: '',
    categorySlug: '',
    featured: false,
    seoDescription: '',
    seoKeywords: '',
    seoTitle: '',
    shareTargetsText: '',
    slug: '',
    summary: '',
    tagNamesText: '',
    title: '',
};

export const emptyPostMeta: BlogPostMeta = { expectedVersion: 0, postId: null };

export const emptyScheduleFormData: ScheduleFormData = {
    publishLocalDate: '',
    publishLocalTime: '',
    unpublishLocalDate: '',
    unpublishLocalTime: '',
};

export const emptyCategoryFormData: CategoryFormData = { description: '', name: '', slug: '' };
export const emptyCategoryMeta: CategoryMeta = { expectedVersion: 0, slug: '' };

export const emptyTagFormData: TagFormData = { description: '', displayName: '', slug: '' };
export const emptyTagMeta: TagMeta = { expectedVersion: 0, slug: '' };

export function joinValues(values: readonly string[] | undefined | null): string {
    return values?.join(', ') ?? '';
}

export function parseDelimitedValues(value: string): string[] {
    return value
        .split(',')
        .map((item) => item.trim())
        .filter((item) => item.length > 0);
}

export function toNullableTrimmed(value: string): string | null {
    const trimmed = value.trim();
    return trimmed.length === 0 ? null : trimmed;
}

export function toScheduleInputValue(value: string | null | undefined): string {
    if (!value) {
        return '';
    }

    return value.slice(0, 5);
}

export function describeSchedule(label: string, publication: BlogPostResponse['scheduledPublish']): string | null {
    if (!publication) {
        return null;
    }

    return `${label}: ${publication.scheduledLocalDate} ${toScheduleInputValue(publication.scheduledLocalTime)} ${publication.timeZoneId} (${publication.localTimeResolution})`;
}

export function postToFormData(post: BlogPostResponse): BlogPostFormData {
    return {
        body: post.body,
        categorySlug: post.categorySlug ?? '',
        featured: post.featured,
        seoDescription: post.seoMetadata.description ?? '',
        seoKeywords: post.seoMetadata.keywords ?? '',
        seoTitle: post.seoMetadata.title ?? '',
        shareTargetsText: joinValues(post.shareTargets),
        slug: post.slug,
        summary: post.summary,
        tagNamesText: joinValues(post.tagNames),
        title: post.title,
    };
}

export function postToScheduleData(post: BlogPostResponse): ScheduleFormData {
    return {
        publishLocalDate: post.scheduledPublish?.scheduledLocalDate ?? '',
        publishLocalTime: toScheduleInputValue(post.scheduledPublish?.scheduledLocalTime),
        unpublishLocalDate: post.scheduledUnpublish?.scheduledLocalDate ?? '',
        unpublishLocalTime: toScheduleInputValue(post.scheduledUnpublish?.scheduledLocalTime),
    };
}

export function postToMeta(post: BlogPostResponse): BlogPostMeta {
    return { expectedVersion: Number(post.version), postId: post.postId };
}
