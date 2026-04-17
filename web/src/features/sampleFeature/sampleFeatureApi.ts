import { starterApi } from '../../shared/api/base/starterBaseApi';
import { createIdempotencyKey } from '../../shared/api/base/idempotencyKeys';
import type {
  PublishSampleAnnouncementRequest,
  PublishedSampleAnnouncementResponse,
  ScheduleSampleAnnouncementRequest,
  ScheduledSampleAnnouncementListResponse,
  ScheduledSampleAnnouncementResponse,
} from '../../shared/api/base/contracts';

export const sampleFeatureApi = starterApi.injectEndpoints({
  endpoints: (builder) => ({
    publishAnnouncement: builder.mutation<PublishedSampleAnnouncementResponse, PublishSampleAnnouncementRequest>({
      invalidatesTags: ['SampleFeatureAnnouncements'],
      query: (body) => ({
        body,
        headers: {
          'Idempotency-Key': createIdempotencyKey(),
        },
        method: 'POST',
        url: '/api/v1/sample-feature/announcements',
      }),
    }),
    listScheduledAnnouncements: builder.query<ScheduledSampleAnnouncementListResponse, number | void>({
      providesTags: ['SampleFeatureAnnouncements'],
      query: (limit = 10) => `/api/v1/sample-feature/announcements/scheduled?limit=${limit ?? 10}`,
    }),
    scheduleAnnouncement: builder.mutation<ScheduledSampleAnnouncementResponse, ScheduleSampleAnnouncementRequest>({
      invalidatesTags: ['SampleFeatureAnnouncements'],
      query: (body) => ({
        body,
        headers: {
          'Idempotency-Key': createIdempotencyKey(),
        },
        method: 'POST',
        url: '/api/v1/sample-feature/announcements/scheduled',
      }),
    }),
  }),
});

export const {
  useListScheduledAnnouncementsQuery,
  usePublishAnnouncementMutation,
  useScheduleAnnouncementMutation,
} = sampleFeatureApi;
