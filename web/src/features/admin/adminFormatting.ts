import type { AdminAnnouncementReadModel, AdminGuidanceListResponse } from '../../shared/api/base/contracts';

export type RealtimeState = 'idle' | 'connecting' | 'live' | 'degraded';

export function toneForRealtimeState(value: RealtimeState) {
  if (value === 'live') {
    return 'healthy';
  }

  if (value === 'degraded') {
    return 'danger';
  }

  return 'warning';
}

export function labelForRealtimeState(value: RealtimeState) {
  if (value === 'live') {
    return 'live sync';
  }

  if (value === 'degraded') {
    return 'reconnect needed';
  }

  if (value === 'connecting') {
    return 'connecting';
  }

  return 'inactive';
}

export function formatAnnouncementSource(announcement: AdminAnnouncementReadModel) {
  const sourceModuleKey = announcement.sourceModuleKey?.trim();
  const sourceReference = announcement.sourceReference?.trim();

  if (!sourceModuleKey) {
    return 'projected';
  }

  return sourceReference
    ? `${sourceModuleKey}/${sourceReference}`
    : sourceModuleKey;
}

export function guidanceTone(featured: boolean) {
  return featured ? 'healthy' : 'warning';
}

export function hasGuidanceEntries(guidance: AdminGuidanceListResponse | undefined): guidance is AdminGuidanceListResponse {
  return (guidance?.entries.length ?? 0) > 0;
}
