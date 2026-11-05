import { defineStore } from 'pinia'
import type { TruckEvent } from '../api/types'

export const useLiveEventsStore = defineStore('live-events', {
  state: () => ({
    events: [] as TruckEvent[],
    socket: undefined as WebSocket | undefined,
    reconnectTimer: undefined as number | undefined,
    wanted: false,
    connected: false,
  }),
  actions: {
    connect() {
      this.wanted = true
      if (this.socket?.readyState === WebSocket.OPEN || this.socket?.readyState === WebSocket.CONNECTING) return
      if (this.reconnectTimer) window.clearTimeout(this.reconnectTimer)
      const protocol = location.protocol === 'https:' ? 'wss' : 'ws'
      const socket = new WebSocket(`${protocol}://${location.host}/ws/v1/truck-events`)
      socket.onopen = () => { if (this.socket === socket) this.connected = true }
      socket.onmessage = ({ data }) => {
        try {
          const event = JSON.parse(data) as TruckEvent
          if (!event.eventId) return
          this.events = [event, ...this.events.filter((item) => item.eventId !== event.eventId)].slice(0, 20)
        } catch { /* Ignore malformed messages; REST remains the source of truth. */ }
      }
      socket.onclose = () => {
        if (this.socket !== socket) return
        this.socket = undefined
        this.connected = false
        if (this.wanted) this.reconnectTimer = window.setTimeout(() => this.connect(), 5000)
      }
      this.socket = socket
    },
    disconnect() {
      this.wanted = false
      this.connected = false
      if (this.reconnectTimer) window.clearTimeout(this.reconnectTimer)
      this.reconnectTimer = undefined
      this.socket?.close()
      this.socket = undefined
    },
  },
})
