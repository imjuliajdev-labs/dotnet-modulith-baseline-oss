import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { SampleFeature } from '../../src/features/sampleFeature/SampleFeature';
import type { useGetCurrentActorQuery, useGetPreferredTimeZoneQuery } from '../../src/shell/auth/authApi';
import type {
  useListScheduledAnnouncementsQuery,
  usePublishAnnouncementMutation,
  useScheduleAnnouncementMutation,
} from '../../src/features/sampleFeature/sampleFeatureApi';

type GetCurrentActorResult = ReturnType<typeof useGetCurrentActorQuery>;
type GetPreferredTimeZoneResult = ReturnType<typeof useGetPreferredTimeZoneQuery>;
type ListScheduledAnnouncementsResult = ReturnType<typeof useListScheduledAnnouncementsQuery>;
type PublishAnnouncementMutationResult = ReturnType<typeof usePublishAnnouncementMutation>;
type ScheduleAnnouncementMutationResult = ReturnType<typeof useScheduleAnnouncementMutation>;

const useGetCurrentActorQueryMock = vi.fn<() => GetCurrentActorResult>();
const useGetPreferredTimeZoneQueryMock = vi.fn<() => GetPreferredTimeZoneResult>();
const useListScheduledAnnouncementsQueryMock = vi.fn<() => ListScheduledAnnouncementsResult>();
const usePublishAnnouncementMutationMock = vi.fn<() => PublishAnnouncementMutationResult>();
const useScheduleAnnouncementMutationMock = vi.fn<() => ScheduleAnnouncementMutationResult>();

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

vi.mock('../../src/shell/auth/authApi', () => ({
  useGetCurrentActorQuery: () => useGetCurrentActorQueryMock(),
  useGetPreferredTimeZoneQuery: () => useGetPreferredTimeZoneQueryMock(),
}));

vi.mock('../../src/features/sampleFeature/sampleFeatureApi', () => ({
  useListScheduledAnnouncementsQuery: () => useListScheduledAnnouncementsQueryMock(),
  usePublishAnnouncementMutation: () => usePublishAnnouncementMutationMock(),
  useScheduleAnnouncementMutation: () => useScheduleAnnouncementMutationMock(),
}));

describe('SampleFeature', () => {
  beforeEach(() => {
    useGetCurrentActorQueryMock.mockReset();
    useGetPreferredTimeZoneQueryMock.mockReset();
    useListScheduledAnnouncementsQueryMock.mockReset();
    usePublishAnnouncementMutationMock.mockReset();
    useScheduleAnnouncementMutationMock.mockReset();

    useGetCurrentActorQueryMock.mockReturnValue(createQueryResult({
      actorId: 'identity:seeded-admin',
      displayName: 'Baseline Admin',
      roles: ['Admin'],
      userName: 'admin',
    }) as GetCurrentActorResult);
    useGetPreferredTimeZoneQueryMock.mockReturnValue(createQueryResult({
      preferredTimeZoneId: 'Etc/UTC',
    }) as GetPreferredTimeZoneResult);
    useListScheduledAnnouncementsQueryMock.mockReturnValue(createQueryResult({ announcements: [] }) as ListScheduledAnnouncementsResult);
    usePublishAnnouncementMutationMock.mockReturnValue(createMutationHookResult<PublishAnnouncementMutationResult>(vi.fn()));
    useScheduleAnnouncementMutationMock.mockReturnValue(createMutationHookResult<ScheduleAnnouncementMutationResult>(vi.fn()));
  });

  it('renders the SampleFeature slice without depending on cross-module reads', () => {
    render(<SampleFeature />);

    expect(screen.getByRole('heading', { name: 'Sample feature slice' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Publish an announcement' })).toBeInTheDocument();
    expect(screen.getByText('standalone')).toBeInTheDocument();
    expect(screen.getByText('outbox publication')).toBeInTheDocument();
  });

  it('submits a new announcement through the SampleFeature write endpoint when the Admin role is present', async () => {
    const user = userEvent.setup();
    const publishAnnouncement = vi.fn().mockReturnValue({
      unwrap: vi.fn().mockResolvedValue({
        announcementId: '33333333-3333-3333-3333-333333333333',
        body: 'Published body',
        publishedAt: '2026-04-05T12:45:00Z',
        publishedByActorId: 'identity:seeded-admin',
        title: 'Shared write title',
      }),
    });

    usePublishAnnouncementMutationMock.mockReturnValue(createMutationHookResult<PublishAnnouncementMutationResult>(publishAnnouncement));

    render(<SampleFeature />);

    await user.clear(screen.getByLabelText('Announcement title'));
    await user.type(screen.getByLabelText('Announcement title'), 'Shared write title');
    await user.clear(screen.getByLabelText('Announcement body'));
    await user.type(screen.getByLabelText('Announcement body'), 'Shared write body');
    await user.click(screen.getByRole('button', { name: 'Publish announcement' }));

    expect(publishAnnouncement).toHaveBeenCalledWith({
      body: 'Shared write body',
      title: 'Shared write title',
    });
    expect(await screen.findByText(/Published "Shared write title"/)).toBeInTheDocument();
  });
});
