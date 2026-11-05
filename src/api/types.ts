export type DetectionResult = 'NORMAL' | 'ALARM' | 'REVIEW_REQUIRED'
export type Quality = 'GOOD' | 'PARTIAL' | 'REVIEW_REQUIRED'
export type AlarmStatus = 'OPEN' | 'ACKNOWLEDGED'
export type FileType = 'PLATE_IMAGE' | 'TOP_IMAGE' | 'POINT_CLOUD' | 'RESULT_JSON' | 'MANIFEST'

export interface TruckEvent {
  eventId: string
  eventVersion: number
  siteId: string
  laneId: string
  deviceId: string
  cameraEventId?: string
  plateNo: string
  plateConfidence?: number
  plateTime?: string
  passStartTime?: string
  passEndTime?: string
  result: DetectionResult
  quality?: Quality
  qualityReason?: string
  loadVolumeM3?: number
  maxHeightMm?: number
  leftRightRatio?: number
  centerOffsetMm?: number
  alarmTypes: string[]
  algorithmVersion: string
  calibrationId: string
  committedAt?: string
}

export interface Page<T> {
  content: T[]
  totalElements: number
  totalPages: number
  number: number
  size: number
}

export interface MediaRef { fileId: number; fileType: FileType; size: number; sha256: string }
export interface TruckEventDetail { event: TruckEvent; media: MediaRef[] }
export interface Alarm {
  alarmId: number; eventId: string; plateNo: string; status: AlarmStatus
  alarmTypes: string[]; acknowledgedBy?: string; acknowledgedAt?: string
  note?: string; createdAt: string
}
export interface Device {
  deviceId: string; enabled: boolean; lastSeenAt?: string; online: boolean
  diskUsagePercent?: number; pendingSyncCount?: number; updatedAt: string
}

