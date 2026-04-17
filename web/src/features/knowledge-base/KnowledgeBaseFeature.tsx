import { useEffect, useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useGetCurrentActorQuery } from '../../shell/auth/authApi';
import { getErrorMessage, isConflictError, isUnauthorizedError } from '../../shared/lib/queryErrors';
import type {
  KnowledgeBasePublicSettingsResponse,
  KnowledgeEntryResponse,
} from '../../shared/api/base/contracts';
import {
  useCreateKnowledgeEntryMutation,
  useGetKnowledgeBaseManagementSettingsQuery,
  useGetKnowledgeBasePublicSettingsQuery,
  useListKnowledgeEntriesForManagementQuery,
  useListPublishedKnowledgeEntriesQuery,
  usePublishKnowledgeEntryMutation,
  useSetKnowledgeEntryStatusMutation,
  useUpdateKnowledgeEntryMutation,
  useUpdateKnowledgeBaseSettingsMutation,
} from './knowledgeBaseApi';

const entrySchema = z.object({
  body: z.string().min(1, 'Answer is required'),
  category: z.string().min(1, 'Category is required'),
  featured: z.boolean(),
  slug: z.string(),
  sortOrder: z.number(),
  title: z.string().min(1, 'Title is required'),
});

type EntryFormData = z.infer<typeof entrySchema>;

const settingsSchema = z.object({
  managementPreviewLimit: z.number().min(1, 'Must be at least 1').max(24, 'Must be at most 24'),
  publicExperienceBlurb: z.string().min(1, 'Public intro is required'),
  publicExperienceTitle: z.string().min(1, 'Public title is required'),
  searchEnabled: z.boolean(),
  searchPlaceholder: z.string().min(1, 'Search placeholder is required'),
});

type SettingsFormData = z.infer<typeof settingsSchema>;

function createDefaultPublicSettings(): KnowledgeBasePublicSettingsResponse {
  return {
    publicExperienceBlurb: 'A generic knowledge surface for FAQ-style publishing today and broader help content later.',
    publicExperienceTitle: 'Knowledge Base',
    searchEnabled: true,
    searchPlaceholder: 'Search by title, answer, or category',
  };
}

export function KnowledgeBaseFeature() {
  const sessionQuery = useGetCurrentActorQuery();
  const session = isUnauthorizedError(sessionQuery.error) ? null : sessionQuery.data ?? null;
  const hasAdminAccess = session?.roles?.includes('Admin') ?? false;
  const canReadManagement = hasAdminAccess;
  const canManage = hasAdminAccess;
  const publicSettingsQuery = useGetKnowledgeBasePublicSettingsQuery();
  const managementSettingsQuery = useGetKnowledgeBaseManagementSettingsQuery(undefined, { skip: !canReadManagement });
  const publishedQuery = useListPublishedKnowledgeEntriesQuery();
  const managementQuery = useListKnowledgeEntriesForManagementQuery(undefined, { skip: !canReadManagement });
  const [createKnowledgeEntry, createKnowledgeEntryState] = useCreateKnowledgeEntryMutation();
  const [publishKnowledgeEntry] = usePublishKnowledgeEntryMutation();
  const [setKnowledgeEntryStatus] = useSetKnowledgeEntryStatusMutation();
  const [updateKnowledgeEntry, updateKnowledgeEntryState] = useUpdateKnowledgeEntryMutation();
  const [updateKnowledgeBaseSettings, updateKnowledgeBaseSettingsState] = useUpdateKnowledgeBaseSettingsMutation();
  const [entryMeta, setEntryMeta] = useState<{ entryId: string | null; expectedVersion: number }>({ entryId: null, expectedVersion: 0 });
  const [settingsExpectedVersion, setSettingsExpectedVersion] = useState<number | null>(null);
  const [mutationError, setMutationError] = useState<string | null>(null);
  const [settingsMutationError, setSettingsMutationError] = useState<string | null>(null);
  const [settingsSaved, setSettingsSaved] = useState(false);
  const [settingsDirty, setSettingsDirty] = useState(false);
  const [entrySaved, setEntrySaved] = useState(false);
  const [queryText, setQueryText] = useState('');

  const entryForm = useForm<EntryFormData>({
    defaultValues: { body: '', category: 'General', featured: false, slug: '', sortOrder: 0, title: '' },
    resolver: zodResolver(entrySchema),
  });

  const settingsForm = useForm<SettingsFormData>({
    defaultValues: { managementPreviewLimit: 12, publicExperienceBlurb: '', publicExperienceTitle: '', searchEnabled: true, searchPlaceholder: '' },
    resolver: zodResolver(settingsSchema),
  });

  useEffect(() => {
    if (managementSettingsQuery.data) {
      const s = managementSettingsQuery.data;
      settingsForm.reset({
        managementPreviewLimit: Number(s.managementPreviewLimit),
        publicExperienceBlurb: s.publicExperienceBlurb,
        publicExperienceTitle: s.publicExperienceTitle,
        searchEnabled: s.searchEnabled,
        searchPlaceholder: s.searchPlaceholder,
      });
      setSettingsExpectedVersion(Number(s.version));
      setSettingsDirty(false);
    }
  }, [managementSettingsQuery.data, settingsForm]);

  const publicSettings = publicSettingsQuery.data ?? createDefaultPublicSettings();
  const publishedEntries = publishedQuery.data?.entries ?? [];
  const normalizedQuery = queryText.trim().toLowerCase();
  const visibleEntries = normalizedQuery.length === 0
    ? publishedEntries
    : publishedEntries.filter((entry) =>
      entry.title.toLowerCase().includes(normalizedQuery)
      || entry.body.toLowerCase().includes(normalizedQuery)
      || entry.category.toLowerCase().includes(normalizedQuery));

  const managementPreviewLimit = Number(managementSettingsQuery.data?.managementPreviewLimit ?? 12);
  const managementEntries = (managementQuery.data?.entries ?? []).slice(0, managementPreviewLimit);
  const pending = createKnowledgeEntryState.isLoading || updateKnowledgeEntryState.isLoading;
  const settingsPending = updateKnowledgeBaseSettingsState.isLoading;

  function loadEntryIntoForm(entry: KnowledgeEntryResponse) {
    entryForm.reset({
      body: entry.body,
      category: entry.category,
      featured: entry.featured,
      slug: entry.slug,
      sortOrder: Number(entry.sortOrder),
      title: entry.title,
    });
    setEntryMeta({ entryId: entry.entryId, expectedVersion: Number(entry.version) });
  }

  async function onSubmit(data: EntryFormData) {
    setMutationError(null);

    try {
      const payload = {
        body: data.body,
        category: data.category,
        featured: data.featured,
        slug: data.slug.trim().length === 0 ? null : data.slug,
        sortOrder: data.sortOrder,
        title: data.title,
      };

      const response = entryMeta.entryId
        ? await updateKnowledgeEntry({
          body: {
            ...payload,
            expectedVersion: entryMeta.expectedVersion,
          },
          entryId: entryMeta.entryId,
        }).unwrap()
        : await createKnowledgeEntry(payload).unwrap();

      loadEntryIntoForm(response);
      setEntrySaved(true);
      setTimeout(() => setEntrySaved(false), 3000);
    }
    catch (error) {
      setMutationError(getErrorMessage(error));
    }
  }

  async function onStatusChange(entry: KnowledgeEntryResponse, status: 'archived' | 'draft' | 'published') {
    setMutationError(null);

    try {
      const response = status === 'published'
        ? await publishKnowledgeEntry({
          body: {
            expectedVersion: entry.version,
          },
          entryId: entry.entryId,
        }).unwrap()
        : await setKnowledgeEntryStatus({
          body: {
            expectedVersion: entry.version,
            status,
          },
          entryId: entry.entryId,
        }).unwrap();

      if (entryMeta.entryId === response.entryId) {
        loadEntryIntoForm(response);
      }
    }
    catch (error) {
      setMutationError(getErrorMessage(error));
    }
  }

  async function onSettingsSubmit(data: SettingsFormData) {
    if (settingsExpectedVersion === null) {
      return;
    }

    setSettingsMutationError(null);

    try {
      const response = await updateKnowledgeBaseSettings({
        expectedVersion: settingsExpectedVersion,
        managementPreviewLimit: data.managementPreviewLimit,
        publicExperienceBlurb: data.publicExperienceBlurb,
        publicExperienceTitle: data.publicExperienceTitle,
        searchEnabled: data.searchEnabled,
        searchPlaceholder: data.searchPlaceholder,
      }).unwrap();

      settingsForm.reset({
        managementPreviewLimit: Number(response.managementPreviewLimit),
        publicExperienceBlurb: response.publicExperienceBlurb,
        publicExperienceTitle: response.publicExperienceTitle,
        searchEnabled: response.searchEnabled,
        searchPlaceholder: response.searchPlaceholder,
      });
      setSettingsExpectedVersion(Number(response.version));
      setSettingsSaved(true);
      setSettingsDirty(false);
      setTimeout(() => setSettingsSaved(false), 3000);
    }
    catch (error) {
      if (isConflictError(error)) {
        setSettingsMutationError('Settings were updated by another operator. Refreshing to latest version.');
        void managementSettingsQuery.refetch();
      } else if (isUnauthorizedError(error)) {
        setSettingsMutationError('Your session has expired. Please sign in again to save settings.');
      } else {
        setSettingsMutationError(getErrorMessage(error));
      }
    }
  }

  return (
    <div className="dashboard-grid">
      <section className="feature-panel dashboard-grid__full">
        <div className="feature-panel__header">
          <div>
            <h2 className="feature-panel__title">{publicSettings.publicExperienceTitle}</h2>
            <p className="feature-panel__copy">{publicSettings.publicExperienceBlurb}</p>
          </div>
          <span className="pill" data-tone={canManage ? 'healthy' : 'warning'}>{canManage ? 'operator mode' : 'public read'}</span>
        </div>

        {publicSettings.searchEnabled ? (
          <label className="field">
            <span className="field__label">Search entries</span>
            <input className="field__input" onChange={(event) => setQueryText(event.target.value)} placeholder={publicSettings.searchPlaceholder} value={queryText} />
          </label>
        ) : null}

        {publishedQuery.isLoading && !publishedQuery.data ? <p className="message">Loading the knowledge base.</p> : null}

        <div className="shell-list">
          {visibleEntries.map((entry) => (
            <details className="feature-panel" key={entry.entryId} open={entry.featured}>
              <summary className="feature-panel__header">
                <div>
                  <h3 className="feature-panel__title">{entry.title}</h3>
                  <p className="module-card__meta">{entry.category}</p>
                </div>
                <span className="pill" data-tone={entry.featured ? 'healthy' : 'warning'}>{entry.featured ? 'featured' : entry.status}</span>
              </summary>
              <p className="feature-panel__copy">{entry.body}</p>
            </details>
          ))}
        </div>

        {!publishedQuery.isLoading && visibleEntries.length === 0 ? <p className="message">No knowledge entries matched the current search.</p> : null}
      </section>

      {canReadManagement ? (
        <section className="feature-panel dashboard-grid__full">
          <div className="feature-panel__header">
            <div>
              <h3 className="feature-panel__title">Operator controls</h3>
              <p className="feature-panel__copy">Manage live settings plus draft, published, and archived entries without leaving the module feature.</p>
            </div>
            <span className="pill" data-tone={canManage ? 'healthy' : 'warning'}>{canManage ? 'manage granted' : 'read only'}</span>
          </div>

          {settingsMutationError ? <p className="message" data-tone="danger">{settingsMutationError}</p> : null}
          {mutationError ? <p className="message" data-tone="danger">{mutationError}</p> : null}

          {settingsExpectedVersion !== null ? (
            <form className="auth-panel__form" onSubmit={(event) => {
              void settingsForm.handleSubmit(onSettingsSubmit)(event);
            }}>
              <label className="field">
                <span className="field__label">Public title</span>
                <input
                  className="field__input"
                  disabled={!canManage || settingsPending}
                  {...settingsForm.register('publicExperienceTitle', { onChange: () => setSettingsDirty(true) })}
                  aria-invalid={!!settingsForm.formState.errors.publicExperienceTitle}
                  aria-describedby={settingsForm.formState.errors.publicExperienceTitle ? 'settings-title-error' : undefined}
                />
              </label>
              {settingsForm.formState.errors.publicExperienceTitle && (
                <span id="settings-title-error" role="alert" className="message" data-tone="danger">{settingsForm.formState.errors.publicExperienceTitle.message}</span>
              )}

              <label className="field">
                <span className="field__label">Public intro</span>
                <textarea
                  className="field__input"
                  disabled={!canManage || settingsPending}
                  rows={3}
                  {...settingsForm.register('publicExperienceBlurb', { onChange: () => setSettingsDirty(true) })}
                  aria-invalid={!!settingsForm.formState.errors.publicExperienceBlurb}
                  aria-describedby={settingsForm.formState.errors.publicExperienceBlurb ? 'settings-blurb-error' : undefined}
                />
              </label>
              {settingsForm.formState.errors.publicExperienceBlurb && (
                <span id="settings-blurb-error" role="alert" className="message" data-tone="danger">{settingsForm.formState.errors.publicExperienceBlurb.message}</span>
              )}

              <label className="field">
                <span className="field__label">Search placeholder</span>
                <input
                  className="field__input"
                  disabled={!canManage || settingsPending}
                  {...settingsForm.register('searchPlaceholder', { onChange: () => setSettingsDirty(true) })}
                  aria-invalid={!!settingsForm.formState.errors.searchPlaceholder}
                  aria-describedby={settingsForm.formState.errors.searchPlaceholder ? 'settings-placeholder-error' : undefined}
                />
              </label>
              {settingsForm.formState.errors.searchPlaceholder && (
                <span id="settings-placeholder-error" role="alert" className="message" data-tone="danger">{settingsForm.formState.errors.searchPlaceholder.message}</span>
              )}

              <label className="field">
                <span className="field__label">Management preview limit</span>
                <input
                  className="field__input"
                  disabled={!canManage || settingsPending}
                  min={1}
                  max={24}
                  type="number"
                  {...settingsForm.register('managementPreviewLimit', { onChange: () => setSettingsDirty(true), valueAsNumber: true })}
                  aria-invalid={!!settingsForm.formState.errors.managementPreviewLimit}
                  aria-describedby={settingsForm.formState.errors.managementPreviewLimit ? 'settings-limit-error' : undefined}
                />
              </label>
              {settingsForm.formState.errors.managementPreviewLimit && (
                <span id="settings-limit-error" role="alert" className="message" data-tone="danger">{settingsForm.formState.errors.managementPreviewLimit.message}</span>
              )}

              <label className="field">
                <span className="field__label">Enable public search</span>
                <input
                  disabled={!canManage || settingsPending}
                  type="checkbox"
                  {...settingsForm.register('searchEnabled', { onChange: () => setSettingsDirty(true) })}
                />
              </label>

              {!settingsForm.watch('searchEnabled') ? (
                <p className="message" data-tone="warning">Public search is disabled. Visitors will not be able to search knowledge entries.</p>
              ) : null}

              <div className="module-card__actions">
                <button className="button" disabled={!canManage || settingsPending} type="submit">Save settings</button>
                <button className="button-ghost" disabled={!settingsDirty || settingsPending} type="button" onClick={() => {
                  if (managementSettingsQuery.data) {
                    const s = managementSettingsQuery.data;
                    settingsForm.reset({
                      managementPreviewLimit: Number(s.managementPreviewLimit),
                      publicExperienceBlurb: s.publicExperienceBlurb,
                      publicExperienceTitle: s.publicExperienceTitle,
                      searchEnabled: s.searchEnabled,
                      searchPlaceholder: s.searchPlaceholder,
                    });
                    setSettingsExpectedVersion(Number(s.version));
                    setSettingsDirty(false);
                  }
                }}>Reset</button>
                <span className="module-card__meta">Version {settingsExpectedVersion}{settingsDirty ? ' (unsaved changes)' : ''}</span>
              </div>
              {settingsSaved ? <span className="message" data-tone="healthy">Settings saved successfully.</span> : null}
            </form>
          ) : null}

          <form className="auth-panel__form" onSubmit={(event) => {
            void entryForm.handleSubmit(onSubmit)(event);
          }}>
            <label className="field">
              <span className="field__label">Title</span>
              <input
                className="field__input"
                disabled={!canManage || pending}
                {...entryForm.register('title')}
                aria-invalid={!!entryForm.formState.errors.title}
                aria-describedby={entryForm.formState.errors.title ? 'entry-title-error' : undefined}
              />
            </label>
            {entryForm.formState.errors.title && (
              <span id="entry-title-error" role="alert" className="message" data-tone="danger">{entryForm.formState.errors.title.message}</span>
            )}

            <label className="field">
              <span className="field__label">Slug</span>
              <input
                className="field__input"
                disabled={!canManage || pending}
                placeholder="Auto-generated when blank"
                {...entryForm.register('slug')}
              />
            </label>

            <label className="field">
              <span className="field__label">Category</span>
              <input
                className="field__input"
                disabled={!canManage || pending}
                {...entryForm.register('category')}
                aria-invalid={!!entryForm.formState.errors.category}
                aria-describedby={entryForm.formState.errors.category ? 'entry-category-error' : undefined}
              />
            </label>
            {entryForm.formState.errors.category && (
              <span id="entry-category-error" role="alert" className="message" data-tone="danger">{entryForm.formState.errors.category.message}</span>
            )}

            <label className="field">
              <span className="field__label">Answer</span>
              <textarea
                className="field__input"
                disabled={!canManage || pending}
                rows={5}
                {...entryForm.register('body')}
                aria-invalid={!!entryForm.formState.errors.body}
                aria-describedby={entryForm.formState.errors.body ? 'entry-body-error' : undefined}
              />
            </label>
            {entryForm.formState.errors.body && (
              <span id="entry-body-error" role="alert" className="message" data-tone="danger">{entryForm.formState.errors.body.message}</span>
            )}

            <label className="field">
              <span className="field__label">Sort order</span>
              <input
                className="field__input"
                disabled={!canManage || pending}
                type="number"
                {...entryForm.register('sortOrder', { valueAsNumber: true })}
              />
            </label>

            <label className="field">
              <span className="field__label">Featured</span>
              <input
                disabled={!canManage || pending}
                type="checkbox"
                {...entryForm.register('featured')}
              />
            </label>

            <div className="module-card__actions">
              <button className="button" disabled={!canManage || pending} type="submit">{entryMeta.entryId ? 'Save entry' : 'Create draft'}</button>
              <button className="button-ghost" type="button" onClick={() => { entryForm.reset({ body: '', category: 'General', featured: false, slug: '', sortOrder: 0, title: '' }); setEntryMeta({ entryId: null, expectedVersion: 0 }); }}>New draft</button>
            </div>
            {entrySaved ? <span className="message" data-tone="healthy">Entry saved successfully.</span> : null}
          </form>

          <div className="module-grid">
            {managementEntries.map((entry) => (
              <article className="module-card" key={entry.entryId}>
                <div className="module-card__header">
                  <div>
                    <p className="module-card__meta">{entry.category}</p>
                    <h3 className="module-card__title">{entry.title}</h3>
                  </div>
                  <span className="pill" data-tone={entry.status === 'published' ? 'healthy' : entry.status === 'draft' ? 'warning' : 'danger'}>{entry.status}</span>
                </div>

                <div className="module-card__body">
                  <span>slug: {entry.slug}</span>
                  <span>version: {entry.version}</span>
                  <span>{entry.featured ? 'featured' : 'standard'}</span>
                </div>

                <div className="module-card__actions">
                  <button className="button-ghost" type="button" onClick={() => loadEntryIntoForm(entry)}>Edit</button>
                  <button className="button-ghost" disabled={!canManage} type="button" onClick={() => {
                    void onStatusChange(entry, 'draft');
                  }}>Return to draft</button>
                  <button className="button-ghost" disabled={!canManage} type="button" onClick={() => {
                    void onStatusChange(entry, 'archived');
                  }}>Archive</button>
                  <button className="button" disabled={!canManage} type="button" onClick={() => {
                    void onStatusChange(entry, 'published');
                  }}>Publish</button>
                </div>
              </article>
            ))}
          </div>

          {(managementQuery.data?.entries?.length ?? 0) > managementPreviewLimit ? (
            <p className="message">Showing the first {managementPreviewLimit} management entries from the live module setting.</p>
          ) : null}
        </section>
      ) : null}
    </div>
  );
}
