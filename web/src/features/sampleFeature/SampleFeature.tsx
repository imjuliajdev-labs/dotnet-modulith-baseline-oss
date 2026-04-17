import { useEffect, useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useGetCurrentActorQuery, useGetPreferredTimeZoneQuery } from '../../shell/auth/authApi';
import { getErrorMessage, isUnauthorizedError } from '../../shared/lib/queryErrors';
import { useListScheduledAnnouncementsQuery, usePublishAnnouncementMutation, useScheduleAnnouncementMutation } from './sampleFeatureApi';

const publishSchema = z.object({
  body: z.string().min(1, 'Body is required'),
  title: z.string().min(1, 'Title is required'),
});

type PublishFormData = z.infer<typeof publishSchema>;

const scheduleSchema = z.object({
  scheduledBody: z.string().min(1, 'Body is required'),
  scheduledLocalDate: z.string().min(1, 'Date is required'),
  scheduledLocalTime: z.string().min(1, 'Time is required'),
  scheduledTitle: z.string().min(1, 'Title is required'),
});

type ScheduleFormData = z.infer<typeof scheduleSchema>;

function formatPublishedUtc(value: string) {
  return new Intl.DateTimeFormat('en', {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(value));
}

export function SampleFeature() {
  const sessionQuery = useGetCurrentActorQuery();
  const session = isUnauthorizedError(sessionQuery.error) ? null : sessionQuery.data ?? null;
  const preferredTimeZoneQuery = useGetPreferredTimeZoneQuery();
  const canWrite = session?.roles?.includes('Admin') ?? false;
  const preferredTimeZoneId = isUnauthorizedError(preferredTimeZoneQuery.error)
    ? 'Etc/UTC'
    : preferredTimeZoneQuery.data?.preferredTimeZoneId ?? 'Etc/UTC';
  const [lastPublishedTitle, setLastPublishedTitle] = useState<string | null>(null);
  const [lastScheduledAnnouncementId, setLastScheduledAnnouncementId] = useState<string | null>(null);
  const [lastScheduledTitle, setLastScheduledTitle] = useState<string | null>(null);
  const [mutationError, setMutationError] = useState<string | null>(null);
  const scheduledAnnouncementsQuery = useListScheduledAnnouncementsQuery(undefined, {
    pollingInterval: lastScheduledAnnouncementId ? 2000 : 0,
  });
  const [publishAnnouncement, publishAnnouncementState] = usePublishAnnouncementMutation();
  const [scheduleAnnouncement, scheduleAnnouncementState] = useScheduleAnnouncementMutation();
  const scheduledAnnouncements = scheduledAnnouncementsQuery.data?.announcements ?? [];
  const scheduleSettled = lastScheduledAnnouncementId === null
    || scheduledAnnouncements.some((announcement) => announcement.scheduledAnnouncementId === lastScheduledAnnouncementId);

  const publishForm = useForm<PublishFormData>({
    defaultValues: {
      body: 'Published from the SampleFeature screen to prove the write, idempotency, and outbox loop.',
      title: 'Operational announcement',
    },
    resolver: zodResolver(publishSchema),
  });

  const scheduleForm = useForm<ScheduleFormData>({
    defaultValues: {
      scheduledBody: 'Scheduled from a browser-local form while the backend resolves the current actor preference into UTC.',
      scheduledLocalDate: '2026-11-01',
      scheduledLocalTime: '01:30',
      scheduledTitle: 'DST-sensitive announcement',
    },
    resolver: zodResolver(scheduleSchema),
  });

  useEffect(() => {
    if (scheduleSettled && lastScheduledAnnouncementId) {
      setLastScheduledAnnouncementId(null);
    }
  }, [lastScheduledAnnouncementId, scheduleSettled]);

  async function onSubmit(data: PublishFormData) {
    setMutationError(null);

    try {
      const published = await publishAnnouncement(data).unwrap();
      setLastPublishedTitle(published.title);
      publishForm.reset({ body: '', title: '' });
    }
    catch (error) {
      setMutationError(getErrorMessage(error));
    }
  }

  async function onSchedule(data: ScheduleFormData) {
    setMutationError(null);

    try {
      const scheduled = await scheduleAnnouncement({
        body: data.scheduledBody,
        scheduledLocalDate: data.scheduledLocalDate,
        scheduledLocalTime: data.scheduledLocalTime,
        title: data.scheduledTitle,
      }).unwrap();

      setLastScheduledAnnouncementId(scheduled.scheduledAnnouncementId);
      setLastScheduledTitle(scheduled.title);
      scheduleForm.reset({ scheduledBody: '', scheduledLocalDate: '', scheduledLocalTime: '', scheduledTitle: '' });
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
            <h2 className="feature-panel__title">Sample feature slice</h2>
            <p className="feature-panel__copy">SampleFeature is the smallest end-to-end governed module: it persists an aggregate, enforces idempotent writes, publishes a public integration event through its own outbox, and stays fully self-contained from other teaching modules.</p>
          </div>
          <span className="pill" data-tone="healthy">standalone</span>
        </div>

        <ul className="shell-list">
          <li>
            <span>The shell route is separate from the backend transport prefix, so UI navigation never collides with API endpoints.</span>
            <span className="pill">safe routing</span>
          </li>
          <li>
            <span>Writes flow through the dispatcher, persist locally, and emit a public integration event that any other module can consume.</span>
            <span className="pill">outbox publication</span>
          </li>
          <li>
            <span>Scheduling resolves browser-supplied local date and time through the current actor's preferred IANA zone on the server.</span>
            <span className="pill">dst aware</span>
          </li>
        </ul>
      </section>

      <section className="feature-panel dashboard-grid__full">
        <div className="feature-panel__header">
          <div>
            <h3 className="feature-panel__title">Publish an announcement</h3>
            <p className="feature-panel__copy">Use the real SampleFeature write endpoint. The endpoint enforces an idempotency key, persists the aggregate, and queues a public integration event in the module-owned outbox.</p>
          </div>
          <span className="pill" data-tone={canWrite ? 'healthy' : 'warning'}>{canWrite ? 'write enabled' : 'read only'}</span>
        </div>

        {lastPublishedTitle ? (
          <p className="message">Published &quot;{lastPublishedTitle}&quot; through the SampleFeature dispatcher and outbox.</p>
        ) : null}

        {mutationError ? <p className="message" data-tone="danger">{mutationError}</p> : null}

        {canWrite ? (
          <form className="auth-panel__form" onSubmit={(event) => {
            void publishForm.handleSubmit(onSubmit)(event);
          }}>
            <label className="field">
              <span className="field__label">Announcement title</span>
              <input
                className="field__input"
                disabled={publishAnnouncementState.isLoading}
                {...publishForm.register('title')}
                aria-invalid={!!publishForm.formState.errors.title}
                aria-describedby={publishForm.formState.errors.title ? 'publish-title-error' : undefined}
              />
            </label>
            {publishForm.formState.errors.title && (
              <span id="publish-title-error" role="alert" className="message" data-tone="danger">{publishForm.formState.errors.title.message}</span>
            )}

            <label className="field">
              <span className="field__label">Announcement body</span>
              <textarea
                className="field__input"
                disabled={publishAnnouncementState.isLoading}
                rows={4}
                {...publishForm.register('body')}
                aria-invalid={!!publishForm.formState.errors.body}
                aria-describedby={publishForm.formState.errors.body ? 'publish-body-error' : undefined}
              />
            </label>
            {publishForm.formState.errors.body && (
              <span id="publish-body-error" role="alert" className="message" data-tone="danger">{publishForm.formState.errors.body.message}</span>
            )}

            <div className="module-card__actions">
              <button className="button" disabled={publishAnnouncementState.isLoading} type="submit">
                {publishAnnouncementState.isLoading ? 'Publishing\u2026' : 'Publish announcement'}
              </button>
            </div>
          </form>
        ) : (
          <p className="message" data-tone="warning">The Admin role is required to publish from this screen.</p>
        )}
      </section>

      <section className="feature-panel dashboard-grid__full">
        <div className="feature-panel__header">
          <div>
            <h3 className="feature-panel__title">Schedule by local time</h3>
            <p className="feature-panel__copy">This uses your current preferred IANA time zone from Identity. The backend resolves gaps and ambiguities at scheduling time and stores the exact UTC due instant.</p>
          </div>
          <span className="pill" data-tone={canWrite ? 'healthy' : 'warning'}>{preferredTimeZoneId}</span>
        </div>

        {lastScheduledTitle ? (
          <p className="message" data-tone={scheduleSettled ? undefined : 'warning'}>
            {scheduleSettled
              ? `Scheduled "${lastScheduledTitle}" using ${preferredTimeZoneId}.`
              : `Scheduled "${lastScheduledTitle}" and refreshing the scheduled queue.`}
          </p>
        ) : null}

        {canWrite ? (
          <form className="auth-panel__form" onSubmit={(event) => {
            void scheduleForm.handleSubmit(onSchedule)(event);
          }}>
            <label className="field">
              <span className="field__label">Scheduled title</span>
              <input
                className="field__input"
                disabled={scheduleAnnouncementState.isLoading}
                {...scheduleForm.register('scheduledTitle')}
                aria-invalid={!!scheduleForm.formState.errors.scheduledTitle}
                aria-describedby={scheduleForm.formState.errors.scheduledTitle ? 'schedule-title-error' : undefined}
              />
            </label>
            {scheduleForm.formState.errors.scheduledTitle && (
              <span id="schedule-title-error" role="alert" className="message" data-tone="danger">{scheduleForm.formState.errors.scheduledTitle.message}</span>
            )}

            <label className="field">
              <span className="field__label">Scheduled body</span>
              <textarea
                className="field__input"
                disabled={scheduleAnnouncementState.isLoading}
                rows={4}
                {...scheduleForm.register('scheduledBody')}
                aria-invalid={!!scheduleForm.formState.errors.scheduledBody}
                aria-describedby={scheduleForm.formState.errors.scheduledBody ? 'schedule-body-error' : undefined}
              />
            </label>
            {scheduleForm.formState.errors.scheduledBody && (
              <span id="schedule-body-error" role="alert" className="message" data-tone="danger">{scheduleForm.formState.errors.scheduledBody.message}</span>
            )}

            <label className="field">
              <span className="field__label">Local date</span>
              <input
                className="field__input"
                disabled={scheduleAnnouncementState.isLoading}
                type="date"
                {...scheduleForm.register('scheduledLocalDate')}
                aria-invalid={!!scheduleForm.formState.errors.scheduledLocalDate}
                aria-describedby={scheduleForm.formState.errors.scheduledLocalDate ? 'schedule-date-error' : undefined}
              />
            </label>
            {scheduleForm.formState.errors.scheduledLocalDate && (
              <span id="schedule-date-error" role="alert" className="message" data-tone="danger">{scheduleForm.formState.errors.scheduledLocalDate.message}</span>
            )}

            <label className="field">
              <span className="field__label">Local time</span>
              <input
                className="field__input"
                disabled={scheduleAnnouncementState.isLoading}
                step={60}
                type="time"
                {...scheduleForm.register('scheduledLocalTime')}
                aria-invalid={!!scheduleForm.formState.errors.scheduledLocalTime}
                aria-describedby={scheduleForm.formState.errors.scheduledLocalTime ? 'schedule-time-error' : undefined}
              />
            </label>
            {scheduleForm.formState.errors.scheduledLocalTime && (
              <span id="schedule-time-error" role="alert" className="message" data-tone="danger">{scheduleForm.formState.errors.scheduledLocalTime.message}</span>
            )}

            <div className="module-card__actions">
              <button className="button" disabled={scheduleAnnouncementState.isLoading} type="submit">
                {scheduleAnnouncementState.isLoading ? 'Scheduling\u2026' : 'Schedule announcement'}
              </button>
            </div>
          </form>
        ) : (
          <p className="message" data-tone="warning">Grant `sample-feature.announcements.write` to schedule announcements from this screen.</p>
        )}
      </section>

      <section className="feature-panel dashboard-grid__full">
        <div className="feature-panel__header">
          <div>
            <h3 className="feature-panel__title">Scheduled queue</h3>
            <p className="feature-panel__copy">These entries are stored with your local date/time, preferred zone, resolution strategy, and exact UTC due instant.</p>
          </div>
          <span className="pill">{scheduledAnnouncements.length} scheduled</span>
        </div>

        {scheduledAnnouncementsQuery.error ? (
          <p className="message" data-tone="danger">{getErrorMessage(scheduledAnnouncementsQuery.error)}</p>
        ) : null}

        {!scheduledAnnouncementsQuery.isLoading && scheduledAnnouncements.length === 0 ? (
          <p className="message" data-tone="warning">No scheduled SampleFeature announcements exist for this actor yet.</p>
        ) : null}

        <div className="module-grid">
          {scheduledAnnouncements.map((announcement) => (
            <article className="module-card" key={announcement.scheduledAnnouncementId}>
              <div className="module-card__header">
                <div>
                  <p className="module-card__meta">scheduled in {announcement.timeZoneId}</p>
                  <h3 className="module-card__title">{announcement.title}</h3>
                </div>
                <span className="pill" data-tone={announcement.status === 'published' ? 'healthy' : 'warning'}>{announcement.status}</span>
              </div>

              <div className="module-card__body">
                <span>{announcement.body}</span>
                <span>local: {announcement.scheduledLocalDate} {announcement.scheduledLocalTime}</span>
                <span>resolution: {announcement.localTimeResolution}</span>
                <span>due utc: {formatPublishedUtc(announcement.scheduledForUtc)}</span>
              </div>
            </article>
          ))}
        </div>
      </section>
    </div>
  );
}
