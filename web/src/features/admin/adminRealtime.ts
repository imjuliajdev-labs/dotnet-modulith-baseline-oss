import type { AdminAnnouncementReadModel } from '../../shared/api/base/contracts';
import { BrowserRealtimeAdapter } from '../../shared/realtime/browserRealtime';
import { createSignalRBrowserRealtimeConnectionFactory } from '../../shared/realtime/signalRBrowserRealtime';

export const ADMIN_ANNOUNCEMENT_PROJECTED_CHANNEL = 'admin.announcement.projected';

export function createAdminRealtimeAdapter() {
  return new BrowserRealtimeAdapter(createSignalRBrowserRealtimeConnectionFactory());
}

export function upsertAdminAnnouncement(
  announcements: readonly AdminAnnouncementReadModel[],
  candidate: AdminAnnouncementReadModel,
) {
  const next = announcements.filter((announcement) => announcement.announcementId !== candidate.announcementId);
  next.unshift(candidate);

  next.sort((left, right) => {
    const publishedUtcComparison = Date.parse(right.publishedUtc) - Date.parse(left.publishedUtc);
    if (publishedUtcComparison !== 0) {
      return publishedUtcComparison;
    }

    return right.announcementId.localeCompare(left.announcementId);
  });

  return next;
}