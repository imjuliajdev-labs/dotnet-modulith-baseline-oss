import { describe, expect, it } from 'vitest';
import {
    BlogSettingsTags,
    getBlogSettingsDependentReadTags,
    getBlogSettingsInvalidationTags,
    getBlogSettingsTagTypes,
} from '../../src/features/blog/BlogSettingsTags';

describe('BlogSettingsTags', () => {
    it('keeps the generated settings tag helpers aligned with the module spec', () => {
        expect(BlogSettingsTags.settings).toBe('BlogSettings');
        expect([...BlogSettingsTags.fanout]).toEqual([
            'BlogPosts',
            'BlogTaxonomy'
        ]);
        expect([...getBlogSettingsTagTypes()]).toEqual([
            'BlogSettings',
            'BlogPosts',
            'BlogTaxonomy'
        ]);
        expect([...getBlogSettingsInvalidationTags()]).toEqual([
            'BlogSettings',
            'BlogPosts',
            'BlogTaxonomy'
        ]);
        expect([...getBlogSettingsDependentReadTags()]).toEqual([
            'BlogPosts',
            'BlogTaxonomy'
        ]);
    });
});
