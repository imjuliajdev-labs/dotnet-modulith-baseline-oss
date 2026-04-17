export const BlogSettingsTags = {
  fanout: [
    'BlogPosts',
    'BlogTaxonomy',
  ] as const,
  settings: 'BlogSettings',
} as const;

export function getBlogSettingsTagTypes() {
  return [BlogSettingsTags.settings, ...BlogSettingsTags.fanout] as const;
}

export type BlogSettingsTagType = ReturnType<typeof getBlogSettingsTagTypes>[number];

export function getBlogSettingsInvalidationTags() {
  return [BlogSettingsTags.settings, ...BlogSettingsTags.fanout] as const;
}

export function getBlogSettingsDependentReadTags() {
  return [
    'BlogPosts',
    'BlogTaxonomy',
  ] as const;
}
