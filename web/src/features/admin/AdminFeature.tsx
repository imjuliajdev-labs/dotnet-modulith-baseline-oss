import { startTransition, useEffect, useEffectEvent, useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useGetCurrentActorQuery } from '../../shell/auth/authApi';
import { getErrorMessage, isUnauthorizedError } from '../../shared/lib/queryErrors';

const createIdentityUserSchema = z.object({
  userName: z.string().trim().min(1, 'User name is required'),
  displayName: z.string().trim().min(1, 'Display name is required'),
  password: z.string().min(12, 'Password must be at least 12 characters'),
  roles: z.string().trim().min(1, 'At least one role is required'),
  preferredTimeZoneId: z.string().trim().min(1, 'Time zone is required'),
});

type CreateIdentityUserFormData = z.infer<typeof createIdentityUserSchema>;

const createIdentityUserDefaults: CreateIdentityUserFormData = {
  userName: 'operator',
  displayName: 'Baseline Operator',
  password: 'LocalOnly!123',
  roles: 'Admin',
  preferredTimeZoneId: 'Etc/UTC',
};
import type { AdminAnnouncementReadModel, IdentityUserAccountResponse } from '../../shared/api/base/contracts';
import {
  useCreateIdentityUserMutation,
  useListAdminAnnouncementsQuery,
  useListAdminGuidanceQuery,
  useListIdentityUsersQuery,
  useResetIdentityUserPasswordMutation,
  useRevokeIdentityUserSessionsMutation,
  useUpdateIdentityUserRolesMutation,
  useUpdateIdentityUserStatusMutation,
} from './adminApi';
import {
  ADMIN_ANNOUNCEMENT_PROJECTED_CHANNEL,
  createAdminRealtimeAdapter,
  upsertAdminAnnouncement,
} from './adminRealtime';
import {
  formatAnnouncementSource,
  guidanceTone,
  hasGuidanceEntries,
  labelForRealtimeState,
  toneForRealtimeState,
  type RealtimeState,
} from './adminFormatting';

export function AdminFeature() {
  const sessionQuery = useGetCurrentActorQuery();
  const session = isUnauthorizedError(sessionQuery.error) ? null : sessionQuery.data ?? null;
  const hasAdminAccess = session?.roles?.includes('Admin') ?? false;
  const canReadIdentityUsers = hasAdminAccess;
  const canManageIdentityUsers = hasAdminAccess;
  const announcementsQuery = useListAdminAnnouncementsQuery(undefined, { skip: !hasAdminAccess });
  const guidanceQuery = useListAdminGuidanceQuery(undefined, { skip: !hasAdminAccess });
  const identityUsersQuery = useListIdentityUsersQuery(undefined, { skip: !canReadIdentityUsers });
  const [createIdentityUser, createIdentityUserState] = useCreateIdentityUserMutation();
  const [resetIdentityUserPassword] = useResetIdentityUserPasswordMutation();
  const [revokeIdentityUserSessions] = useRevokeIdentityUserSessionsMutation();
  const [updateIdentityUserRoles] = useUpdateIdentityUserRolesMutation();
  const [updateIdentityUserStatus] = useUpdateIdentityUserStatusMutation();
  const [realtimeAdapter] = useState(createAdminRealtimeAdapter);
  const [announcements, setAnnouncements] = useState<AdminAnnouncementReadModel[]>([]);
  const createIdentityUserForm = useForm<CreateIdentityUserFormData>({
    defaultValues: createIdentityUserDefaults,
    resolver: zodResolver(createIdentityUserSchema),
  });
  const [identityMutationError, setIdentityMutationError] = useState<string | null>(null);
  const [lastRealtimeUtc, setLastRealtimeUtc] = useState<string | null>(null);
  const [passwordDrafts, setPasswordDrafts] = useState<Record<string, string>>({});
  const [roleDrafts, setRoleDrafts] = useState<Record<string, string>>({});
  const [pendingIdentityActorId, setPendingIdentityActorId] = useState<string | null>(null);
  const [realtimeState, setRealtimeState] = useState<RealtimeState>('idle');

  useEffect(() => {
    const users = identityUsersQuery.data?.users;
    if (!users) {
      return;
    }

    setRoleDrafts((current) => {
      const next = { ...current };
      for (const user of users) {
        if (!(user.actorId in next)) {
          next[user.actorId] = (user.roles ?? []).join(', ');
        }
      }

      return next;
    });
  }, [identityUsersQuery.data?.users]);

  useEffect(() => {
    const nextAnnouncements = announcementsQuery.data?.announcements;
    if (!nextAnnouncements) {
      return;
    }

    startTransition(() => {
      setAnnouncements((current) => {
        if (
          current.length === nextAnnouncements.length
          && current.every((announcement, index) => {
            const candidate = nextAnnouncements[index];
            return candidate !== undefined
              && announcement.announcementId === candidate.announcementId
              && announcement.publishedUtc === candidate.publishedUtc;
          })
        ) {
          return current;
        }

        return nextAnnouncements;
      });
    });
  }, [announcementsQuery.data?.announcements]);

  const handleProjectedAnnouncement = useEffectEvent((announcement: AdminAnnouncementReadModel) => {
    startTransition(() => {
      setAnnouncements((current) => upsertAdminAnnouncement(current, announcement));
      setLastRealtimeUtc(new Date().toISOString());
      setRealtimeState('live');
    });
  });

  function parseRoles(value: string) {
    return value
      .split(/[\n,]+/)
      .map((role) => role.trim())
      .filter((role) => role.length > 0);
  }

  async function onCreateIdentityUser(data: CreateIdentityUserFormData) {
    setIdentityMutationError(null);

    try {
      await createIdentityUser({
        displayName: data.displayName,
        enabled: true,
        password: data.password,
        roles: parseRoles(data.roles),
        preferredTimeZoneId: data.preferredTimeZoneId,
        userName: data.userName,
      }).unwrap();

      createIdentityUserForm.reset(createIdentityUserDefaults);
    }
    catch (error) {
      setIdentityMutationError(getErrorMessage(error));
    }
  }

  async function saveRoles(user: IdentityUserAccountResponse) {
    setIdentityMutationError(null);
    startTransition(() => {
      setPendingIdentityActorId(user.actorId);
    });

    try {
      const response = await updateIdentityUserRoles({
        actorId: user.actorId,
        body: {
          roles: parseRoles(roleDrafts[user.actorId] ?? (user.roles ?? []).join(', ')),
        },
      }).unwrap();

      setRoleDrafts((current) => ({
        ...current,
        [user.actorId]: (response.roles ?? []).join(', '),
      }));
    }
    catch (error) {
      setIdentityMutationError(getErrorMessage(error));
    }
    finally {
      setPendingIdentityActorId(null);
    }
  }

  async function toggleStatus(user: IdentityUserAccountResponse) {
    setIdentityMutationError(null);
    startTransition(() => {
      setPendingIdentityActorId(user.actorId);
    });

    try {
      await updateIdentityUserStatus({
        actorId: user.actorId,
        body: { enabled: !user.enabled },
      }).unwrap();
    }
    catch (error) {
      setIdentityMutationError(getErrorMessage(error));
    }
    finally {
      setPendingIdentityActorId(null);
    }
  }

  async function revokeSessions(user: IdentityUserAccountResponse) {
    setIdentityMutationError(null);
    startTransition(() => {
      setPendingIdentityActorId(user.actorId);
    });

    try {
      await revokeIdentityUserSessions({
        actorId: user.actorId,
      }).unwrap();
    }
    catch (error) {
      setIdentityMutationError(getErrorMessage(error));
    }
    finally {
      setPendingIdentityActorId(null);
    }
  }

  async function resetPassword(user: IdentityUserAccountResponse) {
    setIdentityMutationError(null);
    startTransition(() => {
      setPendingIdentityActorId(user.actorId);
    });

    try {
      const password = passwordDrafts[user.actorId] ?? '';
      await resetIdentityUserPassword({
        actorId: user.actorId,
        body: { password },
      }).unwrap();

      setPasswordDrafts((current) => ({
        ...current,
        [user.actorId]: '',
      }));
    }
    catch (error) {
      setIdentityMutationError(getErrorMessage(error));
    }
    finally {
      setPendingIdentityActorId(null);
    }
  }

  useEffect(() => {
    if (!hasAdminAccess) {
      startTransition(() => {
        setRealtimeState('idle');
      });
      return;
    }

    let disposed = false;
    startTransition(() => {
      setRealtimeState('connecting');
    });

    const unsubscribe = realtimeAdapter.subscribe<AdminAnnouncementReadModel>(
      ADMIN_ANNOUNCEMENT_PROJECTED_CHANNEL,
      (announcement) => {
        if (!disposed) {
          handleProjectedAnnouncement(announcement);
        }
      },
    );

    void realtimeAdapter.connect()
      .then(() => {
        if (!disposed) {
          startTransition(() => {
            setRealtimeState('live');
          });
        }
      })
      .catch(() => {
        if (!disposed) {
          startTransition(() => {
            setRealtimeState('degraded');
          });
        }
      });

    return () => {
      disposed = true;
      unsubscribe();
      void realtimeAdapter.disconnect();
    };
  }, [handleProjectedAnnouncement, hasAdminAccess, realtimeAdapter]);

  if (!hasAdminAccess) {
    return (
      <section className="feature-panel">
        <div className="feature-panel__header">
          <div>
            <h2 className="feature-panel__title">Admin flight deck</h2>
            <p className="feature-panel__copy">The Admin route stays role-aware even when someone navigates directly to it.</p>
          </div>
          <span className="pill" data-tone="warning">restricted</span>
        </div>

        <p className="message">This browser session does not currently hold the Admin role.</p>
      </section>
    );
  }

  return (
    <div className="platform-grid">
      <section className="feature-panel platform-grid__full">
        <div className="feature-panel__header">
          <div>
            <h2 className="feature-panel__title">Admin flight deck</h2>
            <p className="feature-panel__copy">Recent cross-module announcement projections load from the Admin read model and then stay current through the shared browser realtime channel.</p>
          </div>

          <div className="module-card__actions">
            <span className="pill" data-tone="healthy">access granted</span>
            <span className="pill" data-tone={toneForRealtimeState(realtimeState)}>{labelForRealtimeState(realtimeState)}</span>
          </div>
        </div>

        <ul className="shell-list">
          <li>
            <span>The initial feed is contract-first and comes from the Admin module's public read surface.</span>
            <span className="pill">query-backed</span>
          </li>
          <li>
            <span>Live projection completions arrive through the shared SignalR browser adapter instead of feature-local sockets.</span>
            <span className="pill">shared realtime</span>
          </li>
          <li>
            <span>Operator sessions remain cookie-authenticated; no privileged browser token storage is introduced.</span>
            <span className="pill">cookie auth</span>
          </li>
        </ul>

        {lastRealtimeUtc ? <p className="message">Last live projection: {new Date(lastRealtimeUtc).toLocaleTimeString()}.</p> : null}
        {announcementsQuery.error ? <p className="message" data-tone="danger">{getErrorMessage(announcementsQuery.error)}</p> : null}
      </section>

      <section className="feature-panel platform-grid__full">
        <div className="platform-section__header">
          <div>
            <h3 className="platform-section__title">Operator accounts</h3>
            <p className="platform-section__subcopy">Identity users are now manageable from the app instead of being limited to the bootstrap admin account.</p>
          </div>
          <span className="pill" data-tone={canManageIdentityUsers ? 'healthy' : canReadIdentityUsers ? 'warning' : 'danger'}>
            {canManageIdentityUsers ? 'manage granted' : canReadIdentityUsers ? 'read only' : 'restricted'}
          </span>
        </div>

        {identityMutationError ? <p className="message" data-tone="danger">{identityMutationError}</p> : null}

        {!canReadIdentityUsers ? (
          <p className="message">This session cannot read the identity user directory.</p>
        ) : (
          <>
            <p className="message">Passwords use a 12-character minimum with at least 4 unique characters.</p>

            <form
              className="auth-panel__form"
              onSubmit={(event) => {
                void createIdentityUserForm.handleSubmit(onCreateIdentityUser)(event);
              }}
            >
              <label className="field">
                <span className="field__label">User name</span>
                <input
                  className="field__input"
                  {...createIdentityUserForm.register('userName')}
                  aria-invalid={!!createIdentityUserForm.formState.errors.userName}
                  aria-describedby={createIdentityUserForm.formState.errors.userName ? 'create-identity-user-userName-error' : undefined}
                />
              </label>
              {createIdentityUserForm.formState.errors.userName && (
                <span id="create-identity-user-userName-error" role="alert" className="message" data-tone="danger">
                  {createIdentityUserForm.formState.errors.userName.message}
                </span>
              )}

              <label className="field">
                <span className="field__label">Display name</span>
                <input
                  className="field__input"
                  {...createIdentityUserForm.register('displayName')}
                  aria-invalid={!!createIdentityUserForm.formState.errors.displayName}
                  aria-describedby={createIdentityUserForm.formState.errors.displayName ? 'create-identity-user-displayName-error' : undefined}
                />
              </label>
              {createIdentityUserForm.formState.errors.displayName && (
                <span id="create-identity-user-displayName-error" role="alert" className="message" data-tone="danger">
                  {createIdentityUserForm.formState.errors.displayName.message}
                </span>
              )}

              <label className="field">
                <span className="field__label">Password</span>
                <input
                  className="field__input"
                  type="password"
                  {...createIdentityUserForm.register('password')}
                  aria-invalid={!!createIdentityUserForm.formState.errors.password}
                  aria-describedby={createIdentityUserForm.formState.errors.password ? 'create-identity-user-password-error' : undefined}
                />
              </label>
              {createIdentityUserForm.formState.errors.password && (
                <span id="create-identity-user-password-error" role="alert" className="message" data-tone="danger">
                  {createIdentityUserForm.formState.errors.password.message}
                </span>
              )}

              <label className="field">
                <span className="field__label">Roles</span>
                <input
                  className="field__input"
                  {...createIdentityUserForm.register('roles')}
                  aria-invalid={!!createIdentityUserForm.formState.errors.roles}
                  aria-describedby={createIdentityUserForm.formState.errors.roles ? 'create-identity-user-roles-error' : undefined}
                />
              </label>
              {createIdentityUserForm.formState.errors.roles && (
                <span id="create-identity-user-roles-error" role="alert" className="message" data-tone="danger">
                  {createIdentityUserForm.formState.errors.roles.message}
                </span>
              )}

              <label className="field">
                <span className="field__label">Preferred IANA time zone</span>
                <input
                  className="field__input"
                  {...createIdentityUserForm.register('preferredTimeZoneId')}
                  aria-invalid={!!createIdentityUserForm.formState.errors.preferredTimeZoneId}
                  aria-describedby={createIdentityUserForm.formState.errors.preferredTimeZoneId ? 'create-identity-user-preferredTimeZoneId-error' : undefined}
                />
              </label>
              {createIdentityUserForm.formState.errors.preferredTimeZoneId && (
                <span id="create-identity-user-preferredTimeZoneId-error" role="alert" className="message" data-tone="danger">
                  {createIdentityUserForm.formState.errors.preferredTimeZoneId.message}
                </span>
              )}

              <button className="button" disabled={!canManageIdentityUsers || createIdentityUserState.isLoading} type="submit">
                Create operator account
              </button>
            </form>

            {identityUsersQuery.isLoading && !identityUsersQuery.data ? <p className="message">Loading identity users.</p> : null}

            <div className="audit-grid">
              {identityUsersQuery.data?.users.map((user) => {
                const roleDraft = roleDrafts[user.actorId] ?? (user.roles ?? []).join(', ');
                const passwordDraft = passwordDrafts[user.actorId] ?? '';
                const isPending = pendingIdentityActorId === user.actorId;

                return (
                  <article className="audit-entry" key={user.actorId}>
                    <div>
                      <p className="audit-entry__meta">{user.actorId}</p>
                      <h4 className="audit-entry__title">{user.displayName}</h4>
                    </div>

                    <p className="module-card__body">
                      <span>{user.userName}</span>
                      <span>{user.enabled ? 'enabled' : 'disabled'}</span>
                      <span>{(user.roles ?? []).join(', ')}</span>
                    </p>

                    <label className="field">
                      <span className="field__label">Roles</span>
                      <input
                        className="field__input"
                        disabled={!canManageIdentityUsers || isPending}
                        onChange={(event) => setRoleDrafts((current) => ({
                          ...current,
                          [user.actorId]: event.target.value,
                        }))}
                        value={roleDraft}
                      />
                    </label>

                    <label className="field">
                      <span className="field__label">Reset password</span>
                      <input
                        className="field__input"
                        disabled={!canManageIdentityUsers || isPending}
                        onChange={(event) => setPasswordDrafts((current) => ({
                          ...current,
                          [user.actorId]: event.target.value,
                        }))}
                        type="password"
                        value={passwordDraft}
                      />
                    </label>

                    <div className="module-card__actions">
                      <button className="button-ghost" disabled={!canManageIdentityUsers || isPending} type="button" onClick={() => {
                        void saveRoles(user);
                      }}>
                        Save roles
                      </button>
                      <button className="button-ghost" disabled={!canManageIdentityUsers || isPending || passwordDraft.trim().length === 0} type="button" onClick={() => {
                        void resetPassword(user);
                      }}>
                        Reset password
                      </button>
                      <button className="button-ghost" disabled={!canManageIdentityUsers || isPending} type="button" onClick={() => {
                        void revokeSessions(user);
                      }}>
                        Revoke sessions
                      </button>
                      <button className="button" disabled={!canManageIdentityUsers || isPending} type="button" onClick={() => {
                        void toggleStatus(user);
                      }}>
                        {user.enabled ? 'Disable' : 'Enable'}
                      </button>
                    </div>

                    <div className="module-card__actions">
                      <span className="pill" data-tone={user.enabled ? 'healthy' : 'warning'}>{user.enabled ? 'active' : 'disabled'}</span>
                      <span className="audit-entry__meta">updated {new Date(user.updatedUtc).toLocaleString()}</span>
                    </div>
                  </article>
                );
              })}
            </div>
          </>
        )}
      </section>

      <section className="feature-panel platform-grid__full">
        <div className="platform-section__header">
          <div>
            <h3 className="platform-section__title">Operator guidance</h3>
            <p className="platform-section__subcopy">Admin reads published Knowledge Base guidance through a second shared-query seam instead of reaching into that module's API or persistence directly.</p>
          </div>
          <span className="pill">{guidanceQuery.data?.entries.length ?? 0} shared reads</span>
        </div>

        {guidanceQuery.error ? <p className="message" data-tone="danger">{getErrorMessage(guidanceQuery.error)}</p> : null}
        {guidanceQuery.isLoading && !guidanceQuery.data ? <p className="message">Loading guidance.</p> : null}

        {!guidanceQuery.isLoading && !hasGuidanceEntries(guidanceQuery.data) ? (
          <p className="message">No published guidance is available yet.</p>
        ) : (
          <div className="audit-grid">
            {guidanceQuery.data?.entries.map((entry) => (
              <article className="audit-entry" key={entry.entryId}>
                <div>
                  <p className="audit-entry__meta">{entry.category}</p>
                  <h4 className="audit-entry__title">{entry.title}</h4>
                </div>

                <p className="module-card__body">{entry.body}</p>

                <div className="module-card__actions">
                  <span className="pill" data-tone={guidanceTone(entry.featured)}>{entry.featured ? 'featured' : 'published'}</span>
                  <span className="pill">knowledge-base/{entry.slug}</span>
                  <span className="audit-entry__meta">{new Date(entry.publishedUtc).toLocaleString()}</span>
                </div>
              </article>
            ))}
          </div>
        )}
      </section>

      <section className="feature-panel platform-grid__full">
        <div className="platform-section__header">
          <div>
            <h3 className="platform-section__title">Projected announcement feed</h3>
            <p className="platform-section__subcopy">This is the Admin module's live read model for participating module publications, including SampleFeature and KnowledgeBase.</p>
          </div>
          <span className="pill">{announcements.length} tracked</span>
        </div>

        {announcementsQuery.isLoading && announcements.length === 0 ? <p className="message">Loading recent projections.</p> : null}

        {!announcementsQuery.isLoading && announcements.length === 0 ? (
          <p className="message">No announcements have been projected yet.</p>
        ) : (
          <div className="audit-grid">
            {announcements.map((announcement) => (
              <article className="audit-entry" key={announcement.announcementId}>
                <div>
                  <p className="audit-entry__meta">announcement {announcement.announcementId.slice(0, 8)}</p>
                  <h4 className="audit-entry__title">{announcement.title}</h4>
                </div>
                <p className="module-card__body">{announcement.body}</p>
                <div className="module-card__actions">
                  <span className="pill" data-tone="healthy">{formatAnnouncementSource(announcement)}</span>
                  <span className="audit-entry__meta">
                    {new Date(announcement.publishedUtc).toLocaleString()} by {announcement.publishedByActorId}
                  </span>
                </div>
              </article>
            ))}
          </div>
        )}
      </section>
    </div>
  );
}
