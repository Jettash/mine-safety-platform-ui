import { api } from './http'
import type { Alarm, AlarmStatus, DetectionResult, Device, Page, TruckEvent, TruckEventDetail } from './types'

export function queryEvents(options: { page?: number; size?: number; plateNo?: string; result?: DetectionResult } = {}) {
  const query = new URLSearchParams()
  query.set('page', String(options.page ?? 0))
  query.set('size', String(options.size ?? 20))
  if (options.plateNo) query.set('plateNo', options.plateNo)
  if (options.result) query.set('result', options.result)
  return api<Page<TruckEvent>>(`/api/v1/truck-events?${query}`)
}

export const getEvent = (eventId: string) => api<TruckEventDetail>(`/api/v1/truck-events/${eventId}`)
export const reviewEvent = (eventId: string, body: { plateNo: string; result: DetectionResult; reason: string }) =>
  api<TruckEvent>(`/api/v1/truck-events/${eventId}/review`, { method: 'POST', body: JSON.stringify(body) })

export function queryAlarms(status?: AlarmStatus, page = 0, size = 20) {
  const query = new URLSearchParams({ page: String(page), size: String(size) })
  if (status) query.set('status', status)
  return api<Page<Alarm>>(`/api/v1/alarms?${query}`)
}
export const acknowledgeAlarm = (alarmId: number, note: string) =>
  api<Alarm>(`/api/v1/alarms/${alarmId}/ack`, { method: 'POST', body: JSON.stringify({ note }) })
export const queryDevices = () => api<Device[]>('/api/v1/devices')

